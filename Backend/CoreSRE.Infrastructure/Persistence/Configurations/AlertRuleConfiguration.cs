using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// AlertRule EF Core 实体配置 — 映射到 alert_rules 表
/// </summary>
public class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("alert_rules");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Matchers)
            .HasColumnName("matchers")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(e => e.SopId)
            .HasColumnName("sop_id");

        builder.Property(e => e.ResponderAgentId)
            .HasColumnName("responder_agent_id");

        builder.Property(e => e.TeamAgentId)
            .HasColumnName("team_agent_id");

        builder.Property(e => e.SummarizerAgentId)
            .HasColumnName("summarizer_agent_id");

        builder.Property(e => e.NotificationChannels)
            .HasColumnName("notification_channels")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.CooldownMinutes)
            .HasColumnName("cooldown_minutes")
            .HasDefaultValue(15)
            .IsRequired();

        builder.Property(e => e.Tags)
            .HasColumnName("tags")
            .HasColumnType("jsonb");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // 索引
        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.Status);
    }
}
