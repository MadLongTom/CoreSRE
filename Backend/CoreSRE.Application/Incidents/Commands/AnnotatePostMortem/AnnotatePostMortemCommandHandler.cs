using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Incidents.Commands.AnnotatePostMortem;

public class AnnotatePostMortemCommandHandler(
    IIncidentRepository incidentRepository,
    ILogger<AnnotatePostMortemCommandHandler> logger)
    : IRequestHandler<AnnotatePostMortemCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        AnnotatePostMortemCommand request, CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident '{request.IncidentId}' not found.");

        try
        {
            var annotation = PostMortemAnnotationVO.Create(
                request.ActualRootCause,
                request.RcaAccuracy,
                request.AnnotatedBy,
                request.SopEffectiveness,
                request.ImprovementNotes);

            incident.SetPostMortem(annotation);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }

        await incidentRepository.UpdateAsync(incident, cancellationToken);

        logger.LogInformation(
            "Post-mortem annotated for Incident '{IncidentId}': RCA={RcaAccuracy}",
            request.IncidentId, request.RcaAccuracy);

        return Result<bool>.Ok(true);
    }
}
