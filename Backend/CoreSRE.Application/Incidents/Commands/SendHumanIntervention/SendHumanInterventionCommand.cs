using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.SendHumanIntervention;

/// <summary>
/// 向正在处理中的 Incident Agent 对话注入人工消息。
/// </summary>
public record SendHumanInterventionCommand(
    Guid IncidentId,
    string Message,
    string? OperatorName = null) : IRequest<Result<bool>>;
