using A2A;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Skills;
using CoreSRE.Domain.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.PgVector;
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
    private readonly ISandboxToolProvider _sandboxToolProvider;
    private readonly ISkillRegistrationRepository _skillRepo;
    private readonly IFileStorageService _fileStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentResolverService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _defaultMaxMessages;

    public AgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo,
        IHttpClientFactory httpClientFactory,
        IToolFunctionFactory toolFunctionFactory,
        ISandboxToolProvider sandboxToolProvider,
        ISkillRegistrationRepository skillRepo,
        IFileStorageService fileStorage,
        IConfiguration configuration,
        ILogger<AgentResolverService> logger,
        ILoggerFactory loggerFactory)
    {
        _agentRepo = agentRepo;
        _providerRepo = providerRepo;
        _httpClientFactory = httpClientFactory;
        _toolFunctionFactory = toolFunctionFactory;
        _sandboxToolProvider = sandboxToolProvider;
        _skillRepo = skillRepo;
        _fileStorage = fileStorage;
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
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

        // Mock Agent Mode — 如果启用，直接返回 MockChatClient，跳过真实 LLM 解析
        var mockModeSection = _configuration.GetSection("Workflow:MockAgentMode");
        var mockMode = string.Equals(mockModeSection?.Value, "true", StringComparison.OrdinalIgnoreCase);
        if (mockMode && agent.AgentType == Domain.Enums.AgentType.ChatClient)
        {
            _logger.LogInformation("Mock agent mode enabled — returning MockChatClient for agent '{AgentName}'", agent.Name);
            return CreateMockResolvedAgent(agent.Name, agent.LlmConfig);
        }

        return agent.AgentType switch
        {
            Domain.Enums.AgentType.ChatClient => await ResolveChatClientAgent(agent, conversationId),
            Domain.Enums.AgentType.A2A => ResolveA2AAgent(agent),
            _ => throw new NotSupportedException($"Agent type '{agent.AgentType}' is not supported for chat.")
        };
    }

    /// <summary>
    /// 创建 MockChatClient 包装的 ResolvedAgent。
    /// </summary>
    private ResolvedAgent CreateMockResolvedAgent(string agentName, Domain.ValueObjects.LlmConfigVO? llmConfig)
    {
        IChatClient mockClient = new MockChatClient(agentName);
        var options = new ChatClientAgentOptions
        {
            Name = agentName,
        };
        return new ResolvedAgent(mockClient.AsAIAgent(options), llmConfig);
    }

    private async Task<ResolvedAgent> ResolveChatClientAgent(Domain.Entities.AgentRegistration agent, string conversationId)
    {
        if (agent.LlmConfig is null)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM configuration.");

        // 2. 加载 LlmProvider
        if (agent.LlmConfig.ProviderId is null || agent.LlmConfig.ProviderId == Guid.Empty)
            throw new InvalidOperationException($"Agent '{agent.Name}' has no LLM provider configured.");

        var provider = await _providerRepo.GetByIdAsync(agent.LlmConfig.ProviderId.Value);
        if (provider is null)
        {
            _logger.LogWarning(
                "LlmProvider '{ProviderId}' not found for agent '{AgentName}' — falling back to MockChatClient",
                agent.LlmConfig.ProviderId, agent.Name);
            return CreateMockResolvedAgent(agent.Name, agent.LlmConfig);
        }

        // 3. 构建 OpenAIClient → IChatClient
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) });

        IChatClient chatClient = openAiClient
            .GetChatClient(agent.LlmConfig.ModelId)
            .AsIChatClient();

        // 3.5 如果有 ToolRefs，解析为 AIFunction 并用 FunctionInvokingChatClient 包装
        var allTools = new List<AIFunction>();

        if (agent.LlmConfig.ToolRefs is { Count: > 0 } toolRefs)
        {
            var refFunctions = await _toolFunctionFactory.CreateFunctionsAsync(toolRefs);
            allTools.AddRange(refFunctions);
        }

        // 3.6 如果启用沙盒，注入沙盒工具（命令行、文件读写、代码执行）
        if (agent.LlmConfig.EnableSandbox == true)
        {
            var sandboxTools = _sandboxToolProvider.CreateSandboxTools(agent.Id, conversationId, agent.LlmConfig);
            allTools.AddRange(sandboxTools);
            _logger.LogInformation(
                "Sandbox tools enabled for Agent '{AgentName}': {ToolCount} sandbox tools added",
                agent.Name, sandboxTools.Count);
        }

        // 3.7 如果绑定了 SkillRefs，注入渐进式披露工具（read_skill / read_skill_file）
        string? skillSummary = null;
        var configuredSkillRefs = agent.LlmConfig.SkillRefs;
        if (configuredSkillRefs is not null && configuredSkillRefs.Count > 0)
        {
            var skills = (await _skillRepo.GetActiveByIdsAsync(configuredSkillRefs)).ToList();
            if (skills.Count > 0)
            {
                // Build name → entity map for tool lookups
                var skillMap = skills.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

                // Inject read_skill tool (always)
                allTools.Add(new ReadSkillAIFunction(skillMap));

                // Inject read_skill_file tool only when at least one skill has files
                var hasFileSkills = skills.Any(s => s.HasFiles);
                if (hasFileSkills)
                {
                    allTools.Add(new ReadSkillFileAIFunction(skillMap, _fileStorage));
                }

                // Build skill summary for SystemPrompt injection
                skillSummary = SkillPromptBuilder.BuildSkillSummary(skills);
                var toolsSuffix = hasFileSkills ? " + read_skill_file" : "";
                _logger.LogInformation(
                    "Skills injected for Agent '{AgentName}': {SkillCount} skills, tools: read_skill{HasFilesTool}",
                    agent.Name, skills.Count, toolsSuffix);
            }
        }

        if (allTools.Count > 0)
        {
            chatClient = chatClient
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
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

        // Append skill summary to the end of Instructions (progressive disclosure)
        if (!string.IsNullOrWhiteSpace(skillSummary))
        {
            chatOptions.Instructions = (chatOptions.Instructions ?? string.Empty) + skillSummary;
        }

        // 将所有 AIFunction（ToolRef + Sandbox）附加到 ChatOptions.Tools
        if (allTools.Count > 0)
        {
            chatOptions.Tools = allTools.Cast<AITool>().ToList();
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
                // Resolve embedding provider: prefer EmbeddingProviderId, fallback to ProviderId
                var embProviderId = agent.LlmConfig.EmbeddingProviderId ?? agent.LlmConfig.ProviderId;
                var embModelId = agent.LlmConfig.EmbeddingModelId;

                if (embProviderId is null || embProviderId == Guid.Empty || string.IsNullOrWhiteSpace(embModelId))
                {
                    _logger.LogWarning(
                        "Semantic memory enabled for Agent '{AgentName}' but EmbeddingProviderId/EmbeddingModelId not configured. Skipping memory.",
                        agent.Name);
                }
                else
                {
                    var embProvider = await _providerRepo.GetByIdAsync(embProviderId.Value)
                        ?? throw new InvalidOperationException($"Embedding LlmProvider '{embProviderId}' not found.");

                    // Build IEmbeddingGenerator from the provider's OpenAI-compatible endpoint
                    var embClient = new OpenAIClient(
                        new ApiKeyCredential(embProvider.ApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(embProvider.BaseUrl) });

                    var embeddingGenerator = embClient
                        .GetEmbeddingClient(embModelId)
                        .AsIEmbeddingGenerator();

                    // Build pgvector VectorStore backed by the same PostgreSQL instance
                    var connStr = _configuration.GetConnectionString("coresre")!;
                    var vectorStore = new PostgresVectorStore(connStr, new PostgresVectorStoreOptions
                    {
                        EmbeddingGenerator = embeddingGenerator
                    });

                    var collectionName = _configuration["SemanticMemory:CollectionName"] ?? "coresre_memory";
                    var vectorDimensions = agent.LlmConfig.EmbeddingDimensions ?? 1536;
                    var maxResults = agent.LlmConfig.MemoryMaxResults;
                    var searchMode = agent.LlmConfig.MemorySearchMode;
                    var minRelevanceScore = agent.LlmConfig.MemoryMinRelevanceScore ?? 0.0;

                    var memoryOptions = new ChatHistoryMemoryProviderOptions
                    {
                        MaxResults = maxResults is > 0 ? maxResults.Value : null,
                        SearchTime = searchMode?.Equals("OnDemandFunctionCalling", StringComparison.OrdinalIgnoreCase) == true
                            ? ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling
                            : ChatHistoryMemoryProviderOptions.SearchBehavior.BeforeAIInvoke
                    };

                    // Capture loggerFactory for error visibility inside ChatHistoryMemoryProvider.
                    // Without loggerFactory, any errors in InvokedCoreAsync/InvokingCoreAsync
                    // are silently swallowed (catch block checks _logger?.IsEnabled which is null).
                    var memLoggerFactory = _loggerFactory;

                    options.AIContextProviderFactory = (ctx, ct) =>
                    {
                        // When restoring a session from store, ctx.SerializedState contains
                        // the previously serialized scope info.
                        if (ctx.SerializedState.ValueKind == JsonValueKind.Object)
                        {
                            var memProvider = new FixedChatHistoryMemoryProvider(
                                vectorStore,
                                collectionName,
                                vectorDimensions,
                                ctx.SerializedState,
                                ctx.JsonSerializerOptions,
                                options: memoryOptions,
                                loggerFactory: memLoggerFactory,
                                minRelevanceScore: minRelevanceScore);

                            return ValueTask.FromResult<AIContextProvider>(memProvider);
                        }
                        else
                        {
                            var storageScope = new ChatHistoryMemoryProviderScope
                            {
                                ApplicationId = "CoreSRE",
                                AgentId = agent.Id.ToString(),
                                SessionId = conversationId
                            };

                            var memProvider = new FixedChatHistoryMemoryProvider(
                                vectorStore,
                                collectionName,
                                vectorDimensions,
                                storageScope,
                                options: memoryOptions,
                                loggerFactory: memLoggerFactory,
                                minRelevanceScore: minRelevanceScore);

                            return ValueTask.FromResult<AIContextProvider>(memProvider);
                        }
                    };

                    _logger.LogInformation(
                        "Semantic memory configured for Agent '{AgentName}' using embedding model '{EmbeddingModel}' from provider '{ProviderName}'.",
                        agent.Name, embModelId, embProvider.Name);
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
