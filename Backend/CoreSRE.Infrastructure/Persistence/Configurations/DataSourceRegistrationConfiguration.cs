using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// DataSourceRegistration EF Core 实体配置。
/// 使用 ToJson() 将 VO 映射为 PostgreSQL JSONB 列。
/// </summary>
public class DataSourceRegistrationConfiguration : IEntityTypeConfiguration<DataSourceRegistration>
{
    public void Configure(EntityTypeBuilder<DataSourceRegistration> builder)
    {
        builder.ToTable("data_source_registrations");

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

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Product)
            .HasColumnName("product")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

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

        // JSONB value objects — using Property().HasColumnType("jsonb") instead of OwnsOne().ToJson()
        // because EF Core's ToJson() doesn't support Dictionary<string,string> properties
        // (throws NullReferenceException in CreateReadJsonPropertyValueExpression).
        // Npgsql's native JSONB mapping handles Dictionary/List types correctly.
        builder.Property(e => e.ConnectionConfig)
            .HasColumnName("connection_config")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.DefaultQueryConfig)
            .HasColumnName("default_query_config")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.HealthCheck)
            .HasColumnName("health_check")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .IsRequired();

        // Indexes
        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.Product);
        builder.HasIndex(e => e.Status);
    }
}
