using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// AgentSessionRecord EF Core 实体配置。
/// 映射到 PostgreSQL agent_sessions 表，复合主键 (agent_id, conversation_id)，
/// session_data 列使用 JSONB 类型。
/// </summary>
public class AgentSessionRecordConfiguration : IEntityTypeConfiguration<AgentSessionRecord>
{
    public void Configure(EntityTypeBuilder<AgentSessionRecord> builder)
    {
        builder.ToTable("agent_sessions");

        // Composite primary key
        builder.HasKey(e => new { e.AgentId, e.ConversationId });

        // Column mappings (snake_case)
        builder.Property(e => e.AgentId)
            .HasColumnName("agent_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.ConversationId)
            .HasColumnName("conversation_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.SessionData)
            .HasColumnName("session_data")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.SessionType)
            .HasColumnName("session_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Index on agent_id for querying all sessions by agent
        builder.HasIndex(e => e.AgentId)
            .HasDatabaseName("IX_agent_sessions_agent_id");
    }
}
