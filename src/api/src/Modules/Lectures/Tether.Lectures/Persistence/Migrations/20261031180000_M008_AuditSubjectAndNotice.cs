using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Lectures.Persistence.Migrations
{
    /// <summary>
    /// PO-18d review fixes (docs/security/notes-after-stop-review.md). Two nullable, content-free columns on the append-only
    /// <c>lectures.audit_events</c> (adding columns does not fire its row triggers; existing rows keep NULL):
    /// NAS-08 <c>subject_id</c>: the id the action names (for <c>notes.requested</c>, the press; the same id is on the press's AI ledger rows,
    /// so every vendor call can be linked back to the audited press). NAS-05 <c>notice_version</c>: the id of the DRAFT notice the student saw
    /// (never its text), same shape as the syllabus noticeVersion. Down is GUARDED (NAS-R06): it refuses while any row holds a value.
    /// </summary>
    [DbContext(typeof(LecturesDbContext))]
    [Migration("20261031180000_M008_AuditSubjectAndNotice")]
    internal partial class M008_AuditSubjectAndNotice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE lectures.audit_events
                    ADD COLUMN subject_id uuid NULL,
                    ADD COLUMN notice_version varchar(64) NULL,
                    ADD CONSTRAINT ck_audit_events_notice_version CHECK (notice_version IS NULL OR notice_version ~ '^[A-Za-z0-9._-]{1,64}$');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NAS-R06: GUARDED like A005. Dropping the columns would delete evidence from the append-only audit table; forward-fix instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM lectures.audit_events WHERE subject_id IS NOT NULL OR notice_version IS NOT NULL) THEN
                        RAISE EXCEPTION 'M008 cannot be rolled back: audit rows carry a subject or notice id (forward-fix instead)';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE lectures.audit_events
                    DROP CONSTRAINT ck_audit_events_notice_version,
                    DROP COLUMN notice_version,
                    DROP COLUMN subject_id;
                """);
        }
    }
}
