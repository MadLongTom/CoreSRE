namespace CoreSRE.Application.Sandboxes.DTOs;

/// <summary>
/// 沙箱实例 DTO
/// </summary>
public class SandboxInstanceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SandboxType { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public int MemoryMib { get; set; }
    public string K8sNamespace { get; set; } = string.Empty;
    public int AutoStopMinutes { get; set; }
    public bool PersistWorkspace { get; set; }
    public Guid? AgentId { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public string? PodName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
