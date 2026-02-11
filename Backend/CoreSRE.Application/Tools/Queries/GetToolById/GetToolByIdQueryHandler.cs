using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetToolById;

/// <summary>
/// 按 ID 查询工具详情处理器
/// </summary>
public class GetToolByIdQueryHandler : IRequestHandler<GetToolByIdQuery, Result<ToolRegistrationDto>>
{
    private readonly IToolRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetToolByIdQueryHandler(IToolRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<ToolRegistrationDto>> Handle(
        GetToolByIdQuery request,
        CancellationToken cancellationToken)
    {
        var tool = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (tool is null)
            return Result<ToolRegistrationDto>.NotFound($"Tool with ID '{request.Id}' not found.");

        var dto = _mapper.Map<ToolRegistrationDto>(tool);
        return Result<ToolRegistrationDto>.Ok(dto);
    }
}
