using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowById;

/// <summary>
/// 按 ID 查询工作流详情
/// </summary>
public record GetWorkflowByIdQuery(Guid Id) : IRequest<Result<WorkflowDefinitionDto>>;
