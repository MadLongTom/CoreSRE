using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.DTOs;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.ListIncidents;

/// <summary>
/// 查询 Incident 列表（支持状态/严重级/时间范围筛选）。
/// </summary>
public record ListIncidentsQuery(
    string? Status = null,
    string? Severity = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<IEnumerable<IncidentSummaryDto>>>;
