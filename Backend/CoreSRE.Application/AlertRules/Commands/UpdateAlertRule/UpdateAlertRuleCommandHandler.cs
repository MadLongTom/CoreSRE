using AutoMapper;
using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.UpdateAlertRule;

public class UpdateAlertRuleCommandHandler(
    IAlertRuleRepository repository,
    IMapper mapper)
    : IRequestHandler<UpdateAlertRuleCommand, Result<AlertRuleDto>>
{
    public async Task<Result<AlertRuleDto>> Handle(
        UpdateAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (rule is null)
            return Result<AlertRuleDto>.NotFound($"AlertRule with ID '{request.Id}' not found.");

        // 解析可选 severity
        IncidentSeverity? severity = null;
        if (request.Severity is not null)
        {
            if (!Enum.TryParse<IncidentSeverity>(request.Severity, true, out var parsed))
                return Result<AlertRuleDto>.Fail($"Invalid severity: {request.Severity}");
            severity = parsed;
        }

        // 解析可选 status
        if (request.Status is not null)
        {
            if (!Enum.TryParse<AlertRuleStatus>(request.Status, true, out var parsedStatus))
                return Result<AlertRuleDto>.Fail($"Invalid status: {request.Status}");
            rule.SetStatus(parsedStatus);
        }

        // 映射可选 matchers
        List<AlertMatcherVO>? matchers = null;
        if (request.Matchers is not null)
        {
            matchers = request.Matchers
                .Select(m => mapper.Map<AlertMatcherVO>(m))
                .ToList();
        }

        rule.Update(
            name: request.Name,
            description: request.Description,
            matchers: matchers,
            severity: severity,
            sopId: request.SopId,
            responderAgentId: request.ResponderAgentId,
            teamAgentId: request.TeamAgentId,
            summarizerAgentId: request.SummarizerAgentId,
            notificationChannels: request.NotificationChannels,
            cooldownMinutes: request.CooldownMinutes,
            tags: request.Tags);

        if (request.ContextProviders is not null)
            rule.SetContextProviders(request.ContextProviders);

        await repository.UpdateAsync(rule, cancellationToken);

        var dto = mapper.Map<AlertRuleDto>(rule);
        return Result<AlertRuleDto>.Ok(dto);
    }
}
