using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Syllabus.Persistence.Migrations
{
    /// <summary>
    /// SBV-17 (docs/security/sbr-fixes-verification.md, sixth pass). syllabus-item.schema.json (allOf P2-08/P2-19) caps ANY item carrying
    /// <c>injection_suspect</c>, <c>supersession_cue</c> or <c>low_text_quality</c> at <c>please_check</c>, but S001's
    /// <c>ck_items_flags_force_check</c> covered resolved items only, so the rule for policy (<c>not_applicable</c>) and <c>needs_date</c> items
    /// rested on server code (SyllabusSignals.Confidence), and policy rows stored before bb9b124 were never corrected (the web refuses the
    /// whole item page for such a row). Up, in one transaction: (1) correct every non-conforming row to please_check with the server's fixed
    /// sentence (SyllabusSignals.ReasonUnclear; the row_version trigger bumps the ETag); (2) replace the check so those three flags force
    /// please_check for every status, keeping the seven-flag rule for resolved items. Hand-written raw SQL like S002 (no model change).
    /// Down restores S001's check exactly; it does not un-correct data (the corrected rows satisfy both checks).
    /// </summary>
    [DbContext(typeof(SyllabusDbContext))]
    [Migration("20261102120000_S003_ItemsFlagsForceCheckAllStatuses")]
    internal partial class S003_ItemsFlagsForceCheckAllStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE syllabus.items
                   SET confidence = 'please_check', confidence_reason = 'One detail here is unclear. Please check.'
                 WHERE confidence <> 'please_check'
                   AND flags && ARRAY['injection_suspect','supersession_cue','low_text_quality']::text[];

                ALTER TABLE syllabus.items DROP CONSTRAINT ck_items_flags_force_check;
                ALTER TABLE syllabus.items ADD CONSTRAINT ck_items_flags_force_check CHECK (
                    confidence = 'please_check'
                    OR (NOT (flags && ARRAY['injection_suspect','supersession_cue','low_text_quality']::text[])
                        AND (when_status <> 'resolved' OR NOT (flags && ARRAY['tentative_language','weekday_mismatch','table_layout_risk','outside_term']::text[]))));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE syllabus.items DROP CONSTRAINT ck_items_flags_force_check;
                ALTER TABLE syllabus.items ADD CONSTRAINT ck_items_flags_force_check CHECK (when_status <> 'resolved' OR confidence = 'please_check' OR NOT (flags && ARRAY['tentative_language','weekday_mismatch','table_layout_risk','outside_term','injection_suspect','supersession_cue','low_text_quality']::text[]));
                """);
        }
    }
}
