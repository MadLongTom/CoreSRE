using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Queries.GetWorkflowById;

/// <summary>
/// 按 ID 查询工作流详情处理器
/// </summary>
public class GetWorkflowByIdQueryHandler : IRequestHandler<GetWorkflowByIdQuery, Result<WorkflowDefinitionDto>>
{
    private readonly IWorkflowDefinitionRepository _repository;
    private readonly IMapper _mapper;

    public GetWorkflowByIdQueryHandler(IWorkflowDefinitionRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<WorkflowDefinitionDto>> Handle(
        GetWorkflowByIdQuery request,
        CancellationToken cancellationToken)
    {
        var workflow = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (workflow is null)
            return Result<WorkflowDefinitionDto>.NotFound();

        var dto = _mapper.Map<WorkflowDefinitionDto>(workflow);
        return Result<WorkflowDefinitionDto>.Ok(dto);
    }
}
