using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Skills.Queries.GetSkillById;

public class GetSkillByIdQueryHandler : IRequestHandler<GetSkillByIdQuery, Result<SkillRegistrationDto>>
{
    private readonly ISkillRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetSkillByIdQueryHandler(ISkillRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<SkillRegistrationDto>> Handle(
        GetSkillByIdQuery request, CancellationToken cancellationToken)
    {
        var skill = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (skill is null)
            return Result<SkillRegistrationDto>.NotFound();

        return Result<SkillRegistrationDto>.Ok(_mapper.Map<SkillRegistrationDto>(skill));
    }
}
