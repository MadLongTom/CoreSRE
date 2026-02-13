using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.UpdateSkill;

public class UpdateSkillCommandHandler : IRequestHandler<UpdateSkillCommand, Result<SkillRegistrationDto>>
{
    private readonly ISkillRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public UpdateSkillCommandHandler(ISkillRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<SkillRegistrationDto>> Handle(
        UpdateSkillCommand request, CancellationToken cancellationToken)
    {
        var skill = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (skill is null)
            return Result<SkillRegistrationDto>.NotFound();

        // Check name uniqueness (excluding self)
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing is not null && existing.Id != request.Id)
            return Result<SkillRegistrationDto>.Conflict($"Skill with name '{request.Name}' already exists.");

        skill.Update(request.Name, request.Description, request.Category, request.Content);
        skill.SetRequiresTools(request.RequiresTools);

        await _repository.UpdateAsync(skill, cancellationToken);

        return Result<SkillRegistrationDto>.Ok(_mapper.Map<SkillRegistrationDto>(skill));
    }
}
