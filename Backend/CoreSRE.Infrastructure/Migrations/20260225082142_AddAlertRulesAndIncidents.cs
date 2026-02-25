using System;
using System.Collections.Generic;
using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertRulesAndIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    matchers = table.Column<List<AlertMatcherVO>>(type: "jsonb", nullable: false),
                    severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responder_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summarizer_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_channels = table.Column<string>(type: "jsonb", nullable: false),
                    cooldown_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    tags = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    alert_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alert_fingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    alert_payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    alert_labels = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    route = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    root_cause = table.Column<string>(type: "text", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true),
                    generated_sop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    time_to_detect_ms = table.Column<long>(type: "bigint", nullable: true),
                    time_to_resolve_ms = table.Column<long>(type: "bigint", nullable: true),
                    timeline = table.Column<List<IncidentTimelineVO>>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_name",
                table: "alert_rules",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_status",
                table: "alert_rules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_alert_rule_id",
                table: "incidents",
                column: "alert_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_alert_rule_id_alert_fingerprint",
                table: "incidents",
                columns: new[] { "alert_rule_id", "alert_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_severity",
                table: "incidents",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_status",
                table: "incidents",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_rules");

            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
