using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    session_data = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    session_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => new { x.agent_id, x.conversation_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_agent_id",
                table: "agent_sessions",
                column: "agent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_sessions");
        }
    }
}
