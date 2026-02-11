using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetTools;

/// <summary>
/// 查询工具列表处理器
/// </summary>
public class GetToolsQueryHandler : IRequestHandler<GetToolsQuery, Result<PagedResult<ToolRegistrationDto>>>
{
    private readonly IToolRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetToolsQueryHandler(IToolRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResult<ToolRegistrationDto>>> Handle(
        GetToolsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.ToolType,
            request.Status,
            request.Search,
            page,
            pageSize,
            cancellationToken);

        var dtos = _mapper.Map<List<ToolRegistrationDto>>(items);

        var pagedResult = new PagedResult<ToolRegistrationDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Result<PagedResult<ToolRegistrationDto>>.Ok(pagedResult);
    }
}
