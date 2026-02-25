using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.AlertRules.DTOs;

public class AlertRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<AlertMatcherDto> Matchers { get; set; } = [];
    public string Severity { get; set; } = string.Empty;
    public Guid? SopId { get; set; }
    public Guid? ResponderAgentId { get; set; }
    public Guid? TeamAgentId { get; set; }
    public Guid? SummarizerAgentId { get; set; }
    public List<string> NotificationChannels { get; set; } = [];
    public int CooldownMinutes { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AlertMatcherDto
{
    public string Label { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CreateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<AlertMatcherDto> Matchers { get; set; } = [];
    public string Severity { get; set; } = "P3";
    public Guid? SopId { get; set; }
    public Guid? ResponderAgentId { get; set; }
    public Guid? TeamAgentId { get; set; }
    public Guid? SummarizerAgentId { get; set; }
    public List<string>? NotificationChannels { get; set; }
    public int CooldownMinutes { get; set; } = 15;
    public Dictionary<string, string>? Tags { get; set; }
}

public class UpdateAlertRuleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<AlertMatcherDto>? Matchers { get; set; }
    public string? Severity { get; set; }
    public string? Status { get; set; }
    public Guid? SopId { get; set; }
    public Guid? ResponderAgentId { get; set; }
    public Guid? TeamAgentId { get; set; }
    public Guid? SummarizerAgentId { get; set; }
    public List<string>? NotificationChannels { get; set; }
    public int? CooldownMinutes { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}
