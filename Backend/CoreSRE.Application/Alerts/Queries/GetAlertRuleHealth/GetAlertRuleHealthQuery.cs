using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Alerts.Queries.GetAlertRuleHealth;

/// <summary>
/// 获取 AlertRule 健康评分（Spec 025 — US5）
/// </summary>
public record GetAlertRuleHealthQuery(Guid AlertRuleId) : IRequest<Result<AlertRuleHealthVO>>;
