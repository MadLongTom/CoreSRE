using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Commands.CreateSandbox;

/// <summary>
/// 创建沙箱命令处理器
/// </summary>
public class CreateSandboxCommandHandler : IRequestHandler<CreateSandboxCommand, Result<SandboxInstanceDto>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IPersistentSandboxManager _manager;
    private readonly IMapper _mapper;

    public CreateSandboxCommandHandler(
        ISandboxInstanceRepository repository,
        IPersistentSandboxManager manager,
        IMapper mapper)
    {
        _repository = repository;
        _manager = manager;
        _mapper = mapper;
    }

    public async Task<Result<SandboxInstanceDto>> Handle(
        CreateSandboxCommand request, CancellationToken cancellationToken)
    {
        var sandbox = SandboxInstance.Create(
            name: request.Name,
            sandboxType: request.SandboxType,
            image: request.Image,
            cpuCores: request.CpuCores,
            memoryMib: request.MemoryMib,
            k8sNamespace: request.K8sNamespace,
            autoStopMinutes: request.AutoStopMinutes,
            persistWorkspace: request.PersistWorkspace,
            agentId: request.AgentId);

        await _repository.AddAsync(sandbox, cancellationToken);

        // Create K8s Pod — wrap infrastructure errors with user-friendly context
        try
        {
            await _manager.CreateAsync(sandbox, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mark sandbox as terminated since Pod creation failed
            sandbox.MarkTerminated();
            await _repository.UpdateAsync(sandbox, cancellationToken);

            throw SandboxOperationException.FromInfrastructureError(
                "Create", sandbox.Name, sandbox.Image, ex);
        }

        // Update with PodName after Pod creation
        await _repository.UpdateAsync(sandbox, cancellationToken);

        var dto = _mapper.Map<SandboxInstanceDto>(sandbox);
        return Result<SandboxInstanceDto>.Ok(dto);
    }
}
