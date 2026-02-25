using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.UpdateAlertRule;

public record UpdateAlertRuleCommand : IRequest<Result<AlertRuleDto>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public List<AlertMatcherDto>? Matchers { get; init; }
    public string? Severity { get; init; }
    public string? Status { get; init; }
    public Guid? SopId { get; init; }
    public Guid? ResponderAgentId { get; init; }
    public Guid? TeamAgentId { get; init; }
    public Guid? SummarizerAgentId { get; init; }
    public List<string>? NotificationChannels { get; init; }
    public int? CooldownMinutes { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
}
