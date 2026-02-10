using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// LlmProvider EF Core 实体配置。
/// </summary>
public class LlmProviderConfiguration : IEntityTypeConfiguration<LlmProvider>
{
    public void Configure(EntityTypeBuilder<LlmProvider> builder)
    {
        builder.ToTable("llm_providers");

        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // Scalar properties
        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.BaseUrl)
            .HasColumnName("base_url")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ApiKey)
            .HasColumnName("api_key")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.DiscoveredModels)
            .HasColumnName("discovered_models")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ModelsRefreshedAt)
            .HasColumnName("models_refreshed_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // Indexes
        builder.HasIndex(e => e.Name)
            .IsUnique();
    }
}
