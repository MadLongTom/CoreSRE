using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.PublishSop;

public class PublishSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    IAlertRuleRepository alertRuleRepository,
    ILogger<PublishSopCommandHandler> logger)
    : IRequestHandler<PublishSopCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        PublishSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<bool>.NotFound($"Skill '{request.SkillId}' not found.");

        try
        {
            skill.Publish();
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }

        await skillRepository.UpdateAsync(skill, cancellationToken);

        // 如果指定了 AlertRuleId，同步更新 AlertRule 的 SopId 绑定
        if (request.AlertRuleId is not null || skill.SourceAlertRuleId is not null)
        {
            var alertRuleId = request.AlertRuleId ?? skill.SourceAlertRuleId!.Value;
            var alertRule = await alertRuleRepository.GetByIdAsync(alertRuleId, cancellationToken);
            if (alertRule is not null)
            {
                // 将旧版 SOP 标记为 Superseded
                if (alertRule.SopId is not null && alertRule.SopId != skill.Id)
                {
                    var oldSop = await skillRepository.GetByIdAsync(alertRule.SopId.Value, cancellationToken);
                    if (oldSop is not null && oldSop.Status == SkillStatus.Active)
                    {
                        oldSop.MarkSuperseded();
                        await skillRepository.UpdateAsync(oldSop, cancellationToken);
                    }
                }

                alertRule.BindSop(skill.Id, alertRule.ResponderAgentId ?? Guid.Empty);
                await alertRuleRepository.UpdateAsync(alertRule, cancellationToken);

                logger.LogInformation("AlertRule '{AlertRuleId}' SOP binding updated to '{SkillId}'.",
                    alertRuleId, request.SkillId);
            }
        }

        logger.LogInformation("SOP '{SkillId}' published.", request.SkillId);
        return Result<bool>.Ok(true);
    }
}
