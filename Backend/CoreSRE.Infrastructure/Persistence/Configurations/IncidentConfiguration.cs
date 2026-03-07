using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// Incident EF Core 实体配置 — 映射到 incidents 表
/// </summary>
public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.AlertRuleId)
            .HasColumnName("alert_rule_id");

        builder.Property(e => e.AlertFingerprint)
            .HasColumnName("alert_fingerprint")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.AlertPayload)
            .HasColumnName("alert_payload")
            .HasColumnType("jsonb");

        builder.Property(e => e.AlertLabels)
            .HasColumnName("alert_labels")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Route)
            .HasColumnName("route")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ConversationId)
            .HasColumnName("conversation_id");

        builder.Property(e => e.SopId)
            .HasColumnName("sop_id");

        builder.Property(e => e.RootCause)
            .HasColumnName("root_cause")
            .HasColumnType("text");

        builder.Property(e => e.Resolution)
            .HasColumnName("resolution")
            .HasColumnType("text");

        builder.Property(e => e.GeneratedSopId)
            .HasColumnName("generated_sop_id");

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(e => e.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(e => e.TimeToDetectMs)
            .HasColumnName("time_to_detect_ms");

        builder.Property(e => e.TimeToResolveMs)
            .HasColumnName("time_to_resolve_ms");

        builder.Property(e => e.Timeline)
            .HasColumnName("timeline")
            .HasColumnType("jsonb")
            .IsRequired();

        // ── Post-mortem 标注（Spec 023）──
        builder.Property(e => e.PostMortem)
            .HasColumnName("post_mortem")
            .HasColumnType("jsonb");

        // ── SOP 步骤追踪（Spec 024）──
        builder.Property(e => e.SopSteps)
            .HasColumnName("sop_steps")
            .HasColumnType("jsonb");

        builder.Property(e => e.StepExecutions)
            .HasColumnName("step_executions")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.StepOutputs)
            .HasColumnName("step_outputs")
            .HasColumnType("jsonb");

        // ── 降级信息（Spec 025）──
        builder.Property(e => e.FallbackFrom)
            .HasColumnName("fallback_from")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.FallbackReason)
            .HasColumnName("fallback_reason")
            .HasColumnType("text");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // 索引
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Severity);
        builder.HasIndex(e => e.AlertRuleId);
        builder.HasIndex(e => new { e.AlertRuleId, e.AlertFingerprint });
    }
}
