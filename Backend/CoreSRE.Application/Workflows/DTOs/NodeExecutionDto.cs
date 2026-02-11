namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 节点执行记录 DTO
/// </summary>
public record NodeExecutionDto
{
    public string NodeId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Input { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
