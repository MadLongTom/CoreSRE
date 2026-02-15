using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSkillsSpecFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "skill_registrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<string>(
                name: "allowed_tools",
                table: "skill_registrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compatibility",
                table: "skill_registrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "license",
                table: "skill_registrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "metadata",
                table: "skill_registrations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"),
                columns: new[] { "allowed_tools", "compatibility", "license", "metadata" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"),
                columns: new[] { "allowed_tools", "compatibility", "license", "metadata" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000003"),
                columns: new[] { "allowed_tools", "compatibility", "license", "metadata" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000004"),
                columns: new[] { "allowed_tools", "compatibility", "license", "metadata" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000005"),
                columns: new[] { "allowed_tools", "compatibility", "license", "metadata" },
                values: new object[] { null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_tools",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "compatibility",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "license",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "skill_registrations");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "skill_registrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
