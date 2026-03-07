using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.ArchiveSop;

public class ArchiveSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    IAlertRuleRepository alertRuleRepository,
    ILogger<ArchiveSopCommandHandler> logger)
    : IRequestHandler<ArchiveSopCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ArchiveSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<bool>.NotFound($"Skill '{request.SkillId}' not found.");

        skill.Archive();
        await skillRepository.UpdateAsync(skill, cancellationToken);

        // 如果有关联的 AlertRule，解除绑定
        if (skill.SourceAlertRuleId is not null)
        {
            var alertRule = await alertRuleRepository.GetByIdAsync(skill.SourceAlertRuleId.Value, cancellationToken);
            if (alertRule is not null && alertRule.SopId == skill.Id)
            {
                alertRule.ClearSopBinding();
                await alertRuleRepository.UpdateAsync(alertRule, cancellationToken);
            }
        }

        logger.LogInformation("SOP '{SkillId}' archived.", request.SkillId);
        return Result<bool>.Ok(true);
    }
}
