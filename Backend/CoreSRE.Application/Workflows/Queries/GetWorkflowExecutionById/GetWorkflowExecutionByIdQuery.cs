using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowExecutionById;

/// <summary>
/// 按 ID 查询工作流执行记录详情
/// </summary>
public record GetWorkflowExecutionByIdQuery(Guid WorkflowDefinitionId, Guid ExecutionId)
    : IRequest<Result<WorkflowExecutionDto>>;
