using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.DeleteDataSource;

public class DeleteDataSourceCommandHandler : IRequestHandler<DeleteDataSourceCommand, Result<bool>>
{
    private readonly IDataSourceRegistrationRepository _repository;

    public DeleteDataSourceCommandHandler(IDataSourceRegistrationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<bool>> Handle(
        DeleteDataSourceCommand request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (dataSource is null)
            return Result<bool>.NotFound($"DataSource with ID '{request.Id}' not found.");

        await _repository.DeleteAsync(request.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
