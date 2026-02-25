using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.DeleteAlertRule;

public class DeleteAlertRuleCommandHandler(IAlertRuleRepository repository)
    : IRequestHandler<DeleteAlertRuleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteAlertRuleCommand request,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (rule is null)
            return Result<bool>.NotFound($"AlertRule with ID '{request.Id}' not found.");

        // 存在关联 Incident 时不允许删除
        var hasIncidents = await repository.HasIncidentsAsync(request.Id, cancellationToken);
        if (hasIncidents)
            return Result<bool>.Conflict(
                $"Cannot delete AlertRule '{request.Id}' because it has associated incidents.");

        await repository.DeleteAsync(request.Id, cancellationToken);
        return Result<bool>.Ok(true);
    }
}
