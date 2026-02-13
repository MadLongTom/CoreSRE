using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// SkillRegistration EF Core 实体配置 — 映射到 skill_registrations 表
/// </summary>
public class SkillRegistrationConfiguration : IEntityTypeConfiguration<SkillRegistration>
{
    public void Configure(EntityTypeBuilder<SkillRegistration> builder)
    {
        builder.ToTable("skill_registrations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Scope)
            .HasColumnName("scope")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.RequiresTools)
            .HasColumnName("requires_tools")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.HasFiles)
            .HasColumnName("has_files")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // Indexes
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.Scope);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Category);

        // ── Builtin Skill Seed Data ──────────────────────────────────────
        SeedBuiltinSkills(builder);
    }

    /// <summary>
    /// 使用 EF Core HasData 播种内置 Skill。
    /// 使用固定 GUID 作为主键，确保迁移幂等。
    /// </summary>
    private static void SeedBuiltinSkills(EntityTypeBuilder<SkillRegistration> builder)
    {
        var seedDate = new DateTime(2026, 2, 13, 0, 0, 0, DateTimeKind.Utc);

        builder.HasData(
            new
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000001"),
                Name = "incident-response",
                Description = "Guide through incident triage, severity classification, communication templates, and post-mortem facilitation.",
                Category = "SRE",
                Content = """
# Incident Response Skill

## Purpose
Guide the SRE through a structured incident response process, from initial triage
to resolution and post-mortem documentation.

## Workflow

### 1. Triage
- Gather initial facts: service affected, error symptoms, blast radius
- Classify severity: SEV1 (critical), SEV2 (major), SEV3 (minor), SEV4 (cosmetic)
- Identify on-call responders and communication channels

### 2. Investigation
- Check monitoring dashboards and recent deployments
- Review error logs and metrics for anomalies
- Identify potential root causes

### 3. Mitigation
- Propose immediate actions: rollback, feature flag toggle, scaling
- Coordinate with relevant teams
- Verify mitigation effectiveness

### 4. Communication
- Draft status page updates for stakeholders
- Prepare internal Slack/Teams messages with timeline
- Schedule follow-up check-ins

### 5. Post-Mortem
- Document timeline of events
- Identify root cause and contributing factors
- Propose action items with owners and deadlines
- Generate blameless post-mortem report

## Output Format
Always provide structured responses with:
- Current phase indicator
- Action items list
- Severity and status summary
""",
                Scope = SkillScope.Builtin,
                Status = SkillStatus.Active,
                RequiresTools = new List<Guid>(),
                HasFiles = false,
                CreatedAt = seedDate,
                UpdatedAt = (DateTime?)null,
            },
            new
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000002"),
                Name = "code-review",
                Description = "Perform systematic code review: check style, security, performance, and suggest improvements.",
                Category = "Development",
                Content = """
# Code Review Skill

## Purpose
Perform thorough, systematic code reviews focusing on correctness, security,
performance, and maintainability.

## Review Checklist

### Correctness
- Logic errors, off-by-one, null handling
- Edge cases and boundary conditions
- Error handling completeness

### Security
- Input validation and sanitization
- SQL injection, XSS, CSRF vulnerabilities
- Secrets/credentials in code
- Authentication/authorization gaps

### Performance
- N+1 queries, unnecessary allocations
- Missing indexes for database queries
- Caching opportunities
- Async/await usage correctness

### Maintainability
- Code clarity and naming conventions
- SOLID principles adherence
- Test coverage for new/changed logic
- Documentation for public APIs

## Output Format
Provide findings categorized by severity:
- 🔴 Critical: Must fix before merge
- 🟡 Warning: Should fix, potential issues
- 🔵 Suggestion: Nice-to-have improvements
- 💡 Note: Informational observations
""",
                Scope = SkillScope.Builtin,
                Status = SkillStatus.Active,
                RequiresTools = new List<Guid>(),
                HasFiles = false,
                CreatedAt = seedDate,
                UpdatedAt = (DateTime?)null,
            },
            new
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000003"),
                Name = "database-ops",
                Description = "Assist with database operations: query optimization, migration planning, backup/restore procedures.",
                Category = "Database",
                Content = """
# Database Operations Skill

## Purpose
Assist with database administration tasks including query optimization,
schema migration planning, and backup/restore operations.

## Capabilities

### Query Optimization
- Analyze EXPLAIN/EXPLAIN ANALYZE output
- Suggest index creation strategies
- Rewrite inefficient queries
- Identify missing statistics and vacuum needs

### Migration Planning
- Generate safe migration scripts (up + down)
- Plan zero-downtime schema changes
- Identify backward-compatible migration patterns
- Estimate migration duration for large tables

### Backup & Restore
- Design backup strategies (full, incremental, WAL archiving)
- Test restore procedures
- Point-in-time recovery planning
- Cross-region replication setup

### Monitoring
- Connection pool analysis
- Slow query log review
- Lock contention diagnosis
- Storage and growth projections

## Supported Databases
PostgreSQL (primary), MySQL, SQL Server, MongoDB

## Safety Rules
- Never run destructive operations without explicit confirmation
- Always generate rollback scripts with migrations
- Prefer `IF EXISTS` / `IF NOT EXISTS` guards
""",
                Scope = SkillScope.Builtin,
                Status = SkillStatus.Active,
                RequiresTools = new List<Guid>(),
                HasFiles = false,
                CreatedAt = seedDate,
                UpdatedAt = (DateTime?)null,
            },
            new
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000004"),
                Name = "kubernetes-ops",
                Description = "Help with Kubernetes cluster operations: troubleshooting pods, scaling, resource management, and YAML generation.",
                Category = "Infrastructure",
                Content = """
# Kubernetes Operations Skill

## Purpose
Assist with Kubernetes cluster management, workload troubleshooting,
and infrastructure-as-code generation.

## Capabilities

### Troubleshooting
- Diagnose CrashLoopBackOff, ImagePullBackOff, OOMKilled
- Analyze pod events and container logs
- Debug networking issues (DNS, Services, Ingress)
- Investigate resource quota exhaustion

### Resource Management
- Right-size CPU/memory requests and limits
- Configure HPA (Horizontal Pod Autoscaler)
- Plan node pool sizing and scaling
- Cost optimization recommendations

### YAML Generation
- Generate Deployment, Service, Ingress manifests
- Create ConfigMap/Secret templates
- Design RBAC policies (Role, ClusterRole, Bindings)
- Write Helm chart templates

### Operations
- Rolling update strategies and rollback procedures
- Blue-green and canary deployment patterns
- Cluster upgrade planning
- etcd backup and maintenance

## Output Format
- YAML manifests should be complete and ready to apply
- Include namespace annotations
- Add resource labels following conventions
""",
                Scope = SkillScope.Builtin,
                Status = SkillStatus.Active,
                RequiresTools = new List<Guid>(),
                HasFiles = false,
                CreatedAt = seedDate,
                UpdatedAt = (DateTime?)null,
            },
            new
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000005"),
                Name = "monitoring-alerting",
                Description = "Design and optimize monitoring dashboards, alerting rules, and SLO definitions.",
                Category = "Observability",
                Content = """
# Monitoring & Alerting Skill

## Purpose
Help design effective monitoring strategies, create alerting rules,
and define SLOs for service reliability.

## Capabilities

### Dashboard Design
- RED method dashboards (Rate, Errors, Duration)
- USE method dashboards (Utilization, Saturation, Errors)
- Four Golden Signals implementation
- Custom business metric dashboards

### Alert Rules
- Multi-window multi-burn-rate SLO alerts
- Symptom-based alerting (avoid cause-based)
- Alert routing and escalation policies
- Reduce noise: grouping, inhibition, silencing

### SLO Definition
- Identify critical user journeys
- Define SLI (Service Level Indicators)
- Set SLO targets with error budgets
- Calculate error budget burn rates

### Supported Platforms
- Prometheus + Grafana (PromQL)
- Datadog (DQL)
- Azure Monitor (KQL)
- CloudWatch (Metrics Insights)

## Output Format
- Alert rules in platform-native syntax
- SLO specs with calculation formulas
- Dashboard JSON/YAML for import
""",
                Scope = SkillScope.Builtin,
                Status = SkillStatus.Active,
                RequiresTools = new List<Guid>(),
                HasFiles = false,
                CreatedAt = seedDate,
                UpdatedAt = (DateTime?)null,
            }
        );
    }
}
