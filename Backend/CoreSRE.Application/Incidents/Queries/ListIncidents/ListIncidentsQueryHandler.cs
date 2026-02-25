using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.ListIncidents;

public class ListIncidentsQueryHandler(
    IIncidentRepository incidentRepository,
    IMapper mapper)
    : IRequestHandler<ListIncidentsQuery, Result<IEnumerable<IncidentSummaryDto>>>
{
    public async Task<Result<IEnumerable<IncidentSummaryDto>>> Handle(
        ListIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        IncidentStatus? status = null;
        if (!string.IsNullOrEmpty(request.Status) &&
            Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var s))
            status = s;

        IncidentSeverity? severity = null;
        if (!string.IsNullOrEmpty(request.Severity) &&
            Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var sev))
            severity = sev;

        var incidents = await incidentRepository.GetFilteredAsync(
            status, severity, request.From, request.To, cancellationToken);

        var dtos = mapper.Map<IEnumerable<IncidentSummaryDto>>(incidents);
        return Result<IEnumerable<IncidentSummaryDto>>.Ok(dtos);
    }
}
