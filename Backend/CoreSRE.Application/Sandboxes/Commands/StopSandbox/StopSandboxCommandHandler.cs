using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.StopSandbox;

public class StopSandboxCommandHandler : IRequestHandler<StopSandboxCommand, Result<SandboxInstanceDto>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IPersistentSandboxManager _manager;
    private readonly IMapper _mapper;

    public StopSandboxCommandHandler(
        ISandboxInstanceRepository repository,
        IPersistentSandboxManager manager,
        IMapper mapper)
    {
        _repository = repository;
        _manager = manager;
        _mapper = mapper;
    }

    public async Task<Result<SandboxInstanceDto>> Handle(
        StopSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<SandboxInstanceDto>.NotFound();

        if (sandbox.Status != SandboxStatus.Running)
            return Result<SandboxInstanceDto>.Fail($"Sandbox must be Running to stop. Current: {sandbox.Status}");

        await _manager.StopAsync(sandbox, cancellationToken);
        await _repository.UpdateAsync(sandbox, cancellationToken);

        return Result<SandboxInstanceDto>.Ok(_mapper.Map<SandboxInstanceDto>(sandbox));
    }
}
