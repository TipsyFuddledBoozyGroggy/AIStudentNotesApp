using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Lectures.Persistence.Migrations
{
    /// <summary>
    /// B2-09: remember per session whether its transcript source was the demo/replay provider, so a later switch to a real
    /// provider cannot make an old replay session look like a real lecture. Every existing row was created while the replay
    /// provider was the only one that existed, so they backfill to true. The column keeps DEFAULT true (the safe direction: an
    /// insert that does not say is shown as demo, never as a real lecture); the application always states the value. Down is dev-only.
    /// </summary>
    [DbContext(typeof(LecturesDbContext))]
    [Migration("20261028120000_M007_SessionDemoSource")]
    internal partial class M007_SessionDemoSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE lectures.lecture_sessions ADD COLUMN demo_source boolean NOT NULL DEFAULT true;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE lectures.lecture_sessions DROP COLUMN demo_source;");
        }
    }
}
