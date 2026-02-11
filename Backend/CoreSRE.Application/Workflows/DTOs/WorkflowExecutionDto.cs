using System.Text.Json;

namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流执行详情 DTO
/// </summary>
public record WorkflowExecutionDto
{
    public Guid Id { get; init; }
    public Guid WorkflowDefinitionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public JsonElement Input { get; init; }
    public JsonElement? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? TraceId { get; init; }
    public List<NodeExecutionDto> NodeExecutions { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}
