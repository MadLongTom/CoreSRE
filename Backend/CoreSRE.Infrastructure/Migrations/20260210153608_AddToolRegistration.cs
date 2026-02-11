using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    tool_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discovery_error = table.Column<string>(type: "text", nullable: true),
                    import_source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    auth_config = table.Column<string>(type: "jsonb", nullable: false),
                    connection_config = table.Column<string>(type: "jsonb", nullable: false),
                    tool_schema = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_registrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_tool_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    input_schema = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    output_schema = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    annotations = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_tool_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_mcp_tool_items_tool_registrations_tool_registration_id",
                        column: x => x.tool_registration_id,
                        principalTable: "tool_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mcp_tool_items_tool_registration_id",
                table: "mcp_tool_items",
                column: "tool_registration_id");

            migrationBuilder.CreateIndex(
                name: "IX_mcp_tool_items_tool_registration_id_tool_name",
                table: "mcp_tool_items",
                columns: new[] { "tool_registration_id", "tool_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_registrations_name",
                table: "tool_registrations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tool_registrations_tool_type",
                table: "tool_registrations",
                column: "tool_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_tool_items");

            migrationBuilder.DropTable(
                name: "tool_registrations");
        }
    }
}
