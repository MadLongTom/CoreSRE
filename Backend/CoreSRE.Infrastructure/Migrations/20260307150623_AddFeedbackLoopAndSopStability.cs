using System;
using System.Collections.Generic;
using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackLoopAndSopStability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "skill_registrations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<SopExecutionStatsVO>(
                name: "execution_stats",
                table: "skill_registrations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_comment",
                table: "skill_registrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "skill_registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "skill_registrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_alert_rule_id",
                table: "skill_registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_incident_id",
                table: "skill_registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<SopValidationResultVO>(
                name: "validation_result",
                table: "skill_registrations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "skill_registrations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "fallback_from",
                table: "incidents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fallback_reason",
                table: "incidents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<PostMortemAnnotationVO>(
                name: "post_mortem",
                table: "incidents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<SopStepDefinition>>(
                name: "sop_steps",
                table: "incidents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<SopStepExecutionVO>>(
                name: "step_executions",
                table: "incidents",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<Dictionary<int, JsonElement>>(
                name: "step_outputs",
                table: "incidents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "canary_mode",
                table: "alert_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "canary_sop_id",
                table: "alert_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<AlertRuleHealthVO>(
                name: "health_details",
                table: "alert_rules",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "health_score",
                table: "alert_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_consecutive_failures",
                table: "alert_rules",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "canary_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canary_sop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shadow_root_cause = table.Column<string>(type: "text", nullable: true),
                    actual_root_cause = table.Column<string>(type: "text", nullable: true),
                    is_consistent = table.Column<bool>(type: "boolean", nullable: false),
                    shadow_tool_calls = table.Column<string>(type: "jsonb", nullable: false),
                    shadow_token_consumed = table.Column<int>(type: "integer", nullable: false),
                    shadow_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canary_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_optimization_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    suggested_prompt_patch = table.Column<string>(type: "text", nullable: false),
                    based_on_incident_ids = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    previous_instruction_snapshot = table.Column<string>(type: "text", nullable: true),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_optimization_suggestions", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"),
                columns: new[] { "execution_stats", "review_comment", "reviewed_at", "reviewed_by", "source_alert_rule_id", "source_incident_id", "validation_result", "version" },
                values: new object[] { null, null, null, null, null, null, null, 1 });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"),
                columns: new[] { "execution_stats", "review_comment", "reviewed_at", "reviewed_by", "source_alert_rule_id", "source_incident_id", "validation_result", "version" },
                values: new object[] { null, null, null, null, null, null, null, 1 });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000003"),
                columns: new[] { "execution_stats", "review_comment", "reviewed_at", "reviewed_by", "source_alert_rule_id", "source_incident_id", "validation_result", "version" },
                values: new object[] { null, null, null, null, null, null, null, 1 });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000004"),
                columns: new[] { "execution_stats", "review_comment", "reviewed_at", "reviewed_by", "source_alert_rule_id", "source_incident_id", "validation_result", "version" },
                values: new object[] { null, null, null, null, null, null, null, 1 });

            migrationBuilder.UpdateData(
                table: "skill_registrations",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000005"),
                columns: new[] { "execution_stats", "review_comment", "reviewed_at", "reviewed_by", "source_alert_rule_id", "source_incident_id", "validation_result", "version" },
                values: new object[] { null, null, null, null, null, null, null, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_skill_registrations_source_alert_rule_id",
                table: "skill_registrations",
                column: "source_alert_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_canary_results_alert_rule_id",
                table: "canary_results",
                column: "alert_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_canary_results_canary_sop_id",
                table: "canary_results",
                column: "canary_sop_id");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_optimization_suggestions_agent_id",
                table: "prompt_optimization_suggestions",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_optimization_suggestions_status",
                table: "prompt_optimization_suggestions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canary_results");

            migrationBuilder.DropTable(
                name: "prompt_optimization_suggestions");

            migrationBuilder.DropIndex(
                name: "IX_skill_registrations_source_alert_rule_id",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "execution_stats",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "review_comment",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "source_alert_rule_id",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "source_incident_id",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "validation_result",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "version",
                table: "skill_registrations");

            migrationBuilder.DropColumn(
                name: "fallback_from",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "fallback_reason",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "post_mortem",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "sop_steps",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "step_executions",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "step_outputs",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "canary_mode",
                table: "alert_rules");

            migrationBuilder.DropColumn(
                name: "canary_sop_id",
                table: "alert_rules");

            migrationBuilder.DropColumn(
                name: "health_details",
                table: "alert_rules");

            migrationBuilder.DropColumn(
                name: "health_score",
                table: "alert_rules");

            migrationBuilder.DropColumn(
                name: "max_consecutive_failures",
                table: "alert_rules");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "skill_registrations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);
        }
    }
}
