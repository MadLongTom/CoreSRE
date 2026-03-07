using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// PromptOptimizationSuggestion EF Core 实体配置 — 映射到 prompt_optimization_suggestions 表
/// </summary>
public class PromptOptimizationSuggestionConfiguration : IEntityTypeConfiguration<PromptOptimizationSuggestion>
{
    public void Configure(EntityTypeBuilder<PromptOptimizationSuggestion> builder)
    {
        builder.ToTable("prompt_optimization_suggestions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AgentId)
            .HasColumnName("agent_id")
            .IsRequired();

        builder.Property(e => e.IssueType)
            .HasColumnName("issue_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.SuggestedPromptPatch)
            .HasColumnName("suggested_prompt_patch")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.BasedOnIncidentIds)
            .HasColumnName("based_on_incident_ids")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.PreviousInstructionSnapshot)
            .HasColumnName("previous_instruction_snapshot")
            .HasColumnType("text");

        builder.Property(e => e.AppliedAt)
            .HasColumnName("applied_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // 索引
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.Status);
    }
}
