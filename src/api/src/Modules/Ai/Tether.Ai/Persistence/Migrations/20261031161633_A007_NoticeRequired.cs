using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// NAS-R07 (docs/security/notes-after-stop-rereview.md): the database, not only the one code path that creates a press, now requires the
    /// notice id on every <c>ai.lecture_notes</c> row. Rows from before A006 have no notice recorded; they are backfilled with
    /// <c>none-recorded</c> (<see cref="Tether.Ai.Provider.NotesOptions.UnrecordedNotice"/>), an id the server refuses to configure or accept,
    /// so the backfill can never be mistaken for a notice a student saw. No default is set: an insert without a notice fails.
    /// Down only relaxes the column again (no data is lost; the backfilled ids stay).
    /// </remarks>
    internal partial class A007_NoticeRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.Sql("UPDATE ai.lecture_notes SET notice_version = 'none-recorded' WHERE notice_version IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "notice_version",
                schema: "ai",
                table: "lecture_notes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes",
                sql: "notice_version ~ '^[A-Za-z0-9._-]{1,64}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes");

            migrationBuilder.AlterColumn<string>(
                name: "notice_version",
                schema: "ai",
                table: "lecture_notes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddCheckConstraint(
                name: "ck_lecture_notes_notice",
                schema: "ai",
                table: "lecture_notes",
                sql: "notice_version IS NULL OR notice_version ~ '^[A-Za-z0-9._-]{1,64}$'");
        }
    }
}
