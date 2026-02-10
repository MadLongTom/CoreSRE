using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.DeleteProvider;

/// <summary>
/// 删除 LLM Provider 命令处理器。
/// 删除前检查是否有 Agent 引用此 Provider。
/// </summary>
public class DeleteProviderCommandHandler : IRequestHandler<DeleteProviderCommand, Result<bool>>
{
    private readonly ILlmProviderRepository _repository;
    private readonly IAgentRegistrationRepository _agentRepository;

    public DeleteProviderCommandHandler(
        ILlmProviderRepository repository,
        IAgentRegistrationRepository agentRepository)
    {
        _repository = repository;
        _agentRepository = agentRepository;
    }

    public async Task<Result<bool>> Handle(
        DeleteProviderCommand request,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (provider is null)
            return Result<bool>.NotFound($"Provider with ID '{request.Id}' not found.");

        // Check for agent references — get all agents and filter by ProviderId
        var allAgents = await _agentRepository.GetAllAsync(cancellationToken);
        var referencingAgents = allAgents
            .Where(a => a.LlmConfig?.ProviderId == request.Id)
            .ToList();

        if (referencingAgents.Count > 0)
        {
            return Result<bool>.Conflict(
                $"Cannot delete provider '{provider.Name}': {referencingAgents.Count} agent(s) still reference it.");
        }

        await _repository.DeleteAsync(request.Id, cancellationToken);
        return Result<bool>.Ok(true, $"Provider '{provider.Name}' deleted successfully.");
    }
}
