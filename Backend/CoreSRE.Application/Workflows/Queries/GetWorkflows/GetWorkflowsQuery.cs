using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflows;

/// <summary>
/// 查询工作流列表（可按状态过滤）
/// </summary>
public record GetWorkflowsQuery(WorkflowStatus? Status = null)
    : IRequest<Result<List<WorkflowSummaryDto>>>;
