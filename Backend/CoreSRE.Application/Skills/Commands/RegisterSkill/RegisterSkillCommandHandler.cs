using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.RegisterSkill;

public class RegisterSkillCommandHandler : IRequestHandler<RegisterSkillCommand, Result<SkillRegistrationDto>>
{
    private readonly ISkillRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public RegisterSkillCommandHandler(ISkillRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<SkillRegistrationDto>> Handle(
        RegisterSkillCommand request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing is not null)
            return Result<SkillRegistrationDto>.Conflict($"Skill with name '{request.Name}' already exists.");

        var scope = Enum.TryParse<SkillScope>(request.Scope, true, out var s) ? s : SkillScope.User;

        var skill = SkillRegistration.Create(
            name: request.Name,
            description: request.Description,
            category: request.Category,
            content: request.Content,
            scope: scope);

        if (request.RequiresTools.Count > 0)
            skill.SetRequiresTools(request.RequiresTools);

        await _repository.AddAsync(skill, cancellationToken);

        return Result<SkillRegistrationDto>.Ok(_mapper.Map<SkillRegistrationDto>(skill));
    }
}
