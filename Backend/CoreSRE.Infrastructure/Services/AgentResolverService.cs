using A2A;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Skills;
using CoreSRE.Domain.Entities;
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
    private readonly IDataSourceFunctionFactory _dataSourceFunctionFactory;
    private readonly ISandboxToolProvider _sandboxToolProvider;
    private readonly ISkillRegistrationRepository _skillRepo;
    private readonly IFileStorageService _fileStorage;
    private readonly ITeamOrchestrator _teamOrchestrator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentResolverService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _defaultMaxMessages;

    public AgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo,
        IHttpClientFactory httpClientFactory,
        IToolFunctionFactory toolFunctionFactory,
        IDataSourceFunctionFactory dataSourceFunctionFactory,
        ISandboxToolProvider sandboxToolProvider,
        ISkillRegistrationRepository skillRepo,
        IFileStorageService fileStorage,
        ITeamOrchestrator teamOrchestrator,
        IConfiguration configuration,
        ILogger<AgentResolverService> logger,
        ILoggerFactory loggerFactory)
    {
        _agentRepo = agentRepo;
        _providerRepo = providerRepo;
        _httpClientFactory = httpClientFactory;
        _toolFunctionFactory = toolFunctionFactory;
        _dataSourceFunctionFactory = dataSourceFunctionFactory;
        _sandboxToolProvider = sandboxToolProvider;
        _skillRepo = skillRepo;
        _fileStorage = fileStorage;
        _teamOrchestrator = teamOrchestrator;
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
            Domain.Enums.AgentType.Team => await ResolveTeamAgent(agent, conversationId, cancellationToken),
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

        // 规范化 tool call Arguments：防止 Bedrock/Anthropic 代理因 null Arguments 返回 400
        chatClient = new ToolCallNormalizingChatClient(chatClient);

        // 3.5 如果有 ToolRefs，解析为 AIFunction 并用 FunctionInvokingChatClient 包装
        var allTools = new List<AIFunction>();

        if (agent.LlmConfig.ToolRefs is { Count: > 0 } toolRefs)
        {
            var refFunctions = await _toolFunctionFactory.CreateFunctionsAsync(toolRefs);
            allTools.AddRange(refFunctions);
        }

        // 3.5.1 如果有 DataSourceRefs，解析为 AIFunction
        if (agent.LlmConfig.DataSourceRefs is { Count: > 0 } dsRefs)
        {
            var dsFunctions = await _dataSourceFunctionFactory.CreateFunctionsAsync(dsRefs);
            allTools.AddRange(dsFunctions);
            _logger.LogInformation(
                "DataSource functions enabled for Agent '{AgentName}': {FuncCount} functions from {RefCount} datasource refs",
                agent.Name, dsFunctions.Count, dsRefs.Count);
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

        // 3.7 如果绑定了 SkillRefs，构建 S3AgentSkillsProvider
        // 规则：HasFiles=true 的 Skill 需要沙箱支持，无沙箱时跳过这些 Skill 并警告
        S3AgentSkillsProvider? skillsProvider = null;
        var sandboxEnabled = agent.LlmConfig.EnableSandbox == true;
        var configuredSkillRefs = agent.LlmConfig.SkillRefs;
        if (configuredSkillRefs is not null && configuredSkillRefs.Count > 0)
        {
            var allSkills = (await _skillRepo.GetActiveByIdsAsync(configuredSkillRefs)).ToList();
            if (allSkills.Count > 0)
            {
                // 无沙箱时，过滤掉 HasFiles=true 的 Skill（文件包 Skill 必须搭配沙箱使用）
                List<SkillRegistration> skills;
                if (sandboxEnabled)
                {
                    skills = allSkills;
                }
                else
                {
                    var skippedSkills = allSkills.Where(s => s.HasFiles).ToList();
                    skills = allSkills.Where(s => !s.HasFiles).ToList();
                    if (skippedSkills.Count > 0)
                    {
                        _logger.LogWarning(
                            "Agent '{AgentName}': {Count} skill(s) with file packages skipped (no sandbox enabled): {Names}",
                            agent.Name, skippedSkills.Count,
                            string.Join(", ", skippedSkills.Select(s => s.Name)));
                    }
                }

                if (skills.Count > 0)
                {
                    // Create S3-backed skills provider (follows same progressive disclosure
                    // pattern as the framework's FileAgentSkillsProvider).
                    // Tools (load_skill, read_skill_resource) and Instructions are injected
                    // automatically via the AIContextProvider lifecycle — no manual wiring needed.
                    skillsProvider = new S3AgentSkillsProvider(
                        skills,
                        _fileStorage,
                        sandboxEnabled,
                        loggerFactory: _loggerFactory);

                    _logger.LogInformation(
                        "S3AgentSkillsProvider configured for Agent '{AgentName}': {SkillCount} skills",
                        agent.Name, skills.Count);
                }
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

            IChatReducer reducer = new MessageCountingChatReducer(effectiveMax);

            // Use our JSONB-safe provider instead of InMemoryChatHistoryProvider.
            // InMemoryChatHistoryProvider serializes with STJ $type discriminators;
            // PostgreSQL JSONB reorders keys alphabetically, breaking $type positioning
            // which must be first for STJ polymorphic deserialization.
            // PostgresChatHistoryProvider uses explicit "kind" field — immune to key reordering.
            // Provider is a singleton instance; per-session state lives in AgentSession.StateBag.
            options.ChatHistoryProvider = new PostgresChatHistoryProvider(reducer);
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

                    var storageScope = new ChatHistoryMemoryProviderScope
                    {
                        ApplicationId = "CoreSRE",
                        AgentId = agent.Id.ToString(),
                        SessionId = conversationId
                    };

                    // Provider is a singleton instance per agent;
                    // per-session state lives in AgentSession.StateBag.
                    var memProvider = new FixedChatHistoryMemoryProvider(
                        vectorStore,
                        collectionName,
                        vectorDimensions,
                        storageScope,
                        options: memoryOptions,
                        loggerFactory: memLoggerFactory,
                        minRelevanceScore: minRelevanceScore);

                    options.AIContextProviders = [memProvider];

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

        // ── AIContextProviders 汇总 ── Skills + Memory ─────────────────
        // Merge all configured AIContextProviders into a single list.
        // The S3AgentSkillsProvider injects skill tools + instructions via the
        // AIContextProvider lifecycle; the memory provider handles semantic search.
        {
            var aiContextProviders = new List<AIContextProvider>();
            if (skillsProvider is not null)
            {
                aiContextProviders.Add(skillsProvider);
            }
            if (options.AIContextProviders is not null && options.AIContextProviders.Any())
            {
                aiContextProviders.AddRange(options.AIContextProviders);
            }
            if (aiContextProviders.Count > 0)
            {
                options.AIContextProviders = aiContextProviders;
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

    /// <summary>
    /// Team Agent resolution — resolves each participant recursively, then composes
    /// into a Workflow-backed AIAgent via ITeamOrchestrator.
    /// </summary>
    private async Task<ResolvedAgent> ResolveTeamAgent(
        Domain.Entities.AgentRegistration agent,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var teamConfig = agent.TeamConfig
            ?? throw new InvalidOperationException($"Team Agent '{agent.Name}' has no TeamConfig.");

        _logger.LogInformation(
            "Resolving Team Agent '{AgentName}' with mode {Mode} and {Count} participants",
            agent.Name, teamConfig.Mode, teamConfig.ParticipantIds.Count);

        // Resolve each participant agent (ChatClient or A2A — no Team nesting allowed)
        var participants = new List<ResolvedAgent>();
        foreach (var participantId in teamConfig.ParticipantIds)
        {
            var participantReg = await _agentRepo.GetByIdAsync(participantId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Participant agent '{participantId}' not found for Team '{agent.Name}'.");

            if (participantReg.AgentType == Domain.Enums.AgentType.Team)
                throw new InvalidOperationException(
                    $"Team nesting is not allowed: participant '{participantReg.Name}' is itself a Team agent.");

            if (participantReg.Status != Domain.Enums.AgentStatus.Active &&
                participantReg.Status != Domain.Enums.AgentStatus.Registered)
            {
                throw new InvalidOperationException(
                    $"Participant agent '{participantReg.Name}' is not active (status: {participantReg.Status}).");
            }

            var resolved = await ResolveAsync(participantId, conversationId, cancellationToken);
            participants.Add(resolved);
        }

        // Build the composite team agent via ITeamOrchestrator
        // For MagneticOne mode, create an event queue for ledger updates
        var eventQueue = teamConfig.Mode == Domain.Enums.TeamMode.MagneticOne
            ? new System.Collections.Concurrent.ConcurrentQueue<Application.Chat.DTOs.TeamChatEventDto>()
            : null;

        // Build the manager IChatClient for LLM-based modes (Selector / MagneticOne)
        IChatClient? managerClient = null;
        if (teamConfig.Mode == Domain.Enums.TeamMode.Selector
            && teamConfig.SelectorProviderId.HasValue
            && !string.IsNullOrWhiteSpace(teamConfig.SelectorModelId))
        {
            managerClient = await BuildChatClientAsync(
                teamConfig.SelectorProviderId.Value, teamConfig.SelectorModelId, agent.Name);
        }
        else if (teamConfig.Mode == Domain.Enums.TeamMode.MagneticOne
            && teamConfig.OrchestratorProviderId.HasValue
            && !string.IsNullOrWhiteSpace(teamConfig.OrchestratorModelId))
        {
            managerClient = await BuildChatClientAsync(
                teamConfig.OrchestratorProviderId.Value, teamConfig.OrchestratorModelId, agent.Name);
        }

        var teamAgent = _teamOrchestrator.BuildTeamAgent(agent, participants, cancellationToken, eventQueue, managerClient);

        return new ResolvedAgent(teamAgent, null, IsTeam: true, TeamEventQueue: eventQueue);
    }

    /// <summary>
    /// Build an IChatClient from a LLM provider ID and model ID.
    /// Reusable helper extracted from ResolveChatClientAgent for team manager LLMs.
    /// </summary>
    private async Task<IChatClient?> BuildChatClientAsync(Guid providerId, string modelId, string contextName)
    {
        var provider = await _providerRepo.GetByIdAsync(providerId);
        if (provider is null)
        {
            _logger.LogWarning(
                "LlmProvider '{ProviderId}' not found for team manager '{Context}' — manager LLM unavailable.",
                providerId, contextName);
            return null;
        }

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) });

        IChatClient chatClient = openAiClient
            .GetChatClient(modelId)
            .AsIChatClient();

        // Apply the same normalization used for agent chat clients
        chatClient = new ToolCallNormalizingChatClient(chatClient);

        _logger.LogInformation(
            "Built manager IChatClient for '{Context}' using provider '{ProviderName}' model '{ModelId}'",
            contextName, provider.Name, modelId);

        return chatClient;
    }
}
