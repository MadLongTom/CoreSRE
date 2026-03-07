using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.ValidateSop;

public class ValidateSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    IToolRegistrationRepository toolRepository,
    ISopValidator sopValidator,
    ILogger<ValidateSopCommandHandler> logger)
    : IRequestHandler<ValidateSopCommand, Result<SopValidationResultVO>>
{
    public async Task<Result<SopValidationResultVO>> Handle(
        ValidateSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<SopValidationResultVO>.NotFound($"Skill '{request.SkillId}' not found.");

        if (skill.Category != "sop")
            return Result<SopValidationResultVO>.Fail("Only SOP-category skills can be validated.");

        // 获取所有已注册工具名称
        var allTools = await toolRepository.GetByTypeAsync(null, cancellationToken);
        var toolNames = allTools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = sopValidator.Validate(skill.Content, toolNames);

        skill.SetValidationResult(result);
        await skillRepository.UpdateAsync(skill, cancellationToken);

        logger.LogInformation("SOP '{SkillId}' validated: IsValid={IsValid}, Errors={ErrorCount}",
            request.SkillId, result.IsValid, result.Errors.Count);

        return Result<SopValidationResultVO>.Ok(result);
    }
}
