using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Generalises the usage ledger from "a lecture session" to "a scope" (docs/data/schema-syllabus-p2.md section 11). EXPAND step of
    /// expand/contract: <c>session_id</c> stays (nullable, dual-written for lecture rows) and is dropped by a later A004 once all code
    /// reads <c>scope_id</c>. Adds the <c>pool</c> budget dimension to <c>ai.ai_usage</c> and to <c>ai.global_spend</c> (key
    /// <c>(period, pool)</c>) so syllabus spend has its own daily and monthly caps. The ledger is spend evidence: no row is deleted,
    /// rewritten beyond the backfill, or given a scope FK (deleting a syllabus must not refund its spend).
    /// Single statements on a small table, inside the migration transaction. Down is GUARDED: it refuses once any document-scope row exists.
    /// </remarks>
    internal partial class A003_AiScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE ai.ai_usage DROP CONSTRAINT ck_ai_usage_workflow;

                ALTER TABLE ai.ai_usage
                    ADD COLUMN scope_kind varchar(10) NOT NULL DEFAULT 'lecture',
                    ADD COLUMN pool       varchar(10) NOT NULL DEFAULT 'lecture',
                    ADD COLUMN scope_id   uuid;

                UPDATE ai.ai_usage SET scope_id = session_id;

                ALTER TABLE ai.ai_usage ALTER COLUMN scope_id SET NOT NULL;
                ALTER TABLE ai.ai_usage ALTER COLUMN session_id DROP NOT NULL;

                ALTER TABLE ai.ai_usage
                    ADD CONSTRAINT ck_ai_usage_scope_kind CHECK (scope_kind IN ('lecture','syllabus')),
                    ADD CONSTRAINT ck_ai_usage_pool CHECK (pool IN ('lecture','syllabus') AND pool = scope_kind),
                    ADD CONSTRAINT ck_ai_usage_scope_session CHECK ((scope_kind = 'lecture' AND session_id IS NOT NULL AND session_id = scope_id) OR (scope_kind = 'syllabus' AND session_id IS NULL)),
                    ADD CONSTRAINT ck_ai_usage_workflow CHECK (
                        (scope_kind = 'lecture' AND workflow IN ('RollingSummary','Compaction','LostTrack'))
                        OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract'));

                CREATE INDEX ix_ai_usage_scope_created ON ai.ai_usage (user_id, scope_kind, scope_id, created_at);
                CREATE INDEX ix_ai_usage_user_pool_created ON ai.ai_usage (user_id, pool, created_at);

                ALTER TABLE ai.global_spend ADD COLUMN pool varchar(10) NOT NULL DEFAULT 'lecture';
                ALTER TABLE ai.global_spend DROP CONSTRAINT global_spend_pkey, ADD PRIMARY KEY (period, pool);
                ALTER TABLE ai.global_spend ADD CONSTRAINT ck_global_spend_pool CHECK (pool IN ('lecture','syllabus'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Guard first, in its own statement: nothing below runs if document-scope spend exists. Deleting or relabelling those rows to make
            // Down pass is not allowed (it would erase spend evidence and refund the budget); forward-fix instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ai.ai_usage WHERE scope_kind <> 'lecture' OR pool <> 'lecture' OR session_id IS NULL)
                       OR EXISTS (SELECT 1 FROM ai.global_spend WHERE pool <> 'lecture') THEN
                        RAISE EXCEPTION 'A003 cannot be rolled back: document-scope ledger rows exist (forward-fix instead)';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ai.global_spend DROP CONSTRAINT ck_global_spend_pool;
                ALTER TABLE ai.global_spend DROP CONSTRAINT global_spend_pkey, ADD PRIMARY KEY (period);
                ALTER TABLE ai.global_spend DROP COLUMN pool;

                DROP INDEX ai.ix_ai_usage_user_pool_created;
                DROP INDEX ai.ix_ai_usage_scope_created;
                ALTER TABLE ai.ai_usage
                    DROP CONSTRAINT ck_ai_usage_workflow,
                    DROP CONSTRAINT ck_ai_usage_scope_session,
                    DROP CONSTRAINT ck_ai_usage_pool,
                    DROP CONSTRAINT ck_ai_usage_scope_kind;
                ALTER TABLE ai.ai_usage ALTER COLUMN session_id SET NOT NULL;
                ALTER TABLE ai.ai_usage DROP COLUMN scope_id, DROP COLUMN pool, DROP COLUMN scope_kind;
                ALTER TABLE ai.ai_usage ADD CONSTRAINT ck_ai_usage_workflow CHECK (workflow IN ('RollingSummary','Compaction','LostTrack'));
                """);
        }
    }
}
