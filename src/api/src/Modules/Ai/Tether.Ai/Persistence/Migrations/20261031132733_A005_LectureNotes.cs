using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// PO-18d "Make notes" after Stop. Adds <c>ai.lecture_notes</c>: one row per deliberate press (the durable request the gateway checks before
    /// every after-Stop provider call) and the derived notes it produced, stored apart from the transcript (invariant 5). Composite FK
    /// <c>(session_id, user_id)</c> to <c>lectures.lecture_sessions</c> with ON DELETE CASCADE, like summary_revisions and catch_ups (ADR-019), so
    /// a session delete removes it and a row can never name another user than its session. Partial unique index: one Pending press per session.
    /// Widens <c>ck_ai_usage_workflow</c> with the two lecture-pool workflows <c>Notes</c> and <c>FinalSummary</c>. Migrate after Lectures.
    /// Down is GUARDED: it refuses once ledger rows for the new workflows exist (spend evidence is never deleted or relabelled; forward-fix).
    /// </remarks>
    public partial class A005_LectureNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.CreateTable(
                name: "lecture_notes",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sections = table.Column<string>(type: "jsonb", nullable: false),
                    covered_from_ms = table.Column<long>(type: "bigint", nullable: true),
                    covered_to_ms = table.Column<long>(type: "bigint", nullable: true),
                    source_from_seq = table.Column<long>(type: "bigint", nullable: true),
                    source_to_seq = table.Column<long>(type: "bigint", nullable: true),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    final_summary_revision = table.Column<int>(type: "integer", nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lecture_notes", x => x.id);
                    table.CheckConstraint("ck_lecture_notes_content", "(state = 'Ready' AND completed_at IS NOT NULL AND covered_from_ms IS NOT NULL) OR (state <> 'Ready' AND sections = '[]'::jsonb AND final_summary_revision IS NULL AND covered_from_ms IS NULL AND NOT truncated)");
                    table.CheckConstraint("ck_lecture_notes_key", "length(idempotency_key) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_lecture_notes_pending", "state <> 'Pending' OR completed_at IS NULL");
                    table.CheckConstraint("ck_lecture_notes_range", "(covered_from_ms IS NULL AND covered_to_ms IS NULL) OR (covered_from_ms >= 0 AND covered_from_ms <= covered_to_ms)");
                    table.CheckConstraint("ck_lecture_notes_revision", "revision > 0");
                    table.CheckConstraint("ck_lecture_notes_state", "state IN ('Pending','Ready','Unavailable','BudgetLimited','NotNeeded')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage",
                sql: "(scope_kind = 'lecture' AND workflow IN ('RollingSummary','Compaction','QuickRecap','Notes','FinalSummary')) OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract')");

            migrationBuilder.CreateIndex(
                name: "ux_lecture_notes_idempotency",
                schema: "ai",
                table: "lecture_notes",
                columns: new[] { "user_id", "session_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_lecture_notes_pending",
                schema: "ai",
                table: "lecture_notes",
                column: "session_id",
                unique: true,
                filter: "state = 'Pending'");

            // Cross-schema, composite, cascade (allow-listed in PersistenceTests): Lectures is migrated first.
            migrationBuilder.Sql("""
                ALTER TABLE ai.lecture_notes
                    ADD CONSTRAINT fk_lecture_notes_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_lecture_notes_session_revision",
                schema: "ai",
                table: "lecture_notes",
                columns: new[] { "session_id", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ai.ai_usage WHERE workflow IN ('Notes','FinalSummary')) THEN
                        RAISE EXCEPTION 'A005 cannot be rolled back: notes ledger rows exist (forward-fix instead)';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("ALTER TABLE ai.lecture_notes DROP CONSTRAINT fk_lecture_notes_session_user;");

            migrationBuilder.DropTable(
                name: "lecture_notes",
                schema: "ai");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage",
                sql: "(scope_kind = 'lecture' AND workflow IN ('RollingSummary','Compaction','QuickRecap')) OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract')");
        }
    }
}
