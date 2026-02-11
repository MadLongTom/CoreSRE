using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// ToolRegistration EF Core 实体配置。
/// 使用 ToJson() 将 ConnectionConfigVO、AuthConfigVO、ToolSchemaVO 映射为 PostgreSQL JSONB 列。
/// </summary>
public class ToolRegistrationConfiguration : IEntityTypeConfiguration<ToolRegistration>
{
    public void Configure(EntityTypeBuilder<ToolRegistration> builder)
    {
        builder.ToTable("tool_registrations");

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

        builder.Property(e => e.ToolType)
            .HasColumnName("tool_type")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.DiscoveryError)
            .HasColumnName("discovery_error")
            .HasColumnType("text");

        builder.Property(e => e.ImportSource)
            .HasColumnName("import_source")
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // JSONB value objects via ToJson()
        builder.OwnsOne(e => e.ConnectionConfig, cc =>
        {
            cc.ToJson("connection_config");
            cc.Property(c => c.TransportType).HasConversion<string>();
        });

        builder.OwnsOne(e => e.AuthConfig, ac =>
        {
            ac.ToJson("auth_config");
            ac.Property(a => a.AuthType).HasConversion<string>();
        });

        builder.OwnsOne(e => e.ToolSchema, ts =>
        {
            ts.ToJson("tool_schema");
            ts.OwnsOne(s => s.Annotations);
        });

        // Navigation — McpToolItems
        builder.HasMany(e => e.McpToolItems)
            .WithOne(m => m.ToolRegistration!)
            .HasForeignKey(m => m.ToolRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.ToolType);
    }
}
