using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tether.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class M010_GoogleSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.AddColumn<bool>(
                name: "hosted_domain_present",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_states",
                schema: "identity",
                columns: table => new
                {
                    state_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    binder_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    landing = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_states", x => x.state_hash);
                    table.CheckConstraint("ck_oauth_states_hash_lengths", "octet_length(state_hash) = 32 and octet_length(binder_hash) = 32");
                    table.CheckConstraint("ck_oauth_states_purpose", "purpose in ('signin')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_states_expires_at",
                schema: "identity",
                table: "oauth_states",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oauth_states",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hosted_domain_present",
                schema: "identity",
                table: "users");

            // Deliberately NOT unique again: once Google users exist, two of them can share an address (CAL-23) and a unique index
            // would make Down fail. Down keeps a plain index; the uniqueness of M001 is not restored.
            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email");
        }
    }
}
