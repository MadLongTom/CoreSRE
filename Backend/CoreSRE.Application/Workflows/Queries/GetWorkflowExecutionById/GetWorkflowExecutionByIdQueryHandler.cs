using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowExecutionById;

/// <summary>
/// 按 ID 查询工作流执行记录详情处理器。
/// 验证工作流和执行记录存在性（404）。
/// </summary>
public class GetWorkflowExecutionByIdQueryHandler
    : IRequestHandler<GetWorkflowExecutionByIdQuery, Result<WorkflowExecutionDto>>
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly IMapper _mapper;

    public GetWorkflowExecutionByIdQueryHandler(
        IWorkflowDefinitionRepository workflowRepo,
        IWorkflowExecutionRepository executionRepo,
        IMapper mapper)
    {
        _workflowRepo = workflowRepo;
        _executionRepo = executionRepo;
        _mapper = mapper;
    }

    public async Task<Result<WorkflowExecutionDto>> Handle(
        GetWorkflowExecutionByIdQuery request,
        CancellationToken cancellationToken)
    {
        // 验证工作流存在
        var workflow = await _workflowRepo.GetByIdAsync(request.WorkflowDefinitionId, cancellationToken);
        if (workflow is null)
            return Result<WorkflowExecutionDto>.NotFound("工作流不存在");

        // 获取执行记录
        var execution = await _executionRepo.GetByIdAsync(request.ExecutionId, cancellationToken);
        if (execution is null || execution.WorkflowDefinitionId != request.WorkflowDefinitionId)
            return Result<WorkflowExecutionDto>.NotFound("执行记录不存在");

        var dto = _mapper.Map<WorkflowExecutionDto>(execution);
        return Result<WorkflowExecutionDto>.Ok(dto);
    }
}
