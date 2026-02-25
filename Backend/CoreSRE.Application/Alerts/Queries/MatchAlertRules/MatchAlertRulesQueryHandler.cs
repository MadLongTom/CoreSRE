using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Alerts.Queries.MatchAlertRules;

public class MatchAlertRulesQueryHandler(
    IAlertRuleRepository alertRuleRepository,
    ILogger<MatchAlertRulesQueryHandler> logger)
    : IRequestHandler<MatchAlertRulesQuery, Result<List<AlertRuleMatch>>>
{
    public async Task<Result<List<AlertRuleMatch>>> Handle(
        MatchAlertRulesQuery request,
        CancellationToken cancellationToken)
    {
        var activeRules = (await alertRuleRepository.GetActiveRulesAsync(cancellationToken)).ToList();
        var matches = new List<AlertRuleMatch>();

        foreach (var alert in request.Alerts)
        {
            var matched = false;

            foreach (var rule in activeRules)
            {
                if (rule.IsMatch(alert.Labels))
                {
                    matches.Add(new AlertRuleMatch
                    {
                        Alert = alert,
                        AlertRuleId = rule.Id,
                        SopId = rule.SopId,
                        ResponderAgentId = rule.ResponderAgentId,
                        TeamAgentId = rule.TeamAgentId,
                        SummarizerAgentId = rule.SummarizerAgentId,
                        Severity = rule.Severity,
                        CooldownMinutes = rule.CooldownMinutes,
                        NotificationChannels = rule.NotificationChannels
                    });

                    matched = true;
                    break; // 首条匹配的规则生效（类似 Alertmanager routing tree）
                }
            }

            if (!matched)
            {
                logger.LogInformation(
                    "Alert '{AlertName}' (fingerprint: {Fingerprint}) did not match any active rule.",
                    alert.AlertName, alert.Fingerprint);
            }
        }

        return Result<List<AlertRuleMatch>>.Ok(matches);
    }
}
