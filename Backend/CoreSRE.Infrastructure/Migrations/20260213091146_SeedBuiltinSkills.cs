using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedBuiltinSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sandbox_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sandbox_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    image = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    cpu_cores = table.Column<int>(type: "integer", nullable: false),
                    memory_mib = table.Column<int>(type: "integer", nullable: false),
                    k8s_namespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    auto_stop_minutes = table.Column<int>(type: "integer", nullable: false),
                    persist_workspace = table.Column<bool>(type: "boolean", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pod_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skill_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requires_tools = table.Column<string>(type: "jsonb", nullable: false),
                    has_files = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_registrations", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "skill_registrations",
                columns: new[] { "id", "category", "content", "created_at", "description", "has_files", "name", "requires_tools", "scope", "status", "updated_at" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), "SRE", "# Incident Response Skill\r\n\r\n## Purpose\r\nGuide the SRE through a structured incident response process, from initial triage\r\nto resolution and post-mortem documentation.\r\n\r\n## Workflow\r\n\r\n### 1. Triage\r\n- Gather initial facts: service affected, error symptoms, blast radius\r\n- Classify severity: SEV1 (critical), SEV2 (major), SEV3 (minor), SEV4 (cosmetic)\r\n- Identify on-call responders and communication channels\r\n\r\n### 2. Investigation\r\n- Check monitoring dashboards and recent deployments\r\n- Review error logs and metrics for anomalies\r\n- Identify potential root causes\r\n\r\n### 3. Mitigation\r\n- Propose immediate actions: rollback, feature flag toggle, scaling\r\n- Coordinate with relevant teams\r\n- Verify mitigation effectiveness\r\n\r\n### 4. Communication\r\n- Draft status page updates for stakeholders\r\n- Prepare internal Slack/Teams messages with timeline\r\n- Schedule follow-up check-ins\r\n\r\n### 5. Post-Mortem\r\n- Document timeline of events\r\n- Identify root cause and contributing factors\r\n- Propose action items with owners and deadlines\r\n- Generate blameless post-mortem report\r\n\r\n## Output Format\r\nAlways provide structured responses with:\r\n- Current phase indicator\r\n- Action items list\r\n- Severity and status summary", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Guide through incident triage, severity classification, communication templates, and post-mortem facilitation.", false, "incident-response", "[]", "Builtin", "Active", null },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), "Development", "# Code Review Skill\r\n\r\n## Purpose\r\nPerform thorough, systematic code reviews focusing on correctness, security,\r\nperformance, and maintainability.\r\n\r\n## Review Checklist\r\n\r\n### Correctness\r\n- Logic errors, off-by-one, null handling\r\n- Edge cases and boundary conditions\r\n- Error handling completeness\r\n\r\n### Security\r\n- Input validation and sanitization\r\n- SQL injection, XSS, CSRF vulnerabilities\r\n- Secrets/credentials in code\r\n- Authentication/authorization gaps\r\n\r\n### Performance\r\n- N+1 queries, unnecessary allocations\r\n- Missing indexes for database queries\r\n- Caching opportunities\r\n- Async/await usage correctness\r\n\r\n### Maintainability\r\n- Code clarity and naming conventions\r\n- SOLID principles adherence\r\n- Test coverage for new/changed logic\r\n- Documentation for public APIs\r\n\r\n## Output Format\r\nProvide findings categorized by severity:\r\n- 🔴 Critical: Must fix before merge\r\n- 🟡 Warning: Should fix, potential issues\r\n- 🔵 Suggestion: Nice-to-have improvements\r\n- 💡 Note: Informational observations", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Perform systematic code review: check style, security, performance, and suggest improvements.", false, "code-review", "[]", "Builtin", "Active", null },
                    { new Guid("a0000000-0000-0000-0000-000000000003"), "Database", "# Database Operations Skill\r\n\r\n## Purpose\r\nAssist with database administration tasks including query optimization,\r\nschema migration planning, and backup/restore operations.\r\n\r\n## Capabilities\r\n\r\n### Query Optimization\r\n- Analyze EXPLAIN/EXPLAIN ANALYZE output\r\n- Suggest index creation strategies\r\n- Rewrite inefficient queries\r\n- Identify missing statistics and vacuum needs\r\n\r\n### Migration Planning\r\n- Generate safe migration scripts (up + down)\r\n- Plan zero-downtime schema changes\r\n- Identify backward-compatible migration patterns\r\n- Estimate migration duration for large tables\r\n\r\n### Backup & Restore\r\n- Design backup strategies (full, incremental, WAL archiving)\r\n- Test restore procedures\r\n- Point-in-time recovery planning\r\n- Cross-region replication setup\r\n\r\n### Monitoring\r\n- Connection pool analysis\r\n- Slow query log review\r\n- Lock contention diagnosis\r\n- Storage and growth projections\r\n\r\n## Supported Databases\r\nPostgreSQL (primary), MySQL, SQL Server, MongoDB\r\n\r\n## Safety Rules\r\n- Never run destructive operations without explicit confirmation\r\n- Always generate rollback scripts with migrations\r\n- Prefer `IF EXISTS` / `IF NOT EXISTS` guards", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Assist with database operations: query optimization, migration planning, backup/restore procedures.", false, "database-ops", "[]", "Builtin", "Active", null },
                    { new Guid("a0000000-0000-0000-0000-000000000004"), "Infrastructure", "# Kubernetes Operations Skill\r\n\r\n## Purpose\r\nAssist with Kubernetes cluster management, workload troubleshooting,\r\nand infrastructure-as-code generation.\r\n\r\n## Capabilities\r\n\r\n### Troubleshooting\r\n- Diagnose CrashLoopBackOff, ImagePullBackOff, OOMKilled\r\n- Analyze pod events and container logs\r\n- Debug networking issues (DNS, Services, Ingress)\r\n- Investigate resource quota exhaustion\r\n\r\n### Resource Management\r\n- Right-size CPU/memory requests and limits\r\n- Configure HPA (Horizontal Pod Autoscaler)\r\n- Plan node pool sizing and scaling\r\n- Cost optimization recommendations\r\n\r\n### YAML Generation\r\n- Generate Deployment, Service, Ingress manifests\r\n- Create ConfigMap/Secret templates\r\n- Design RBAC policies (Role, ClusterRole, Bindings)\r\n- Write Helm chart templates\r\n\r\n### Operations\r\n- Rolling update strategies and rollback procedures\r\n- Blue-green and canary deployment patterns\r\n- Cluster upgrade planning\r\n- etcd backup and maintenance\r\n\r\n## Output Format\r\n- YAML manifests should be complete and ready to apply\r\n- Include namespace annotations\r\n- Add resource labels following conventions", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Help with Kubernetes cluster operations: troubleshooting pods, scaling, resource management, and YAML generation.", false, "kubernetes-ops", "[]", "Builtin", "Active", null },
                    { new Guid("a0000000-0000-0000-0000-000000000005"), "Observability", "# Monitoring & Alerting Skill\r\n\r\n## Purpose\r\nHelp design effective monitoring strategies, create alerting rules,\r\nand define SLOs for service reliability.\r\n\r\n## Capabilities\r\n\r\n### Dashboard Design\r\n- RED method dashboards (Rate, Errors, Duration)\r\n- USE method dashboards (Utilization, Saturation, Errors)\r\n- Four Golden Signals implementation\r\n- Custom business metric dashboards\r\n\r\n### Alert Rules\r\n- Multi-window multi-burn-rate SLO alerts\r\n- Symptom-based alerting (avoid cause-based)\r\n- Alert routing and escalation policies\r\n- Reduce noise: grouping, inhibition, silencing\r\n\r\n### SLO Definition\r\n- Identify critical user journeys\r\n- Define SLI (Service Level Indicators)\r\n- Set SLO targets with error budgets\r\n- Calculate error budget burn rates\r\n\r\n### Supported Platforms\r\n- Prometheus + Grafana (PromQL)\r\n- Datadog (DQL)\r\n- Azure Monitor (KQL)\r\n- CloudWatch (Metrics Insights)\r\n\r\n## Output Format\r\n- Alert rules in platform-native syntax\r\n- SLO specs with calculation formulas\r\n- Dashboard JSON/YAML for import", new DateTime(2026, 2, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Design and optimize monitoring dashboards, alerting rules, and SLO definitions.", false, "monitoring-alerting", "[]", "Builtin", "Active", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_instances_agent_id",
                table: "sandbox_instances",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_instances_status",
                table: "sandbox_instances",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_skill_registrations_category",
                table: "skill_registrations",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_skill_registrations_name",
                table: "skill_registrations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skill_registrations_scope",
                table: "skill_registrations",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "IX_skill_registrations_status",
                table: "skill_registrations",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_instances");

            migrationBuilder.DropTable(
                name: "skill_registrations");
        }
    }
}
