using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.DiscoverMetadata;

public class DiscoverDataSourceMetadataCommandHandler
    : IRequestHandler<DiscoverDataSourceMetadataCommand, Result<DataSourceMetadataVO>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly IDataSourceQuerierFactory _querierFactory;

    public DiscoverDataSourceMetadataCommandHandler(
        IDataSourceRegistrationRepository repository,
        IDataSourceQuerierFactory querierFactory)
    {
        _repository = repository;
        _querierFactory = querierFactory;
    }

    public async Task<Result<DataSourceMetadataVO>> Handle(
        DiscoverDataSourceMetadataCommand request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.DataSourceId, cancellationToken);
        if (dataSource is null)
            return Result<DataSourceMetadataVO>.NotFound(
                $"DataSource with ID '{request.DataSourceId}' not found.");

        var querier = _querierFactory.GetQuerier(dataSource.Product);
        var metadata = await querier.DiscoverMetadataAsync(dataSource, cancellationToken);

        // Update metadata in entity
        dataSource.UpdateMetadata(metadata);
        await _repository.UpdateAsync(dataSource, cancellationToken);

        return Result<DataSourceMetadataVO>.Ok(metadata);
    }
}
