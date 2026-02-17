using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// AgentRegistration EF Core 实体配置。
/// 使用 ToJson() 将 AgentCardVO、LlmConfigVO、HealthCheckVO 映射为 PostgreSQL JSONB 列。
/// </summary>
public class AgentRegistrationConfiguration : IEntityTypeConfiguration<AgentRegistration>
{
    public void Configure(EntityTypeBuilder<AgentRegistration> builder)
    {
        builder.ToTable("agent_registrations");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Scalar properties
        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(e => e.AgentType)
            .HasColumnName("agent_type")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Endpoint)
            .HasColumnName("endpoint")
            .HasMaxLength(2048);

        builder.Property(e => e.WorkflowRef)
            .HasColumnName("workflow_ref");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // JSONB value objects via ToJson()
        builder.OwnsOne(e => e.AgentCard, card =>
        {
            card.ToJson("agent_card");
            card.OwnsMany(c => c.Skills);
            card.OwnsMany(c => c.Interfaces);
            card.OwnsMany(c => c.SecuritySchemes);
        });

        builder.OwnsOne(e => e.LlmConfig, llm =>
        {
            llm.ToJson("llm_config");
        });

        builder.OwnsOne(e => e.HealthCheck, hc =>
        {
            hc.ToJson("health_check");
        });

        // TeamConfigVO uses Npgsql native JSONB mapping (not EF Core OwnsOne/ToJson)
        // because it contains Dictionary<Guid, List<HandoffTargetVO>> which OwnsOne doesn't support.
        builder.Property(e => e.TeamConfig)
            .HasColumnType("jsonb")
            .HasColumnName("team_config");

        // Indexes
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.AgentType);
    }
}
