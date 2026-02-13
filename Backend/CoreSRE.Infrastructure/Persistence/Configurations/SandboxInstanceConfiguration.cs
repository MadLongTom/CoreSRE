using CoreSRE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreSRE.Infrastructure.Persistence.Configurations;

/// <summary>
/// SandboxInstance EF Core 实体配置 — 映射到 sandbox_instances 表
/// </summary>
public class SandboxInstanceConfiguration : IEntityTypeConfiguration<SandboxInstance>
{
    public void Configure(EntityTypeBuilder<SandboxInstance> builder)
    {
        builder.ToTable("sandbox_instances");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.SandboxType)
            .HasColumnName("sandbox_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Image)
            .HasColumnName("image")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.CpuCores)
            .HasColumnName("cpu_cores")
            .IsRequired();

        builder.Property(e => e.MemoryMib)
            .HasColumnName("memory_mib")
            .IsRequired();

        builder.Property(e => e.K8sNamespace)
            .HasColumnName("k8s_namespace")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.AutoStopMinutes)
            .HasColumnName("auto_stop_minutes")
            .IsRequired();

        builder.Property(e => e.PersistWorkspace)
            .HasColumnName("persist_workspace")
            .IsRequired();

        builder.Property(e => e.AgentId)
            .HasColumnName("agent_id");

        builder.Property(e => e.LastActivityAt)
            .HasColumnName("last_activity_at");

        builder.Property(e => e.PodName)
            .HasColumnName("pod_name")
            .HasMaxLength(128);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at");

        // Indexes
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.AgentId);
    }
}
