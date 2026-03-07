using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.Commands.RetryStepExecution;

/// <summary>
/// 重试 SOP 中失败的步骤
/// </summary>
public record RetryStepExecutionCommand(
    Guid IncidentId,
    int StepNumber) : IRequest<Result<bool>>;
