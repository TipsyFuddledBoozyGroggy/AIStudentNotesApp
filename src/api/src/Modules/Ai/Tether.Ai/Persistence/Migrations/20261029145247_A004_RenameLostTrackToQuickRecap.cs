using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A004_RenameLostTrackToQuickRecap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PO sensitive-language rule (2026-10-29): the feature is renamed Quick recap. Drop the CHECK first so the relabel is legal,
            // relabel stored workflow / prompt-version values, then re-add the CHECK with the new name.
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.Sql("UPDATE ai.ai_usage SET workflow = 'QuickRecap' WHERE workflow = 'LostTrack';");
            migrationBuilder.Sql("UPDATE ai.ai_usage SET prompt_version = 'quick-recap.v1' WHERE prompt_version = 'lost-track.v1';");
            migrationBuilder.Sql("UPDATE ai.catch_ups SET prompt_version = 'quick-recap.v1' WHERE prompt_version = 'lost-track.v1';");
            migrationBuilder.Sql("UPDATE ai.summary_revisions SET prompt_version = 'quick-recap.v1' WHERE prompt_version = 'lost-track.v1';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage",
                sql: "(scope_kind = 'lecture' AND workflow IN ('RollingSummary','Compaction','QuickRecap')) OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.Sql("UPDATE ai.summary_revisions SET prompt_version = 'lost-track.v1' WHERE prompt_version = 'quick-recap.v1';");
            migrationBuilder.Sql("UPDATE ai.catch_ups SET prompt_version = 'lost-track.v1' WHERE prompt_version = 'quick-recap.v1';");
            migrationBuilder.Sql("UPDATE ai.ai_usage SET prompt_version = 'lost-track.v1' WHERE prompt_version = 'quick-recap.v1';");
            migrationBuilder.Sql("UPDATE ai.ai_usage SET workflow = 'LostTrack' WHERE workflow = 'QuickRecap';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_workflow",
                schema: "ai",
                table: "ai_usage",
                sql: "(scope_kind = 'lecture' AND workflow IN ('RollingSummary','Compaction','LostTrack')) OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract')");
        }
    }
}
