using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.DeleteSkill;

public class DeleteSkillCommandHandler : IRequestHandler<DeleteSkillCommand, Result<bool>>
{
    private readonly ISkillRegistrationRepository _repository;
    private readonly IFileStorageService _fileStorage;

    public DeleteSkillCommandHandler(
        ISkillRegistrationRepository repository,
        IFileStorageService fileStorage)
    {
        _repository = repository;
        _fileStorage = fileStorage;
    }

    public async Task<Result<bool>> Handle(
        DeleteSkillCommand request, CancellationToken cancellationToken)
    {
        var skill = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (skill is null)
            return Result<bool>.NotFound();

        // Clean up S3 file package
        if (skill.HasFiles)
        {
            await _fileStorage.DeletePrefixAsync("coresre-skills", $"{skill.Id}/", cancellationToken);
        }

        await _repository.DeleteAsync(skill.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
