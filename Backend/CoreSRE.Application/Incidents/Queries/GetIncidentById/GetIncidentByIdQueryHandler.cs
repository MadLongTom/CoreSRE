using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.GetIncidentById;

public class GetIncidentByIdQueryHandler(
    IIncidentRepository incidentRepository,
    IMapper mapper)
    : IRequestHandler<GetIncidentByIdQuery, Result<IncidentDetailDto>>
{
    public async Task<Result<IncidentDetailDto>> Handle(
        GetIncidentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (incident is null)
            return Result<IncidentDetailDto>.NotFound($"Incident '{request.Id}' not found.");

        var dto = mapper.Map<IncidentDetailDto>(incident);
        return Result<IncidentDetailDto>.Ok(dto);
    }
}
