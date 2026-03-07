using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.RespondToIntervention;

/// <summary>
/// 回复 Agent 发起的结构化干预请求（工具审批 / 文本输入 / 选择）。
/// RequestId 用于定位具体的待处理请求。
/// </summary>
public record RespondToInterventionCommand(
    Guid IncidentId,
    string RequestId,
    string ResponseType,
    string? Content = null,
    bool? Approved = null,
    string? OperatorName = null) : IRequest<Result<bool>>;
