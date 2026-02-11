using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// McpToolItem EF Core 实体配置。
/// FK 到 ToolRegistration（CASCADE DELETE），ToolAnnotationsVO 映射为 JSONB。
/// </summary>
public class McpToolItemConfiguration : IEntityTypeConfiguration<McpToolItem>
{
    public void Configure(EntityTypeBuilder<McpToolItem> builder)
    {
        builder.ToTable("mcp_tool_items");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Scalar properties
        builder.Property(e => e.ToolRegistrationId)
            .HasColumnName("tool_registration_id")
            .IsRequired();

        builder.Property(e => e.ToolName)
            .HasColumnName("tool_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(e => e.InputSchema)
            .HasColumnName("input_schema")
            .HasColumnType("jsonb");

        builder.Property(e => e.OutputSchema)
            .HasColumnName("output_schema")
            .HasColumnType("jsonb");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // JSONB value objects via ToJson()
        builder.OwnsOne(e => e.Annotations, ann =>
        {
            ann.ToJson("annotations");
        });

        // Indexes
        builder.HasIndex(e => new { e.ToolRegistrationId, e.ToolName })
            .IsUnique();

        builder.HasIndex(e => e.ToolRegistrationId);
    }
}
