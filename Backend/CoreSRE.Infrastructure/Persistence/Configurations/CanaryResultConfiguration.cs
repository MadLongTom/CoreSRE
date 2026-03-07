using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// CanaryResult EF Core 实体配置 — 映射到 canary_results 表
/// </summary>
public class CanaryResultConfiguration : IEntityTypeConfiguration<CanaryResult>
{
    public void Configure(EntityTypeBuilder<CanaryResult> builder)
    {
        builder.ToTable("canary_results");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AlertRuleId)
            .HasColumnName("alert_rule_id")
            .IsRequired();

        builder.Property(e => e.IncidentId)
            .HasColumnName("incident_id")
            .IsRequired();

        builder.Property(e => e.CanarySopId)
            .HasColumnName("canary_sop_id")
            .IsRequired();

        builder.Property(e => e.ShadowRootCause)
            .HasColumnName("shadow_root_cause")
            .HasColumnType("text");

        builder.Property(e => e.ActualRootCause)
            .HasColumnName("actual_root_cause")
            .HasColumnType("text");

        builder.Property(e => e.IsConsistent)
            .HasColumnName("is_consistent")
            .IsRequired();

        builder.Property(e => e.ShadowToolCalls)
            .HasColumnName("shadow_tool_calls")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ShadowTokenConsumed)
            .HasColumnName("shadow_token_consumed")
            .IsRequired();

        builder.Property(e => e.ShadowDurationMs)
            .HasColumnName("shadow_duration_ms")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // 索引
        builder.HasIndex(e => e.AlertRuleId);
        builder.HasIndex(e => e.CanarySopId);
    }
}
