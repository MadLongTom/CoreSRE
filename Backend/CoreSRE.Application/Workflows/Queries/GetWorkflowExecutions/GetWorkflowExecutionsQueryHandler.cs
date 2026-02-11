using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowExecutions;

/// <summary>
/// 查询工作流执行记录列表处理器。
/// 支持按 status 可选筛选。验证工作流存在性（404）。
/// </summary>
public class GetWorkflowExecutionsQueryHandler
    : IRequestHandler<GetWorkflowExecutionsQuery, Result<List<WorkflowExecutionSummaryDto>>>
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IMapper _mapper;

    public GetWorkflowExecutionsQueryHandler(
        IWorkflowDefinitionRepository workflowRepo,
        IWorkflowExecutionRepository executionRepo,
        IMapper mapper)
    {
        _workflowRepo = workflowRepo;
        _executionRepo = executionRepo;
        _mapper = mapper;
    }

    public async Task<Result<List<WorkflowExecutionSummaryDto>>> Handle(
        GetWorkflowExecutionsQuery request,
        CancellationToken cancellationToken)
    {
        // 验证工作流存在
        var workflow = await _workflowRepo.GetByIdAsync(request.WorkflowDefinitionId, cancellationToken);
        if (workflow is null)
            return Result<List<WorkflowExecutionSummaryDto>>.NotFound("工作流不存在");

        // 获取执行记录
        var executions = await _executionRepo.GetByWorkflowIdAsync(request.WorkflowDefinitionId, cancellationToken);

        // 按状态筛选
        if (!string.IsNullOrEmpty(request.Status)
            && Enum.TryParse<ExecutionStatus>(request.Status, ignoreCase: true, out var status))
        {
            executions = executions.Where(e => e.Status == status);
        }

        var dtos = _mapper.Map<List<WorkflowExecutionSummaryDto>>(executions);
        return Result<List<WorkflowExecutionSummaryDto>>.Ok(dtos);
    }
}
