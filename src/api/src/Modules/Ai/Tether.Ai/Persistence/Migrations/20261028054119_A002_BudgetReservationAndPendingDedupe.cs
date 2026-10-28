using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>Adds the reserved outcome (budget reservation, B3-02) and one in-flight catch-up per session and window (B3-05).</remarks>
    internal partial class A002_BudgetReservationAndPendingDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_outcome",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.CreateIndex(
                name: "ux_catch_ups_pending",
                schema: "ai",
                table: "catch_ups",
                columns: new[] { "session_id", "window_minutes" },
                unique: true,
                filter: "ai_state = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_outcome",
                schema: "ai",
                table: "ai_usage",
                sql: "outcome IN ('ok','repaired','invalid','degraded','error','timeout','rate_limited','budget_denied','stale','reserved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_catch_ups_pending",
                schema: "ai",
                table: "catch_ups");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_usage_outcome",
                schema: "ai",
                table: "ai_usage");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_usage_outcome",
                schema: "ai",
                table: "ai_usage",
                sql: "outcome IN ('ok','repaired','invalid','degraded','error','timeout','rate_limited','budget_denied','stale')");
        }
    }
}
