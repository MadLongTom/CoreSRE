using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.AnnotatePostMortem;

/// <summary>
/// 提交 Incident Post-mortem 标注
/// </summary>
public record AnnotatePostMortemCommand(
    Guid IncidentId,
    string ActualRootCause,
    RcaAccuracyRating RcaAccuracy,
    string AnnotatedBy,
    SopEffectivenessRating? SopEffectiveness = null,
    string? ImprovementNotes = null) : IRequest<Result<bool>>;
