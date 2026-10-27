using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Courses.Persistence.Migrations
{
    /// <summary>
    /// ADR-014: database-level FK courses.user_id -> identity.users(id) ON DELETE CASCADE.
    /// Raw SQL on purpose: the Courses module has no reference to the Identity module, so EF cannot model it.
    /// This is the single allow-listed cross-schema FK (docs/architecture/backend-foundation.md); a pg_constraint
    /// test fails if any other appears. Requires the Identity migrations to have been applied first, and fails
    /// (by design) if orphaned courses already exist.
    /// </summary>
    /// <inheritdoc />
    internal partial class M004_CoursesUserFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE courses.courses
                    ADD CONSTRAINT fk_courses_user_id FOREIGN KEY (user_id)
                    REFERENCES identity.users (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE courses.courses DROP CONSTRAINT fk_courses_user_id;");
        }
    }
}
