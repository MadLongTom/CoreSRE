using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.AlertRules.Commands.DeleteAlertRule;

public record DeleteAlertRuleCommand(Guid Id) : IRequest<Result<bool>>;
