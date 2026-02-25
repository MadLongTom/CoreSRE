using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Incidents.DTOs;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.GetIncidentById;

public record GetIncidentByIdQuery(Guid Id) : IRequest<Result<IncidentDetailDto>>;
