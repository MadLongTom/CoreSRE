using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowExecutions;

/// <summary>
/// 查询指定工作流的执行记录列表（可按状态筛选）
/// </summary>
public record GetWorkflowExecutionsQuery(Guid WorkflowDefinitionId, string? Status = null)
    : IRequest<Result<List<WorkflowExecutionSummaryDto>>>;
