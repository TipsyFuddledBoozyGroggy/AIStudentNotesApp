using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Lectures.Persistence.Migrations
{
    /// <summary>
    /// NAS-R07 (docs/security/notes-after-stop-rereview.md): a <c>notes.requested</c> audit row must name its press (<c>subject_id</c>) and the
    /// notice the student saw (<c>notice_version</c>), enforced by the database instead of by the one code path that writes it. The table is
    /// append-only (rows from before M008 cannot be backfilled), so the CHECK is added <c>NOT VALID</c>: PostgreSQL enforces it on every new
    /// or changed row and does not re-check existing ones. Hand-written like M007/M008 (no model change: the rule is raw SQL by design).
    /// Down drops the CHECK only; no data is touched.
    /// </summary>
    [DbContext(typeof(LecturesDbContext))]
    [Migration("20261031190000_M009_NotesRequestedEvidence")]
    internal partial class M009_NotesRequestedEvidence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE lectures.audit_events
                    ADD CONSTRAINT ck_audit_events_notes_requested
                    CHECK (action <> 'notes.requested' OR (subject_id IS NOT NULL AND notice_version IS NOT NULL)) NOT VALID;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE lectures.audit_events DROP CONSTRAINT ck_audit_events_notes_requested;");
        }
    }
}
