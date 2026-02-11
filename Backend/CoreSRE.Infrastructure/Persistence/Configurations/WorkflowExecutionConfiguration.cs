using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// WorkflowExecution EF Core 实体配置。
/// 使用 ToJson() 将 GraphSnapshot 和 NodeExecutions 映射为 PostgreSQL JSONB 列。
/// </summary>
public class WorkflowExecutionConfiguration : IEntityTypeConfiguration<WorkflowExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.ToTable("workflow_executions");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Scalar properties
        builder.Property(e => e.WorkflowDefinitionId)
            .HasColumnName("workflow_definition_id")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Input)
            .HasColumnName("input")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Output)
            .HasColumnName("output")
            .HasColumnType("jsonb");

        builder.Property(e => e.StartedAt)
            .HasColumnName("started_at");

        builder.Property(e => e.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(e => e.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // JSONB: GraphSnapshot (WorkflowGraphVO)
        builder.OwnsOne(e => e.GraphSnapshot, g =>
        {
            g.ToJson("graph_snapshot");
            g.OwnsMany(x => x.Nodes, n =>
            {
                n.Property(p => p.NodeType).HasConversion<string>();
            });
            g.OwnsMany(x => x.Edges, e =>
            {
                e.Property(p => p.EdgeType).HasConversion<string>();
            });
        });

        // JSONB: NodeExecutions (List<NodeExecutionVO>)
        builder.OwnsMany(e => e.NodeExecutions, n =>
        {
            n.ToJson("node_executions");
            n.Property(p => p.Status).HasConversion<string>();
        });

        // Indexes
        builder.HasIndex(e => e.WorkflowDefinitionId);
        builder.HasIndex(e => e.Status);
    }
}
