using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.AlertRules.Queries.GetAlertRules;

public record GetAlertRulesQuery(
    string? Status = null,
    string? Severity = null) : IRequest<Result<List<AlertRuleDto>>>;
