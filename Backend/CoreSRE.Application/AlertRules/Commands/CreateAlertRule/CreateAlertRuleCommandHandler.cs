using AutoMapper;
using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.CreateAlertRule;

public class CreateAlertRuleCommandHandler(
    IAlertRuleRepository repository,
    IMapper mapper)
    : IRequestHandler<CreateAlertRuleCommand, Result<AlertRuleDto>>
{
    public async Task<Result<AlertRuleDto>> Handle(
        CreateAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<IncidentSeverity>(request.Severity, true, out var severity))
            return Result<AlertRuleDto>.Fail($"Invalid severity: {request.Severity}");

        var matchers = request.Matchers
            .Select(m => mapper.Map<AlertMatcherVO>(m))
            .ToList();

        var rule = AlertRule.Create(
            name: request.Name,
            matchers: matchers,
            severity: severity,
            description: request.Description,
            sopId: request.SopId,
            responderAgentId: request.ResponderAgentId,
            teamAgentId: request.TeamAgentId,
            summarizerAgentId: request.SummarizerAgentId,
            notificationChannels: request.NotificationChannels,
            cooldownMinutes: request.CooldownMinutes,
            tags: request.Tags);

        await repository.AddAsync(rule, cancellationToken);

        var dto = mapper.Map<AlertRuleDto>(rule);
        return Result<AlertRuleDto>.Ok(dto);
    }
}
