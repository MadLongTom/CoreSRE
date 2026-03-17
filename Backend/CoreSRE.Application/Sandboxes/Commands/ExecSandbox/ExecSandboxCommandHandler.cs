using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.ExecSandbox;

public class ExecSandboxCommandHandler : IRequestHandler<ExecSandboxCommand, Result<SandboxExecResult>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IPersistentSandboxManager _manager;

    public ExecSandboxCommandHandler(
        ISandboxInstanceRepository repository,
        IPersistentSandboxManager manager)
    {
        _repository = repository;
        _manager = manager;
    }

    public async Task<Result<SandboxExecResult>> Handle(
        ExecSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<SandboxExecResult>.NotFound();

        if (sandbox.Status != SandboxStatus.Running)
            return Result<SandboxExecResult>.Fail($"Sandbox must be Running to execute commands. Current: {sandbox.Status}");

        var result = await _manager.ExecAsync(sandbox, request.Command, request.Args, cancellationToken);

        // Update last activity
        await _repository.UpdateAsync(sandbox, cancellationToken);

        return Result<SandboxExecResult>.Ok(result);
    }
}
