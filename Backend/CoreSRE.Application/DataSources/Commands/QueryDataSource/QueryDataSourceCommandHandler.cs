using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.QueryDataSource;

public class QueryDataSourceCommandHandler
    : IRequestHandler<QueryDataSourceCommand, Result<DataSourceResultVO>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IDataSourceQuerierFactory _querierFactory;

    public QueryDataSourceCommandHandler(
        IDataSourceRegistrationRepository repository,
        IDataSourceQuerierFactory querierFactory)
    {
        _repository = repository;
        _querierFactory = querierFactory;
    }

    public async Task<Result<DataSourceResultVO>> Handle(
        QueryDataSourceCommand request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.DataSourceId, cancellationToken);
        if (dataSource is null)
            return Result<DataSourceResultVO>.NotFound(
                $"DataSource with ID '{request.DataSourceId}' not found.");

        var querier = _querierFactory.GetQuerier(dataSource.Product);
        var resultVO = await querier.QueryAsync(dataSource, request.Query, cancellationToken);

        return Result<DataSourceResultVO>.Ok(resultVO);
    }
}
