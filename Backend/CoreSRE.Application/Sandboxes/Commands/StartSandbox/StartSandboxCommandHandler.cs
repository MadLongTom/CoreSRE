using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.StartSandbox;

public class StartSandboxCommandHandler : IRequestHandler<StartSandboxCommand, Result<SandboxInstanceDto>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IPersistentSandboxManager _manager;
    private readonly IMapper _mapper;

    public StartSandboxCommandHandler(
        ISandboxInstanceRepository repository,
        IPersistentSandboxManager manager,
        IMapper mapper)
    {
        _repository = repository;
        _manager = manager;
        _mapper = mapper;
    }

    public async Task<Result<SandboxInstanceDto>> Handle(
        StartSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<SandboxInstanceDto>.NotFound();

        if (sandbox.Status != SandboxStatus.Stopped)
            return Result<SandboxInstanceDto>.Fail($"Sandbox must be in Stopped state to start. Current: {sandbox.Status}");

        try
        {
            await _manager.StartAsync(sandbox, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw SandboxOperationException.FromInfrastructureError(
                "Start", sandbox.Name, sandbox.Image, ex);
        }

        await _repository.UpdateAsync(sandbox, cancellationToken);

        return Result<SandboxInstanceDto>.Ok(_mapper.Map<SandboxInstanceDto>(sandbox));
    }
}
