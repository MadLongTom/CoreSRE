using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.DataSources.Queries.GetDataSourceById;

public class GetDataSourceByIdQueryHandler
    : IRequestHandler<GetDataSourceByIdQuery, Result<DataSourceRegistrationDto>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IMapper _mapper;

    public GetDataSourceByIdQueryHandler(IDataSourceRegistrationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<DataSourceRegistrationDto>> Handle(
        GetDataSourceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (dataSource is null)
            return Result<DataSourceRegistrationDto>.NotFound($"DataSource with ID '{request.Id}' not found.");

        var dto = _mapper.Map<DataSourceRegistrationDto>(dataSource);
        return Result<DataSourceRegistrationDto>.Ok(dto);
    }
}
