using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.UpdateIncidentStatus;

/// <summary>
/// 更新 Incident 状态 + 追加 Timeline 事件。
/// </summary>
public record UpdateIncidentStatusCommand(
    Guid IncidentId,
    string NewStatus,
    string? Note = null) : IRequest<Result<bool>>;
