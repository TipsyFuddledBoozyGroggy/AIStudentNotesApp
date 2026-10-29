using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Syllabus.Persistence.Migrations
{
    /// <summary>
    /// S002 (raw SQL, like Lectures M006): the four cross-schema FKs (all ON DELETE CASCADE; docs/data/schema-syllabus-p2.md section 9),
    /// the row_version bump trigger on syllabi and items (ETag basis, D-02), the immutability triggers on canonical_texts and
    /// item_proposals (invariant 5), the deferred N-14 trigger (an AI item may have a null snippet only after the text was purged),
    /// the append-only guard on decision_audit and the single purge path syllabus.purge_evidence(user).
    /// Requires Identity and Courses migrated first. Down is dev-only.
    /// </summary>
    [DbContext(typeof(SyllabusDbContext))]
    [Migration("20261028194922_S002_SyllabusIntegrity")]
    internal partial class S002_SyllabusIntegrity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE syllabus.syllabi
                    ADD CONSTRAINT fk_syllabi_course_user FOREIGN KEY (course_id, user_id) REFERENCES courses.courses (id, user_id) ON DELETE CASCADE;
                ALTER TABLE syllabus.calendar_events
                    ADD CONSTRAINT fk_calendar_events_course_user FOREIGN KEY (course_id, user_id) REFERENCES courses.courses (id, user_id) ON DELETE CASCADE;
                ALTER TABLE syllabus.idempotency_keys
                    ADD CONSTRAINT fk_idempotency_keys_course_user FOREIGN KEY (course_id, user_id) REFERENCES courses.courses (id, user_id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_idempotency_keys_user FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;

                CREATE FUNCTION syllabus.bump_row_version() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN NEW.row_version := OLD.row_version + 1; RETURN NEW; END $fn$;
                CREATE TRIGGER trg_syllabi_row_version BEFORE UPDATE ON syllabus.syllabi
                    FOR EACH ROW EXECUTE FUNCTION syllabus.bump_row_version();
                CREATE TRIGGER trg_items_row_version BEFORE UPDATE ON syllabus.items
                    FOR EACH ROW EXECUTE FUNCTION syllabus.bump_row_version();

                CREATE FUNCTION syllabus.forbid_source_update() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'syllabus.% is immutable', TG_TABLE_NAME USING ERRCODE = '55000'; END $fn$;
                CREATE TRIGGER trg_canonical_texts_immutable BEFORE UPDATE ON syllabus.canonical_texts
                    FOR EACH ROW EXECUTE FUNCTION syllabus.forbid_source_update();
                CREATE TRIGGER trg_item_proposals_immutable BEFORE UPDATE ON syllabus.item_proposals
                    FOR EACH ROW EXECUTE FUNCTION syllabus.forbid_source_update();

                -- N-14 / R3-01: a null snippet text is legal only once the canonical text has been purged. Deferred, so the purge job's
                -- multi-statement transaction (items first, then the syllabus stamp) passes at commit.
                CREATE FUNCTION syllabus.check_snippet_present() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE purged timestamptz; present boolean;
                BEGIN
                    IF TG_TABLE_NAME = 'items' THEN
                        IF NOT (NEW.origin = 'ai' AND NEW.snippet_text IS NULL) THEN RETURN NULL; END IF;
                    ELSE
                        IF NEW.snippet_text IS NOT NULL THEN RETURN NULL; END IF;
                    END IF;
                    SELECT canonical_text_purged_at, true INTO purged, present FROM syllabus.syllabi WHERE id = NEW.syllabus_id AND user_id = NEW.user_id;
                    IF present AND purged IS NULL THEN
                        RAISE EXCEPTION 'snippet text may be null only after the canonical text was purged' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END $fn$;
                CREATE CONSTRAINT TRIGGER trg_items_snippet_present AFTER INSERT OR UPDATE OF snippet_text, context_text ON syllabus.items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION syllabus.check_snippet_present();
                CREATE CONSTRAINT TRIGGER trg_unclaimed_dates_snippet_present AFTER INSERT OR UPDATE OF snippet_text, context_text ON syllabus.unclaimed_dates
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION syllabus.check_snippet_present();

                CREATE FUNCTION syllabus.forbid_evidence_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('syllabus.evidence_purge', true) = 'on' THEN RETURN OLD; END IF;
                    RAISE EXCEPTION 'syllabus.% is append-only', TG_TABLE_NAME USING ERRCODE = '55000';
                END $fn$;
                CREATE TRIGGER trg_decision_audit_append_only BEFORE UPDATE OR DELETE ON syllabus.decision_audit
                    FOR EACH ROW EXECUTE FUNCTION syllabus.forbid_evidence_change();
                CREATE TRIGGER trg_decision_audit_no_truncate BEFORE TRUNCATE ON syllabus.decision_audit
                    FOR EACH STATEMENT EXECUTE FUNCTION syllabus.forbid_evidence_change();

                -- The single purge path (account deletion owns the call, docs/data open question 6). Also removes the user's counters.
                CREATE FUNCTION syllabus.purge_evidence(p_user_id uuid) RETURNS void LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM set_config('syllabus.evidence_purge', 'on', true);
                    DELETE FROM syllabus.decision_audit WHERE user_id = p_user_id;
                    DELETE FROM syllabus.creation_counters WHERE user_id = p_user_id;
                    PERFORM set_config('syllabus.evidence_purge', 'off', true);
                END $fn$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS syllabus.purge_evidence(uuid);
                DROP TRIGGER IF EXISTS trg_decision_audit_no_truncate ON syllabus.decision_audit;
                DROP TRIGGER IF EXISTS trg_decision_audit_append_only ON syllabus.decision_audit;
                DROP TRIGGER IF EXISTS trg_unclaimed_dates_snippet_present ON syllabus.unclaimed_dates;
                DROP TRIGGER IF EXISTS trg_items_snippet_present ON syllabus.items;
                DROP TRIGGER IF EXISTS trg_item_proposals_immutable ON syllabus.item_proposals;
                DROP TRIGGER IF EXISTS trg_canonical_texts_immutable ON syllabus.canonical_texts;
                DROP TRIGGER IF EXISTS trg_items_row_version ON syllabus.items;
                DROP TRIGGER IF EXISTS trg_syllabi_row_version ON syllabus.syllabi;
                DROP FUNCTION IF EXISTS syllabus.forbid_evidence_change();
                DROP FUNCTION IF EXISTS syllabus.check_snippet_present();
                DROP FUNCTION IF EXISTS syllabus.forbid_source_update();
                DROP FUNCTION IF EXISTS syllabus.bump_row_version();
                ALTER TABLE syllabus.idempotency_keys DROP CONSTRAINT IF EXISTS fk_idempotency_keys_user, DROP CONSTRAINT IF EXISTS fk_idempotency_keys_course_user;
                ALTER TABLE syllabus.calendar_events DROP CONSTRAINT IF EXISTS fk_calendar_events_course_user;
                ALTER TABLE syllabus.syllabi DROP CONSTRAINT IF EXISTS fk_syllabi_course_user;
                """);
        }
    }
}
