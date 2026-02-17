using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.DataSources.Queries.GetDataSources;

public class GetDataSourcesQueryHandler
    : IRequestHandler<GetDataSourcesQuery, Result<PagedResult<DataSourceRegistrationDto>>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetDataSourcesQueryHandler(IDataSourceRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResult<DataSourceRegistrationDto>>> Handle(
        GetDataSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Category,
            request.Status,
            request.Search,
            page,
            pageSize,
            cancellationToken);

        var dtos = _mapper.Map<List<DataSourceRegistrationDto>>(items);

        var pagedResult = new PagedResult<DataSourceRegistrationDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Result<PagedResult<DataSourceRegistrationDto>>.Ok(pagedResult);
    }
}
