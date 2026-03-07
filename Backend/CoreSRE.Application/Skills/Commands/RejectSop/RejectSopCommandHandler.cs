using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.RejectSop;

public class RejectSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    ILogger<RejectSopCommandHandler> logger)
    : IRequestHandler<RejectSopCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RejectSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<bool>.NotFound($"Skill '{request.SkillId}' not found.");

        try
        {
            skill.Reject(request.ReviewedBy, request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }

        await skillRepository.UpdateAsync(skill, cancellationToken);

        logger.LogInformation("SOP '{SkillId}' rejected by {ReviewedBy}.", request.SkillId, request.ReviewedBy);
        return Result<bool>.Ok(true);
    }
}
