using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Lectures.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class M005_Lectures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lectures");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    course_attested = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_records", x => x.id);
                    table.CheckConstraint("ck_consent_records_attested", "course_attested");
                    table.CheckConstraint("ck_consent_records_kind", "kind IN ('mic_recording')");
                });

            migrationBuilder.CreateTable(
                name: "consent_events",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_events", x => x.id);
                    table.CheckConstraint("ck_consent_events_kind", "kind IN ('Start','Pause','Resume','Stop')");
                    table.ForeignKey(
                        name: "fk_consent_events_consent_record_id",
                        column: x => x.consent_record_id,
                        principalSchema: "lectures",
                        principalTable: "consent_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lecture_sessions",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consent_record_id = table.Column<Guid>(type: "uuid", nullable: false)
                    // NOTE: no xmin column; it is a PostgreSQL system column used as the concurrency token (LectureSession.Version).
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lecture_sessions", x => x.id);
                    table.CheckConstraint("ck_lecture_sessions_state", "state IN ('Active','Paused','Stopped','Failed')");
                    table.CheckConstraint("ck_lecture_sessions_stopped_at", "state NOT IN ('Active','Paused') OR stopped_at IS NULL");
                    table.ForeignKey(
                        name: "fk_lecture_sessions_consent_record_id",
                        column: x => x.consent_record_id,
                        principalSchema: "lectures",
                        principalTable: "consent_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audio_tickets",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auth_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_audio_tickets_session_id",
                        column: x => x.session_id,
                        principalSchema: "lectures",
                        principalTable: "lecture_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "lectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_idempotency_keys_session_id",
                        column: x => x.session_id,
                        principalSchema: "lectures",
                        principalTable: "lecture_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_counters",
                schema: "lectures",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_counters", x => x.session_id);
                    table.CheckConstraint("ck_session_counters_last_seq", "last_seq >= 0");
                    table.ForeignKey(
                        name: "fk_session_counters_session_id",
                        column: x => x.session_id,
                        principalSchema: "lectures",
                        principalTable: "lecture_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_events",
                schema: "lectures",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_events", x => new { x.session_id, x.seq });
                    table.CheckConstraint("ck_session_events_seq", "seq > 0");
                    table.CheckConstraint("ck_session_events_state", "state IN ('Active','Paused','Stopped','Failed')");
                    table.ForeignKey(
                        name: "fk_session_events_session_id",
                        column: x => x.session_id,
                        principalSchema: "lectures",
                        principalTable: "lecture_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_audio_tickets_session_id",
                schema: "lectures",
                table: "audio_tickets",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audio_tickets_token_hash",
                schema: "lectures",
                table: "audio_tickets",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_user_time",
                schema: "lectures",
                table: "audit_events",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_events_consent_record_id",
                schema: "lectures",
                table: "consent_events",
                column: "consent_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_events_user_session",
                schema: "lectures",
                table: "consent_events",
                columns: new[] { "user_id", "session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_session_id",
                schema: "lectures",
                table: "consent_records",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_user_course",
                schema: "lectures",
                table: "consent_records",
                columns: new[] { "user_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_session_id",
                schema: "lectures",
                table: "idempotency_keys",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_keys_user_key",
                schema: "lectures",
                table: "idempotency_keys",
                columns: new[] { "user_id", "key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lecture_sessions_consent_record_id",
                schema: "lectures",
                table: "lecture_sessions",
                column: "consent_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_lecture_sessions_user_course_id",
                schema: "lectures",
                table: "lecture_sessions",
                columns: new[] { "user_id", "course_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_lecture_sessions_id_user_id",
                schema: "lectures",
                table: "lecture_sessions",
                columns: new[] { "id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_lecture_sessions_one_live_per_user",
                schema: "lectures",
                table: "lecture_sessions",
                column: "user_id",
                unique: true,
                filter: "state IN ('Active','Paused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_tickets",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "consent_events",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "session_counters",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "session_events",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "lecture_sessions",
                schema: "lectures");

            migrationBuilder.DropTable(
                name: "consent_records",
                schema: "lectures");
        }
    }
}
