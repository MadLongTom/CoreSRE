using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.UpdateSandbox;

public class UpdateSandboxCommandHandler : IRequestHandler<UpdateSandboxCommand, Result<SandboxInstanceDto>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IMapper _mapper;

    public UpdateSandboxCommandHandler(ISandboxInstanceRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<SandboxInstanceDto>> Handle(
        UpdateSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<SandboxInstanceDto>.NotFound();

        if (sandbox.Status != SandboxStatus.Stopped)
            return Result<SandboxInstanceDto>.Fail("Sandbox config can only be updated in Stopped state.");

        sandbox.UpdateConfig(
            name: request.Name ?? sandbox.Name,
            image: request.Image,
            cpuCores: request.CpuCores,
            memoryMib: request.MemoryMib,
            autoStopMinutes: request.AutoStopMinutes,
            persistWorkspace: request.PersistWorkspace,
            agentId: request.AgentId);

        await _repository.UpdateAsync(sandbox, cancellationToken);

        return Result<SandboxInstanceDto>.Ok(_mapper.Map<SandboxInstanceDto>(sandbox));
    }
}
