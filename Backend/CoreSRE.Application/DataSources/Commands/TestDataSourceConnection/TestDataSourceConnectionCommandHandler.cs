using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.TestDataSourceConnection;

public class TestDataSourceConnectionCommandHandler
    : IRequestHandler<TestDataSourceConnectionCommand, Result<DataSourceHealthVO>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IDataSourceQuerierFactory _querierFactory;

    public TestDataSourceConnectionCommandHandler(
        IDataSourceRegistrationRepository repository,
        IDataSourceQuerierFactory querierFactory)
    {
        _repository = repository;
        _querierFactory = querierFactory;
    }

    public async Task<Result<DataSourceHealthVO>> Handle(
        TestDataSourceConnectionCommand request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.DataSourceId, cancellationToken);
        if (dataSource is null)
            return Result<DataSourceHealthVO>.NotFound(
                $"DataSource with ID '{request.DataSourceId}' not found.");

        var querier = _querierFactory.GetQuerier(dataSource.Product);
        var health = await querier.HealthCheckAsync(dataSource, cancellationToken);

        // Update health check state in entity
        dataSource.UpdateHealthCheck(health);
        await _repository.UpdateAsync(dataSource, cancellationToken);

        return Result<DataSourceHealthVO>.Ok(health);
    }
}
