using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// WorkflowDefinition EF Core 实体配置。
/// 使用 ToJson() 将 WorkflowGraphVO 映射为 PostgreSQL JSONB 列。
/// </summary>
public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");

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

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // JSONB value object: WorkflowGraphVO with nested OwnsMany for nodes and edges
        builder.OwnsOne(e => e.Graph, g =>
        {
            g.ToJson("graph");
            g.OwnsMany(x => x.Nodes, n =>
            {
                n.Property(p => p.NodeType).HasConversion<string>();
            });
            g.OwnsMany(x => x.Edges, e =>
            {
                e.Property(p => p.EdgeType).HasConversion<string>();
            });
        });

        // Indexes
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.Status);
    }
}
