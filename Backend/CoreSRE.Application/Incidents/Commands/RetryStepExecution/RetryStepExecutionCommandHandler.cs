using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Incidents.Commands.RetryStepExecution;

public class RetryStepExecutionCommandHandler(
    IIncidentRepository incidentRepository,
    ILogger<RetryStepExecutionCommandHandler> logger)
    : IRequestHandler<RetryStepExecutionCommand, Result<bool>>
{
    private const int MaxRetries = 3;

    public async Task<Result<bool>> Handle(
        RetryStepExecutionCommand request, CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<bool>.NotFound($"Incident '{request.IncidentId}' not found.");

        var stepExecution = incident.StepExecutions
            .FirstOrDefault(s => s.StepNumber == request.StepNumber);

        if (stepExecution is null)
            return Result<bool>.NotFound($"Step {request.StepNumber} not found in Incident.");

        if (stepExecution.Status != StepExecutionStatus.Failed)
            return Result<bool>.Fail($"Step {request.StepNumber} is not in Failed status. Current: {stepExecution.Status}.");

        if (stepExecution.RetryCount >= MaxRetries)
            return Result<bool>.Fail($"Step {request.StepNumber} has already been retried {MaxRetries} times. Consider manual intervention.");

        // 重置步骤状态为 Pending 并增加重试计数
        incident.UpdateStepExecution(request.StepNumber, stepExecution.IncrementRetry());
        await incidentRepository.UpdateAsync(incident, cancellationToken);

        logger.LogInformation(
            "Step {StepNumber} of Incident '{IncidentId}' queued for retry (attempt {RetryCount}).",
            request.StepNumber, request.IncidentId, stepExecution.RetryCount + 1);

        // 注意：实际重新执行由 IncidentDispatcherService 的分步执行引擎驱动。
        // 这里只重置状态，让执行引擎检测到 Pending 状态后重新执行该步骤。
        return Result<bool>.Ok(true);
    }
}
