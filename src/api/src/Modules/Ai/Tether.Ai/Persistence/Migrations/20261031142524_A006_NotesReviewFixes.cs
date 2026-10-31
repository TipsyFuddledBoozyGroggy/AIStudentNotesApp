using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Security review of PO-18d (docs/security/notes-after-stop-review.md). lecture_notes: <c>started_at</c> (NAS-03: the permission window
    /// starts when the job runs), <c>notice_version</c> (NAS-05), <c>final_summary_state</c> (NAS-03: the Final-summary outcome of a Ready press,
    /// never silent) and the partial unique index <c>ux_lecture_notes_pending_user</c> (one pending press per user). ai_usage: <c>press_id</c>
    /// (NAS-08: links each Notes/FinalSummary call to its audited press). Before the new index and CHECK, every Pending press becomes
    /// Unavailable (the startup sweep does the same on every boot; a pending press cannot survive a deploy anyway) and Ready rows get their
    /// final-summary outcome backfilled from <c>final_summary_revision</c>. Down is GUARDED (NAS-R06): it refuses while any evidence column
    /// holds data, and otherwise drops the (empty) columns.
    /// </remarks>
    internal partial class A006_NotesReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "final_summary_state",
                schema: "ai",
                table: "lecture_notes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_version",
                schema: "ai",
                table: "lecture_notes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                schema: "ai",
                table: "lecture_notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "press_id",
                schema: "ai",
                table: "ai_usage",
                type: "uuid",
                nullable: true);

            // Existing rows: no Pending press may survive (the per-user index and the started_at rule), and Ready rows get their outcome.
            migrationBuilder.Sql("""
                UPDATE ai.lecture_notes SET state = 'Unavailable', completed_at = now() WHERE state = 'Pending';
                UPDATE ai.lecture_notes SET final_summary_state = CASE WHEN final_summary_revision IS NULL THEN 'Unavailable' ELSE 'Ready' END WHERE state = 'Ready';
                """);

            migrationBuilder.CreateIndex(
                name: "ux_lecture_notes_pending_user",
                schema: "ai",
                table: "lecture_notes",
                column: "user_id",
                unique: true,
                filter: "state = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lecture_notes_final_state",
                schema: "ai",
                table: "lecture_notes",
                sql: "(state = 'Ready' AND final_summary_state IN ('Ready','Unavailable','BudgetLimited','NotNeeded') AND ((final_summary_state = 'Ready') = (final_summary_revision IS NOT NULL))) OR (state <> 'Ready' AND final_summary_state IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes",
                sql: "notice_version IS NULL OR notice_version ~ '^[A-Za-z0-9._-]{1,64}$'");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_press",
                schema: "ai",
                table: "ai_usage",
                column: "press_id",
                filter: "press_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_press",
                schema: "ai",
                table: "ai_usage",
                sql: "press_id IS NULL OR workflow IN ('Notes','FinalSummary')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NAS-R06: GUARDED like A005. The columns hold NAS-05/NAS-08 evidence (the notice a student saw, when a press started, which vendor
            // call a press caused); a rollback must never delete it silently. Forward-fix instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ai.lecture_notes WHERE notice_version IS NOT NULL OR started_at IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM ai.ai_usage WHERE press_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'A006 cannot be rolled back: notice, start or press evidence exists (forward-fix instead)';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_lecture_notes_pending_user",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lecture_notes_final_state",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropIndex(
                name: "ix_ai_usage_press",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_press",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.DropColumn(
                name: "final_summary_state",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropColumn(
                name: "notice_version",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropColumn(
                name: "started_at",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.DropColumn(
                name: "press_id",
                schema: "ai",
                table: "ai_usage");
        }
    }
}
