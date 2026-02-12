using A2A;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using OpenAI;
using System.ClientModel;
using CoreSRE.Infrastructure.Persistence.Sessions;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Agent 解析服务 — 从 AgentRegistration ID 构建就绪的 AIAgent。
/// ChatClient 类型：AgentRegistration → LlmProvider → OpenAIClient → IChatClient → ChatClientAgent
/// A2A 类型：AgentRegistration → Endpoint → A2AClient → A2AAgent
/// 当 EnableChatHistory=true 时，配置 ChatHistoryProviderFactory 实现框架管理的会话历史。
/// 当 EnableSemanticMemory=true 时，配置 AIContextProviderFactory 实现跨会话语义记忆。
/// 对话历史通过 AgentSessionStore + AIHostAgent 管道持久化到 PostgreSQL。
/// </summary>
public class AgentResolverService : IAgentResolver
{
    private readonly IAgentRegistrationRepository _agentRepo;
    private readonly ILlmProviderRepository _providerRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IToolFunctionFactory _toolFunctionFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentResolverService> _logger;
    private readonly int _defaultMaxMessages;

    public AgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo,
        IHttpClientFactory httpClientFactory,
        IToolFunctionFactory toolFunctionFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<AgentResolverService> logger)
    {
        _agentRepo = agentRepo;
        _providerRepo = providerRepo;
        _httpClientFactory = httpClientFactory;
        _toolFunctionFactory = toolFunctionFactory;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _defaultMaxMessages = configuration.GetValue<int>("ChatHistory:DefaultMaxMessages", 50);
    }

    public async Task<ResolvedAgent> ResolveAsync(
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

    private async Task<ResolvedAgent> ResolveChatClientAgent(Domain.Entities.AgentRegistration agent)
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

        // 4. 创建 ChatClientAgent
        var options = new ChatClientAgentOptions
        {
            Id = agent.Id.ToString(),
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

        // ── History & Memory 配置 ────────────────────────────────────────
        var enableHistory = agent.LlmConfig.EnableChatHistory ?? true;
        if (enableHistory)
        {
            var maxMessages = agent.LlmConfig.MaxHistoryMessages;
            // Treat 0 or negative as null (platform default)
            var effectiveMax = maxMessages is > 0 ? maxMessages.Value : _defaultMaxMessages;

            options.ChatHistoryProviderFactory = (ctx, ct) =>
            {
                IChatReducer reducer = new MessageCountingChatReducer(effectiveMax);

                // Use our JSONB-safe provider instead of InMemoryChatHistoryProvider.
                // InMemoryChatHistoryProvider serializes with STJ $type discriminators;
                // PostgreSQL JSONB reorders keys alphabetically, breaking $type positioning
                // which must be first for STJ polymorphic deserialization.
                // PostgresChatHistoryProvider uses explicit "kind" field — immune to key reordering.
                ChatHistoryProvider provider = ctx.SerializedState.ValueKind == JsonValueKind.Object
                    ? new PostgresChatHistoryProvider(reducer, ctx.SerializedState)
                    : new PostgresChatHistoryProvider(reducer);

                return ValueTask.FromResult(provider);
            };
        }

        // ── Semantic Memory 配置 (AIContextProviderFactory) ──────────────
        var enableMemory = agent.LlmConfig.EnableSemanticMemory ?? false;
        if (enableMemory)
        {
            try
            {
                var vectorStore = _serviceProvider.GetService<VectorStore>();

                if (vectorStore is not null)
                {
                    var memorySection = _configuration.GetSection("SemanticMemory");
                    var collectionName = memorySection["CollectionName"] ?? "coresre_memory";
                    var vectorDimensions = memorySection.GetValue<int>("EmbeddingDimensions", 1536);
                    var maxResults = agent.LlmConfig.MemoryMaxResults;
                    var searchMode = agent.LlmConfig.MemorySearchMode;

                    var memoryOptions = new ChatHistoryMemoryProviderOptions
                    {
                        MaxResults = maxResults is > 0 ? maxResults.Value : null,
                        SearchTime = searchMode?.Equals("OnDemandFunctionCalling", StringComparison.OrdinalIgnoreCase) == true
                            ? ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling
                            : ChatHistoryMemoryProviderOptions.SearchBehavior.BeforeAIInvoke
                    };

                    options.AIContextProviderFactory = (ctx, ct) =>
                    {
                        var storageScope = new ChatHistoryMemoryProviderScope
                        {
                            ApplicationId = "CoreSRE",
                            AgentId = agent.Id.ToString()
                        };

                        var provider = new ChatHistoryMemoryProvider(
                            vectorStore,
                            collectionName,
                            vectorDimensions,
                            storageScope,
                            options: memoryOptions);

                        return ValueTask.FromResult<AIContextProvider>(provider);
                    };
                }
                else
                {
                    _logger.LogWarning(
                        "Semantic memory enabled for Agent '{AgentName}' but VectorStore not registered. Skipping memory configuration.",
                        agent.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to configure semantic memory for Agent '{AgentName}'. Agent will operate without cross-session memory.",
                    agent.Name);
            }
        }

        return new ResolvedAgent(chatClient.AsAIAgent(options), agent.LlmConfig);
    }

    private ResolvedAgent ResolveA2AAgent(Domain.Entities.AgentRegistration agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Endpoint))
            throw new InvalidOperationException($"A2A Agent '{agent.Name}' has no endpoint configured.");

        var httpClient = _httpClientFactory.CreateClient("A2ACardResolver");
        var a2aClient = new A2AClient(new Uri(agent.Endpoint), httpClient);

        return new ResolvedAgent(
            a2aClient.AsAIAgent(name: agent.Name, description: agent.Description),
            null);
    }
}
