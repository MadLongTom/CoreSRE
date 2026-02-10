using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversation EF Core 实体配置。
/// 映射到 PostgreSQL conversations 表，FK 关联 agent_registrations。
/// </summary>
public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Scalar properties (snake_case)
        builder.Property(e => e.AgentId)
            .HasColumnName("agent_id")
            .IsRequired();

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // Foreign key to AgentRegistration
        builder.HasOne<AgentRegistration>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.AgentId)
            .HasDatabaseName("IX_conversations_agent_id");

        builder.HasIndex(e => e.UpdatedAt)
            .IsDescending()
            .HasDatabaseName("IX_conversations_updated_at");
    }
}
