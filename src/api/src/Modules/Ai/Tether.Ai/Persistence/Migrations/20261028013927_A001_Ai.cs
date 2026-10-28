using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class A001_Ai : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "ai_usage",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    cached_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    est_cost_micros = table.Column<long>(type: "bigint", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage", x => x.id);
                    table.CheckConstraint("ck_ai_usage_outcome", "outcome IN ('ok','repaired','invalid','degraded','error','timeout','rate_limited','budget_denied','stale')");
                    table.CheckConstraint("ck_ai_usage_tokens", "input_tokens >= 0 AND cached_tokens >= 0 AND output_tokens >= 0 AND est_cost_micros >= 0 AND latency_ms >= 0");
                    table.CheckConstraint("ck_ai_usage_workflow", "workflow IN ('RollingSummary','Compaction','LostTrack')");
                });

            migrationBuilder.CreateTable(
                name: "catch_ups",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_minutes = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ai_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_ms = table.Column<long>(type: "bigint", nullable: false),
                    to_ms = table.Column<long>(type: "bigint", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catch_ups", x => x.id);
                    table.CheckConstraint("ck_catch_ups_ai_state", "ai_state IN ('Pending','Ready','Unavailable','BudgetLimited','NotNeeded')");
                    table.CheckConstraint("ck_catch_ups_kind", "kind IN ('Extractive','Ai')");
                    table.CheckConstraint("ck_catch_ups_window", "window_minutes IN (2,5,10)");
                });

            migrationBuilder.CreateTable(
                name: "summary_revisions",
                schema: "ai",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    based_on_seq = table.Column<long>(type: "bigint", nullable: false),
                    source_from_seq = table.Column<long>(type: "bigint", nullable: false),
                    source_to_seq = table.Column<long>(type: "bigint", nullable: false),
                    covered_from_ms = table.Column<long>(type: "bigint", nullable: false),
                    covered_to_ms = table.Column<long>(type: "bigint", nullable: false),
                    tldr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    topics = table.Column<string>(type: "jsonb", nullable: false),
                    terms = table.Column<string>(type: "jsonb", nullable: false),
                    citations = table.Column<string>(type: "jsonb", nullable: false),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_summary_revisions", x => new { x.session_id, x.revision });
                    table.CheckConstraint("ck_summary_revisions_kind", "kind IN ('Delta','Compaction')");
                    table.CheckConstraint("ck_summary_revisions_range", "source_from_seq <= source_to_seq AND covered_from_ms <= covered_to_ms");
                    table.CheckConstraint("ck_summary_revisions_revision", "revision > 0");
                    table.CheckConstraint("ck_summary_revisions_status", "status IN ('Draft','Final')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_session_created",
                schema: "ai",
                table: "ai_usage",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_user_created",
                schema: "ai",
                table: "ai_usage",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_catch_ups_session_created",
                schema: "ai",
                table: "catch_ups",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_catch_ups_idempotency",
                schema: "ai",
                table: "catch_ups",
                columns: new[] { "user_id", "session_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            // Global monthly spend counter: spans all users, so it has no owner column and is not an EF entity (see GlobalSpend).
            migrationBuilder.Sql("""
                CREATE TABLE ai.global_spend (
                    period text NOT NULL PRIMARY KEY,
                    spent_micros bigint NOT NULL CHECK (spent_micros >= 0),
                    updated_at timestamp with time zone NOT NULL
                );
                """);

            // Cross-schema FKs are raw SQL by design (ADR-014/019/020): derived rows die with their session, and the composite
            // (session_id, user_id) key means a row cannot name another user than its session. The ledger has NO session FK on purpose
            // (deleting a lecture must not hand the student their daily budget back); it dies with the user. Requires Identity and
            // Lectures migrated first.
            migrationBuilder.Sql("""
                ALTER TABLE ai.summary_revisions
                    ADD CONSTRAINT fk_summary_revisions_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE ai.catch_ups
                    ADD CONSTRAINT fk_catch_ups_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE ai.ai_usage
                    ADD CONSTRAINT fk_ai_usage_user_id FOREIGN KEY (user_id)
                    REFERENCES identity.users (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE ai.ai_usage DROP CONSTRAINT fk_ai_usage_user_id;
                ALTER TABLE ai.catch_ups DROP CONSTRAINT fk_catch_ups_session_user;
                ALTER TABLE ai.summary_revisions DROP CONSTRAINT fk_summary_revisions_session_user;
                DROP TABLE ai.global_spend;
                """);

            migrationBuilder.DropTable(
                name: "ai_usage",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "catch_ups",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "summary_revisions",
                schema: "ai");
        }
    }
}
