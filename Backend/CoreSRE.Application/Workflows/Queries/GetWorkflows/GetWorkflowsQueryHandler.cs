using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflows;

/// <summary>
/// 查询工作流列表处理器
/// </summary>
public class GetWorkflowsQueryHandler : IRequestHandler<GetWorkflowsQuery, Result<List<WorkflowSummaryDto>>>
{
    private readonly IWorkflowDefinitionRepository _repository;
    private readonly IMapper _mapper;

    public GetWorkflowsQueryHandler(IWorkflowDefinitionRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<List<WorkflowSummaryDto>>> Handle(
        GetWorkflowsQuery request,
        CancellationToken cancellationToken)
    {
        var workflows = request.Status.HasValue
            ? await _repository.GetByStatusAsync(request.Status.Value, cancellationToken)
            : await _repository.GetAllAsync(cancellationToken);

        var dtos = _mapper.Map<List<WorkflowSummaryDto>>(workflows);
        return Result<List<WorkflowSummaryDto>>.Ok(dtos);
    }
}
