using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.DeleteSandbox;

public class DeleteSandboxCommandHandler : IRequestHandler<DeleteSandboxCommand, Result<bool>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IPersistentSandboxManager _manager;

    public DeleteSandboxCommandHandler(
        ISandboxInstanceRepository repository,
        IPersistentSandboxManager manager)
    {
        _repository = repository;
        _manager = manager;
    }

    public async Task<Result<bool>> Handle(
        DeleteSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<bool>.NotFound();

        await _manager.TerminateAsync(sandbox, cancellationToken);
        await _repository.UpdateAsync(sandbox, cancellationToken);

        // Delete from database
        await _repository.DeleteAsync(sandbox.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
