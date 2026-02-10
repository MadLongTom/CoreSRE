using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Agent 解析服务 — 从 AgentRegistration ID 构建就绪的 ChatClientAgent。
/// 流程：AgentRegistration → LlmProvider → OpenAIClient → IChatClient → AsAIAgent → ChatClientAgent
/// AG-UI 协议是无状态的（每次请求携带完整消息历史），因此无需配置 ChatHistoryProvider。
/// 对话历史持久化由 AgentSessionRecord + AG-UI 事件流自动处理。
/// </summary>
public class AgentResolverService : IAgentResolver
{
    private readonly IAgentRegistrationRepository _agentRepo;
    private readonly ILlmProviderRepository _providerRepo;

    public AgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo)
    {
        _agentRepo = agentRepo;
        _providerRepo = providerRepo;
    }

    public async Task<AIAgent> ResolveAsync(
        Guid agentRegistrationId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        // 1. 加载 AgentRegistration
        var agent = await _agentRepo.GetByIdAsync(agentRegistrationId, cancellationToken)
            ?? throw new InvalidOperationException($"AgentRegistration '{agentRegistrationId}' not found.");

        if (agent.AgentType != Domain.Enums.AgentType.ChatClient)
            throw new NotSupportedException($"Agent type '{agent.AgentType}' is not supported for chat. Only ChatClient agents are supported.");

        if (agent.LlmConfig is null)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM configuration.");

        // 2. 加载 LlmProvider
        if (agent.LlmConfig.ProviderId is null || agent.LlmConfig.ProviderId == Guid.Empty)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM provider configured.");

        var provider = await _providerRepo.GetByIdAsync(agent.LlmConfig.ProviderId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"LlmProvider '{agent.LlmConfig.ProviderId}' not found.");

        // 3. 构建 OpenAIClient → IChatClient
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) });

        IChatClient chatClient = openAiClient
            .GetChatClient(agent.LlmConfig.ModelId)
            .AsIChatClient();

        // 4. 创建 ChatClientAgent（AG-UI 无状态模式 — 消息随请求体发送）
        var options = new ChatClientAgentOptions
        {
            Name = agent.Name,
            Description = agent.Description ?? string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(agent.LlmConfig.Instructions))
        {
            options.ChatOptions = new ChatOptions
            {
                Instructions = agent.LlmConfig.Instructions
            };
        }

        return chatClient.AsAIAgent(options);
    }
}
