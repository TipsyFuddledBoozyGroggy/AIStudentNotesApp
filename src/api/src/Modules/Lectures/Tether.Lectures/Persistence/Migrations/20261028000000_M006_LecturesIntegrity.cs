using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Lectures.Persistence.Migrations
{
    /// <summary>
    /// B1.5 fix pass (docs/security/m2-b1-review.md), raw SQL because EF cannot model cross-schema FKs (ADR-014/019) or triggers.
    /// B1-01/ADR-019: lecture_sessions.user_id -> identity.users and course_id -> courses.courses, ON DELETE CASCADE.
    ///   Requires Identity and Courses migrated first; fails (by design) if orphaned sessions already exist.
    /// B1-03: composite (id, user_id) FKs so children cannot name another user than their session, and a session cannot be reassigned.
    /// B1-02: consent_events and audit_events are append-only (UPDATE/TRUNCATE always rejected; DELETE rejected unless it runs
    ///   inside lectures.purge_evidence(user), the single documented purge path for erasure/retention). Retention policy is
    ///   undecided (B1-09, Product Owner and counsel): rows are kept by default and nothing calls the purge function yet.
    ///   The GUC guard stops accidental or ORM-driven mutation; it does not stop a DB-privileged actor, so production should
    ///   run the app as a non-owner role with INSERT/SELECT on these tables and EXECUTE on the function (devops item).
    /// B1-11: stopped_at check, B1-10: sweep indexes. Down is dev-only.
    /// </summary>
    [DbContext(typeof(LecturesDbContext))]
    [Migration("20261028000000_M006_LecturesIntegrity")]
    internal partial class M006_LecturesIntegrity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE lectures.lecture_sessions
                    ADD CONSTRAINT fk_lecture_sessions_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_lecture_sessions_course_id FOREIGN KEY (course_id) REFERENCES courses.courses (id) ON DELETE CASCADE;

                CREATE UNIQUE INDEX ux_consent_records_id_user_id ON lectures.consent_records (id, user_id);

                ALTER TABLE lectures.lecture_sessions
                    ADD CONSTRAINT fk_lecture_sessions_consent_user FOREIGN KEY (consent_record_id, user_id)
                    REFERENCES lectures.consent_records (id, user_id);
                ALTER TABLE lectures.consent_events
                    ADD CONSTRAINT fk_consent_events_consent_user FOREIGN KEY (consent_record_id, user_id)
                    REFERENCES lectures.consent_records (id, user_id);
                ALTER TABLE lectures.session_events
                    ADD CONSTRAINT fk_session_events_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE lectures.session_counters
                    ADD CONSTRAINT fk_session_counters_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE lectures.audio_tickets
                    ADD CONSTRAINT fk_audio_tickets_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;
                ALTER TABLE lectures.idempotency_keys
                    ADD CONSTRAINT fk_idempotency_keys_session_user FOREIGN KEY (session_id, user_id)
                    REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE;

                ALTER TABLE lectures.lecture_sessions DROP CONSTRAINT ck_lecture_sessions_stopped_at;
                ALTER TABLE lectures.lecture_sessions ADD CONSTRAINT ck_lecture_sessions_stopped_at CHECK
                    ((state NOT IN ('Active','Paused') OR stopped_at IS NULL) AND (state <> 'Stopped' OR stopped_at IS NOT NULL));

                CREATE INDEX ix_audio_tickets_expires_at ON lectures.audio_tickets (expires_at);
                CREATE INDEX ix_idempotency_keys_created_at ON lectures.idempotency_keys (created_at);

                CREATE FUNCTION lectures.forbid_evidence_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('lectures.evidence_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'lectures.% is append-only', TG_TABLE_NAME USING ERRCODE = '55000';
                END
                $fn$;

                CREATE TRIGGER trg_consent_events_append_only BEFORE UPDATE OR DELETE ON lectures.consent_events
                    FOR EACH ROW EXECUTE FUNCTION lectures.forbid_evidence_change();
                CREATE TRIGGER trg_consent_events_no_truncate BEFORE TRUNCATE ON lectures.consent_events
                    FOR EACH STATEMENT EXECUTE FUNCTION lectures.forbid_evidence_change();
                CREATE TRIGGER trg_audit_events_append_only BEFORE UPDATE OR DELETE ON lectures.audit_events
                    FOR EACH ROW EXECUTE FUNCTION lectures.forbid_evidence_change();
                CREATE TRIGGER trg_audit_events_no_truncate BEFORE TRUNCATE ON lectures.audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION lectures.forbid_evidence_change();

                -- The single purge path: deletes one user's content-free evidence. consent_records are referenced by
                -- sessions, so it fails while the user still has sessions (user deletion cascades them first).
                CREATE FUNCTION lectures.purge_evidence(p_user_id uuid) RETURNS void LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM set_config('lectures.evidence_purge', 'on', true);
                    DELETE FROM lectures.audit_events WHERE user_id = p_user_id;
                    DELETE FROM lectures.consent_events WHERE user_id = p_user_id;
                    PERFORM set_config('lectures.evidence_purge', 'off', true);
                    DELETE FROM lectures.consent_records WHERE user_id = p_user_id;
                END
                $fn$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION lectures.purge_evidence(uuid);
                DROP TRIGGER trg_consent_events_append_only ON lectures.consent_events;
                DROP TRIGGER trg_consent_events_no_truncate ON lectures.consent_events;
                DROP TRIGGER trg_audit_events_append_only ON lectures.audit_events;
                DROP TRIGGER trg_audit_events_no_truncate ON lectures.audit_events;
                DROP FUNCTION lectures.forbid_evidence_change();
                DROP INDEX lectures.ix_idempotency_keys_created_at;
                DROP INDEX lectures.ix_audio_tickets_expires_at;
                ALTER TABLE lectures.lecture_sessions DROP CONSTRAINT ck_lecture_sessions_stopped_at;
                ALTER TABLE lectures.lecture_sessions ADD CONSTRAINT ck_lecture_sessions_stopped_at CHECK
                    (state NOT IN ('Active','Paused') OR stopped_at IS NULL);
                ALTER TABLE lectures.idempotency_keys DROP CONSTRAINT fk_idempotency_keys_session_user;
                ALTER TABLE lectures.audio_tickets DROP CONSTRAINT fk_audio_tickets_session_user;
                ALTER TABLE lectures.session_counters DROP CONSTRAINT fk_session_counters_session_user;
                ALTER TABLE lectures.session_events DROP CONSTRAINT fk_session_events_session_user;
                ALTER TABLE lectures.consent_events DROP CONSTRAINT fk_consent_events_consent_user;
                ALTER TABLE lectures.lecture_sessions DROP CONSTRAINT fk_lecture_sessions_consent_user;
                DROP INDEX lectures.ux_consent_records_id_user_id;
                ALTER TABLE lectures.lecture_sessions DROP CONSTRAINT fk_lecture_sessions_course_id;
                ALTER TABLE lectures.lecture_sessions DROP CONSTRAINT fk_lecture_sessions_user_id;
                """);
        }
    }
}
