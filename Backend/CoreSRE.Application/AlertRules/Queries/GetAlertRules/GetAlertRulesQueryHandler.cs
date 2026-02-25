using AutoMapper;
using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.AlertRules.Queries.GetAlertRules;

public class GetAlertRulesQueryHandler(
    IAlertRuleRepository repository,
    IMapper mapper)
    : IRequestHandler<GetAlertRulesQuery, Result<List<AlertRuleDto>>>
{
    public async Task<Result<List<AlertRuleDto>>> Handle(
        GetAlertRulesQuery request,
        CancellationToken cancellationToken)
    {
        IEnumerable<Domain.Entities.AlertRule> rules;

        if (request.Status is not null &&
            Enum.TryParse<AlertRuleStatus>(request.Status, true, out var status))
        {
            rules = await repository.GetByStatusAsync(status, cancellationToken);
        }
        else
        {
            rules = await repository.GetAllAsync(cancellationToken);
        }

        // 客户端过滤 severity（repo 层返回全量）
        if (request.Severity is not null &&
            Enum.TryParse<IncidentSeverity>(request.Severity, true, out var severity))
        {
            rules = rules.Where(r => r.Severity == severity);
        }

        var dtos = mapper.Map<List<AlertRuleDto>>(rules);
        return Result<List<AlertRuleDto>>.Ok(dtos);
    }
}
