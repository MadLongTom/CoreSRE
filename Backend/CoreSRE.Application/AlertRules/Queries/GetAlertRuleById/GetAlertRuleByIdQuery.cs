using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.AlertRules.Queries.GetAlertRuleById;

public record GetAlertRuleByIdQuery(Guid Id) : IRequest<Result<AlertRuleDto>>;
