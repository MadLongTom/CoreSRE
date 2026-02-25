using AutoMapper;
using CoreSRE.Application.AlertRules.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.AlertRules.Queries.GetAlertRuleById;

public class GetAlertRuleByIdQueryHandler(
    IAlertRuleRepository repository,
    IMapper mapper)
    : IRequestHandler<GetAlertRuleByIdQuery, Result<AlertRuleDto>>
{
    public async Task<Result<AlertRuleDto>> Handle(
        GetAlertRuleByIdQuery request,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (rule is null)
            return Result<AlertRuleDto>.NotFound($"AlertRule with ID '{request.Id}' not found.");

        var dto = mapper.Map<AlertRuleDto>(rule);
        return Result<AlertRuleDto>.Ok(dto);
    }
}
