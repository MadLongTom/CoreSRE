using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CoreSRE.Infrastructure.Persistence;

/// <summary>
/// 应用数据库上下文
/// </summary>
public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AgentRegistration> AgentRegistrations => Set<AgentRegistration>();
    public DbSet<AgentSessionRecord> AgentSessions => Set<AgentSessionRecord>();
    public DbSet<LlmProvider> LlmProviders => Set<LlmProvider>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ToolRegistration> ToolRegistrations => Set<ToolRegistration>();
    public DbSet<McpToolItem> McpToolItems => Set<McpToolItem>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<SandboxInstance> SandboxInstances => Set<SandboxInstance>();
    public DbSet<SkillRegistration> SkillRegistrations => Set<SkillRegistration>();
    public DbSet<DataSourceRegistration> DataSourceRegistrations => Set<DataSourceRegistration>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<CanaryResult> CanaryResults => Set<CanaryResult>();
    public DbSet<PromptOptimizationSuggestion> PromptOptimizationSuggestions => Set<PromptOptimizationSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 自动设置更新时间
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
