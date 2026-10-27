using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CollegeCompanion.Courses.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class M002_Courses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "courses");

            migrationBuilder.CreateTable(
                name: "courses",
                schema: "courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    term = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    instructor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    // NOTE: no xmin column. xmin is a PostgreSQL system column used as the concurrency token (Course.Version).
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.id);
                    table.CheckConstraint("ck_courses_name_not_blank", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_courses_user_id_id",
                schema: "courses",
                table: "courses",
                columns: new[] { "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_courses_id_user_id",
                schema: "courses",
                table: "courses",
                columns: new[] { "id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "courses",
                schema: "courses");
        }
    }
}
