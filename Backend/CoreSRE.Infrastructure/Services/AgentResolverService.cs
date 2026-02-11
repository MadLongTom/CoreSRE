using A2A;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Agent 解析服务 — 从 AgentRegistration ID 构建就绪的 AIAgent。
/// ChatClient 类型：AgentRegistration → LlmProvider → OpenAIClient → IChatClient → ChatClientAgent
/// A2A 类型：AgentRegistration → Endpoint → A2AClient → A2AAgent
/// AG-UI 协议是无状态的（每次请求携带完整消息历史），因此无需配置 ChatHistoryProvider。
/// 对话历史持久化由 AgentSessionRecord + AG-UI 事件流自动处理。
/// </summary>
public class AgentResolverService : IAgentResolver
{
    private readonly IAgentRegistrationRepository _agentRepo;
    private readonly ILlmProviderRepository _providerRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolFunctionFactory _toolFunctionFactory;

    public AgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo,
        IHttpClientFactory httpClientFactory,
        IToolFunctionFactory toolFunctionFactory)
    {
        _agentRepo = agentRepo;
        _providerRepo = providerRepo;
        _httpClientFactory = httpClientFactory;
        _toolFunctionFactory = toolFunctionFactory;
    }

    public async Task<AIAgent> ResolveAsync(
        Guid agentRegistrationId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        // 1. 加载 AgentRegistration
        var agent = await _agentRepo.GetByIdAsync(agentRegistrationId, cancellationToken)
            ?? throw new InvalidOperationException($"AgentRegistration '{agentRegistrationId}' not found.");

        return agent.AgentType switch
        {
            Domain.Enums.AgentType.ChatClient => await ResolveChatClientAgent(agent),
            Domain.Enums.AgentType.A2A => ResolveA2AAgent(agent),
            _ => throw new NotSupportedException($"Agent type '{agent.AgentType}' is not supported for chat.")
        };
    }

    private async Task<AIAgent> ResolveChatClientAgent(Domain.Entities.AgentRegistration agent)
    {
        if (agent.LlmConfig is null)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM configuration.");

        // 2. 加载 LlmProvider
        if (agent.LlmConfig.ProviderId is null || agent.LlmConfig.ProviderId == Guid.Empty)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM provider configured.");

        var provider = await _providerRepo.GetByIdAsync(agent.LlmConfig.ProviderId.Value)
            ?? throw new InvalidOperationException($"LlmProvider '{agent.LlmConfig.ProviderId}' not found.");

        // 3. 构建 OpenAIClient → IChatClient
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) });

        IChatClient chatClient = openAiClient
            .GetChatClient(agent.LlmConfig.ModelId)
            .AsIChatClient();

        // 3.5 如果有 ToolRefs，解析为 AIFunction 并用 FunctionInvokingChatClient 包装
        IReadOnlyList<AIFunction>? aiFunctions = null;
        if (agent.LlmConfig.ToolRefs is { Count: > 0 } toolRefs)
        {
            aiFunctions = await _toolFunctionFactory.CreateFunctionsAsync(toolRefs);

            if (aiFunctions.Count > 0)
            {
                chatClient = chatClient
                    .AsBuilder()
                    .UseFunctionInvocation()
                    .Build();
            }
        }

        // 4. 创建 ChatClientAgent（AG-UI 无状态模式 — 消息随请求体发送）
        var options = new ChatClientAgentOptions
        {
            Name = agent.Name,
            Description = agent.Description ?? string.Empty,
        };

        var chatOptions = new ChatOptions();

        if (!string.IsNullOrWhiteSpace(agent.LlmConfig.Instructions))
        {
            chatOptions.Instructions = agent.LlmConfig.Instructions;
        }

        // 将 AIFunction 列表附加到 ChatOptions.Tools
        if (aiFunctions is { Count: > 0 })
        {
            chatOptions.Tools = aiFunctions.Cast<AITool>().ToList();
        }

        // ── 应用 ChatOptions 扩展配置 ──────────────────────────────────────
        chatOptions.Temperature = agent.LlmConfig.Temperature;
        chatOptions.MaxOutputTokens = agent.LlmConfig.MaxOutputTokens;
        chatOptions.TopP = agent.LlmConfig.TopP;
        chatOptions.TopK = agent.LlmConfig.TopK;
        chatOptions.FrequencyPenalty = agent.LlmConfig.FrequencyPenalty;
        chatOptions.PresencePenalty = agent.LlmConfig.PresencePenalty;
        chatOptions.Seed = agent.LlmConfig.Seed;

        if (agent.LlmConfig.StopSequences is { Count: > 0 })
        {
            chatOptions.StopSequences = agent.LlmConfig.StopSequences;
        }

        if (agent.LlmConfig.AllowMultipleToolCalls is not null)
        {
            chatOptions.AllowMultipleToolCalls = agent.LlmConfig.AllowMultipleToolCalls;
        }

        if (!string.IsNullOrWhiteSpace(agent.LlmConfig.ResponseFormat))
        {
            chatOptions.ResponseFormat = agent.LlmConfig.ResponseFormat switch
            {
                "Json" when !string.IsNullOrWhiteSpace(agent.LlmConfig.ResponseFormatSchema) =>
                    ChatResponseFormat.ForJsonSchema(
                        System.Text.Json.JsonDocument.Parse(agent.LlmConfig.ResponseFormatSchema).RootElement),
                "Json" => ChatResponseFormat.Json,
                "Text" => ChatResponseFormat.Text,
                _ => null
            };
        }

        if (!string.IsNullOrWhiteSpace(agent.LlmConfig.ToolMode))
        {
            chatOptions.ToolMode = agent.LlmConfig.ToolMode switch
            {
                "Auto" => ChatToolMode.Auto,
                "Required" => ChatToolMode.RequireAny,
                "None" => ChatToolMode.None,
                _ => null
            };
        }

        options.ChatOptions = chatOptions;

        return chatClient.AsAIAgent(options);
    }

    private AIAgent ResolveA2AAgent(Domain.Entities.AgentRegistration agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Endpoint))
            throw new InvalidOperationException($"A2A Agent '{agent.Name}' has no endpoint configured.");

        var httpClient = _httpClientFactory.CreateClient("A2ACardResolver");
        var a2aClient = new A2AClient(new Uri(agent.Endpoint), httpClient);

        return a2aClient.AsAIAgent(
            name: agent.Name,
            description: agent.Description);
    }
}
