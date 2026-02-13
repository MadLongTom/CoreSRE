using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Skills.Queries.GetSkills;

public class GetSkillsQueryHandler : IRequestHandler<GetSkillsQuery, Result<PagedResult<SkillRegistrationDto>>>
{
    private readonly ISkillRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetSkillsQueryHandler(ISkillRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResult<SkillRegistrationDto>>> Handle(
        GetSkillsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Scope, request.Status, request.Category, request.Search,
            page, pageSize, cancellationToken);

        var dtos = _mapper.Map<List<SkillRegistrationDto>>(items);

        return Result<PagedResult<SkillRegistrationDto>>.Ok(new PagedResult<SkillRegistrationDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}
