using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.UpdateIncidentStatus;

public class UpdateIncidentStatusCommandHandler(IIncidentRepository repository)
    : IRequestHandler<UpdateIncidentStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateIncidentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident with ID '{request.IncidentId}' not found.");

        if (!Enum.TryParse<IncidentStatus>(request.NewStatus, true, out var newStatus))
            return Result<bool>.Fail($"Invalid status: {request.NewStatus}");

        try
        {
            incident.TransitionTo(newStatus);

            if (request.Note is not null)
            {
                incident.AddTimelineEvent(
                    TimelineEventType.ManualNote, request.Note);
            }

            await repository.UpdateAsync(incident, cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }
    }
}
