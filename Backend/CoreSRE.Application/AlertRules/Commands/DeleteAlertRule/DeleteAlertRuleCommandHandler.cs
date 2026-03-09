using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.DeleteAlertRule;

public class DeleteAlertRuleCommandHandler(
    IAlertRuleRepository repository,
    IIncidentRepository incidentRepository)
    : IRequestHandler<DeleteAlertRuleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (rule is null)
            return Result<bool>.NotFound($"AlertRule with ID '{request.Id}' not found.");

        // 级联删除关联的 Incident
        var incidents = await incidentRepository.GetByAlertRuleIdAsync(request.Id, cancellationToken);
        foreach (var incident in incidents)
            await incidentRepository.DeleteAsync(incident.Id, cancellationToken);

        await repository.DeleteAsync(request.Id, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
