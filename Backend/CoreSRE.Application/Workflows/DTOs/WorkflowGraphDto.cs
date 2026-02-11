namespace CoreSRE.Application.Workflows.DTOs;

/// <summary>
/// 工作流 DAG 图 DTO
/// </summary>
public record WorkflowGraphDto
{
    public List<WorkflowNodeDto> Nodes { get; init; } = [];
    public List<WorkflowEdgeDto> Edges { get; init; } = [];
}
