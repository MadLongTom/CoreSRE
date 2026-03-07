using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.CreateAlertRule;

public record CreateAlertRuleCommand : IRequest<Result<AlertRuleDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public List<AlertMatcherDto> Matchers { get; init; } = [];
    public string Severity { get; init; } = "P3";
    public Guid? SopId { get; init; }
    public Guid? ResponderAgentId { get; init; }
    public Guid? TeamAgentId { get; init; }
    public Guid? SummarizerAgentId { get; init; }
    public List<string>? NotificationChannels { get; init; }
    public int CooldownMinutes { get; init; } = 15;
    public Dictionary<string, string>? Tags { get; init; }
    public List<ContextInitItemVO>? ContextProviders { get; init; }
}
