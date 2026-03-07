using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.FallbackToRca;

/// <summary>
/// SOP 执行失败后降级到 RCA 链路（Spec 025 — US1）
/// </summary>
public record FallbackToRcaCommand(Guid IncidentId, string Reason) : IRequest<Result<bool>>;
