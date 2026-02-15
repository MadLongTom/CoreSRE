using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAllowedToolsToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL cannot auto-cast varchar → jsonb; use raw SQL with USING clause
            migrationBuilder.Sql(
                """UPDATE skill_registrations SET allowed_tools = '[]' WHERE allowed_tools IS NULL OR allowed_tools = '';""");
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools SET NOT NULL;""");
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools TYPE jsonb USING allowed_tools::jsonb;""");
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools SET DEFAULT '[]';""");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"),
                column: "allowed_tools",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"),
                column: "allowed_tools",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000003"),
                column: "allowed_tools",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000004"),
                column: "allowed_tools",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000005"),
                column: "allowed_tools",
                value: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools DROP DEFAULT;""");
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools TYPE character varying(500) USING allowed_tools::text;""");
            migrationBuilder.Sql(
                """ALTER TABLE skill_registrations ALTER COLUMN allowed_tools DROP NOT NULL;""");

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"),
                column: "allowed_tools",
                value: null);

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"),
                column: "allowed_tools",
                value: null);

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000003"),
                column: "allowed_tools",
                value: null);

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000004"),
                column: "allowed_tools",
                value: null);

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000005"),
                column: "allowed_tools",
                value: null);
        }
    }
}
