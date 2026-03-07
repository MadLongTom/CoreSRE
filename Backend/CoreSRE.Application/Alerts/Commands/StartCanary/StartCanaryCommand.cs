using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Alerts.Commands.StartCanary;

/// <summary>
/// 启动 AlertRule 金丝雀模式（Spec 025 — US3）
/// </summary>
public record StartCanaryCommand(Guid AlertRuleId, Guid CanarySopId) : IRequest<Result<bool>>;
