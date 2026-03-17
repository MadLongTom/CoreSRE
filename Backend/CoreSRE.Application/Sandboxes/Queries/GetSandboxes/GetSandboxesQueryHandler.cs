using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Queries.GetSandboxes;

public class GetSandboxesQueryHandler : IRequestHandler<GetSandboxesQuery, Result<PagedResult<SandboxInstanceDto>>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IMapper _mapper;

    public GetSandboxesQueryHandler(ISandboxInstanceRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResult<SandboxInstanceDto>>> Handle(
        GetSandboxesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Status, request.AgentId, request.Search,
            page, pageSize, cancellationToken);

        var dtos = _mapper.Map<List<SandboxInstanceDto>>(items);

        return Result<PagedResult<SandboxInstanceDto>>.Ok(new PagedResult<SandboxInstanceDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}
