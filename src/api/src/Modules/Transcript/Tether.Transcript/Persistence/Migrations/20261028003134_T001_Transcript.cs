using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Transcript.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Cross-schema FKs are raw SQL by design (ADR-014/019/020): source transcript rows are deleted with the session (C-21), and the
    /// composite (session_id, user_id) key means a row cannot name another user than its session. Requires Lectures migrated first.
    /// </remarks>
    internal partial class T001_Transcript : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transcript");

            migrationBuilder.CreateTable(
                name: "audio_progress",
                schema: "transcript",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_ms = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_progress", x => x.session_id);
                    table.CheckConstraint("ck_audio_progress_ms", "accepted_ms >= 0");
                });

            migrationBuilder.CreateTable(
                name: "transcript_events",
                schema: "transcript",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    segment_no = table.Column<int>(type: "integer", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: true),
                    supersedes = table.Column<int>(type: "integer", nullable: true),
                    start_ms = table.Column<long>(type: "bigint", nullable: true),
                    end_ms = table.Column<long>(type: "bigint", nullable: true),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    from_ms = table.Column<long>(type: "bigint", nullable: true),
                    to_ms = table.Column<long>(type: "bigint", nullable: true),
                    gap_reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transcript_events", x => new { x.session_id, x.seq });
                    table.CheckConstraint("ck_transcript_events_kind", "kind IN ('Final','Correction','Gap')");
                    table.CheckConstraint("ck_transcript_events_seq", "seq > 0");
                    table.CheckConstraint("ck_transcript_events_shape", "(kind = 'Final' AND segment_no IS NOT NULL AND start_ms IS NOT NULL AND end_ms >= start_ms AND text IS NOT NULL AND provider IS NOT NULL)\nOR (kind = 'Correction' AND segment_no IS NOT NULL AND revision IS NOT NULL AND supersedes IS NOT NULL AND supersedes < revision AND text IS NOT NULL)\nOR (kind = 'Gap' AND from_ms IS NOT NULL AND to_ms >= from_ms AND gap_reason IN ('paused','network','device','provider'))");
                });

            migrationBuilder.CreateIndex(
                name: "ix_transcript_events_session_segment",
                schema: "transcript",
                table: "transcript_events",
                columns: new[] { "session_id", "segment_no" });

            migrationBuilder.Sql("""
                ALTER TABLE transcript.transcript_events
                    ADD CONSTRAINT fk_transcript_events_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE transcript.audio_progress
                    ADD CONSTRAINT fk_audio_progress_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE transcript.audio_progress DROP CONSTRAINT fk_audio_progress_session_user; ALTER TABLE transcript.transcript_events DROP CONSTRAINT fk_transcript_events_session_user;");

            migrationBuilder.DropTable(
                name: "audio_progress",
                schema: "transcript");

            migrationBuilder.DropTable(
                name: "transcript_events",
                schema: "transcript");
        }
    }
}
