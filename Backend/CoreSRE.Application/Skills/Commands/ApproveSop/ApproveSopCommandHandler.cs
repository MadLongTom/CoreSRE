using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.ApproveSop;

public class ApproveSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    ILogger<ApproveSopCommandHandler> logger)
    : IRequestHandler<ApproveSopCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ApproveSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<bool>.NotFound($"Skill '{request.SkillId}' not found.");

        try
        {
            skill.Approve(request.ReviewedBy, request.Comment);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }

        await skillRepository.UpdateAsync(skill, cancellationToken);

        logger.LogInformation("SOP '{SkillId}' approved by {ReviewedBy}.", request.SkillId, request.ReviewedBy);
        return Result<bool>.Ok(true);
    }
}
