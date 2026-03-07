using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.StopCanary;

/// <summary>
/// 停止 AlertRule 金丝雀模式（Spec 025 — US3）
/// </summary>
public record StopCanaryCommand(Guid AlertRuleId) : IRequest<Result<bool>>;
