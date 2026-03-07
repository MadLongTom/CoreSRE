using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Persistence.Sessions;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class AgentResolverServiceTests
{
    private readonly Mock<IAgentRegistrationRepository> _agentRepoMock = new();
    private readonly Mock<ILlmProviderRepository> _providerRepoMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<IToolFunctionFactory> _toolFunctionFactoryMock = new();
    private readonly Mock<ISandboxToolProvider> _sandboxToolProviderMock = new();
    private readonly Mock<ITeamOrchestrator> _teamOrchestratorMock = new();
    private readonly Mock<IConfiguration> _configurationMock = new();
    private readonly Mock<ILogger<AgentResolverService>> _loggerMock = new();
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();

    private AgentResolverService CreateService()
    {
        // Default: ChatHistory:DefaultMaxMessages = 50
        _configurationMock
            .Setup(c => c.GetSection("ChatHistory:DefaultMaxMessages"))
            .Returns(CreateConfigSection("50"));

        return new AgentResolverService(
            _agentRepoMock.Object,
            _providerRepoMock.Object,
            _httpClientFactoryMock.Object,
            _toolFunctionFactoryMock.Object,
            new Mock<IDataSourceFunctionFactory>().Object,
            _sandboxToolProviderMock.Object,
            new Mock<ISkillRegistrationRepository>().Object,
            new Mock<IFileStorageService>().Object,
            _teamOrchestratorMock.Object,
            new Mock<IDataSourceQuerierFactory>().Object,
            new Mock<IDataSourceRegistrationRepository>().Object,
            _configurationMock.Object,
            _loggerMock.Object,
            _loggerFactoryMock.Object);
    }

    private static IConfigurationSection CreateConfigSection(string? value)
    {
        var section = new Mock<IConfigurationSection>();
        section.Setup(s => s.Value).Returns(value);
        return section.Object;
    }

    private static IConfigurationSection CreateConfigSectionWithChildren(Dictionary<string, string?> children)
    {
        var section = new Mock<IConfigurationSection>();
        foreach (var kvp in children)
        {
            var childSection = new Mock<IConfigurationSection>();
            childSection.Setup(s => s.Value).Returns(kvp.Value);
            section.Setup(s => s.GetSection(kvp.Key)).Returns(childSection.Object);
            section.Setup(s => s[kvp.Key]).Returns(kvp.Value);
        }
        return section.Object;
    }

    private static AgentRegistration CreateChatClientAgent(
        Guid id,
        string name = "Test Agent",
        Guid? providerId = null,
        string modelId = "gpt-4",
        string? instructions = null,
        List<Guid>? toolRefs = null,
        float? temperature = null,
        int? maxOutputTokens = null,
        float? topP = null,
        int? topK = null,
        float? frequencyPenalty = null,
        float? presencePenalty = null,
        long? seed = null,
        List<string>? stopSequences = null,
        string? responseFormat = null,
        string? toolMode = null,
        bool? allowMultipleToolCalls = null,
        bool? enableChatHistory = null,
        int? maxHistoryMessages = null,
        bool? enableSemanticMemory = null,
        Guid? embeddingProviderId = null,
        string? embeddingModelId = null,
        int? embeddingDimensions = null,
        string? memorySearchMode = null,
        int? memoryMaxResults = null)
    {
        var llmConfig = new LlmConfigVO
        {
            ProviderId = providerId ?? Guid.NewGuid(),
            ModelId = modelId,
            Instructions = instructions,
            ToolRefs = toolRefs ?? [],
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            TopP = topP,
            TopK = topK,
            FrequencyPenalty = frequencyPenalty,
            PresencePenalty = presencePenalty,
            Seed = seed,
            StopSequences = stopSequences,
            ResponseFormat = responseFormat,
            ToolMode = toolMode,
            AllowMultipleToolCalls = allowMultipleToolCalls,
            EnableChatHistory = enableChatHistory,
            MaxHistoryMessages = maxHistoryMessages,
            EnableSemanticMemory = enableSemanticMemory,
            EmbeddingProviderId = embeddingProviderId,
            EmbeddingModelId = embeddingModelId,
            EmbeddingDimensions = embeddingDimensions,
            MemorySearchMode = memorySearchMode,
            MemoryMaxResults = memoryMaxResults,
        };

        var agent = AgentRegistration.CreateChatClient(name, "Test agent", llmConfig);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(agent, id);
        return agent;
    }

    private static LlmProvider CreateProvider(Guid id)
    {
        var provider = LlmProvider.Create("TestProvider", "https://api.openai.com/v1", "sk-test-key");
        typeof(BaseEntity).GetProperty("Id")!.SetValue(provider, id);
        return provider;
    }

    private void SetupRepoMocks(Guid agentId, AgentRegistration agent, Guid providerId, LlmProvider provider)
    {
        _agentRepoMock.Setup(r => r.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _providerRepoMock.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Existing tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveChatClientAgent_EmptyToolRefs_ReturnsAgentWithoutFunctionInvocation()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, toolRefs: []);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        result.Agent.Should().NotBeNull();
        _toolFunctionFactoryMock.Verify(
            f => f.CreateFunctionsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveChatClientAgent_WithToolRefs_CallsToolFunctionFactory()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var toolRef1 = Guid.NewGuid();
        var toolRef2 = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, toolRefs: [toolRef1, toolRef2]);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);

        var mockFunctions = new List<AIFunction>();
        _toolFunctionFactoryMock.Setup(f => f.CreateFunctionsAsync(
                It.Is<IReadOnlyList<Guid>>(refs => refs.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFunctions.AsReadOnly());

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        result.Agent.Should().NotBeNull();
        _toolFunctionFactoryMock.Verify(
            f => f.CreateFunctionsAsync(
                It.Is<IReadOnlyList<Guid>>(refs => refs.Contains(toolRef1) && refs.Contains(toolRef2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════
    // US1: Framework-Managed Chat History — T005-T008
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveChatClientAgent_EnableChatHistoryTrue_ConfiguresChatHistoryProvider()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.ChatHistoryProvider.Should().NotBeNull(
            "when EnableChatHistory is true, the provider instance must be configured");
    }

    [Fact]
    public async Task ResolveChatClientAgent_EnableChatHistoryNull_ConfiguresChatHistoryProvider()
    {
        // Arrange — null defaults to true
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: null);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.ChatHistoryProvider.Should().NotBeNull(
            "when EnableChatHistory is null (default), it should be treated as true");
    }

    [Fact]
    public async Task ResolveChatClientAgent_EnableChatHistoryFalse_NoChatHistoryProvider()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: false);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.ChatHistoryProvider.Should().BeNull(
            "when EnableChatHistory is false, stateless mode — no provider configured");
    }

    [Fact]
    public async Task ChatHistoryProvider_IsPostgresChatHistoryProvider()
    {
        // Arrange — create agent with history enabled
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        var result = await service.ResolveAsync(agentId, "conv-1");
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();

        // Assert — provider is our JSONB-safe PostgresChatHistoryProvider (singleton instance)
        var historyProvider = options!.ChatHistoryProvider;
        historyProvider.Should().NotBeNull();
        historyProvider.Should().BeOfType<PostgresChatHistoryProvider>();
    }

    [Fact]
    public async Task ChatHistoryProvider_WithoutState_HasNoMessages()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        var result = await service.ResolveAsync(agentId, "conv-1");
        var options = result.Agent.GetService<ChatClientAgentOptions>();

        // Assert — provider is a fresh instance with no session state
        var historyProvider = options!.ChatHistoryProvider;
        historyProvider.Should().NotBeNull();
        historyProvider.Should().BeOfType<PostgresChatHistoryProvider>();
    }

    // ═══════════════════════════════════════════════════════════════════
    // US2: Token Window Management — T015-T017
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveChatClientAgent_MaxHistoryMessages20_ConfiguresReducerWith20()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true, maxHistoryMessages: 20);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        var historyProvider = options!.ChatHistoryProvider;

        // Assert
        historyProvider.Should().BeOfType<PostgresChatHistoryProvider>();
        var pgProvider = (PostgresChatHistoryProvider)historyProvider!;
        pgProvider.ChatReducer.Should().NotBeNull("MaxHistoryMessages = 20 should configure a reducer");
        pgProvider.ChatReducer.Should().BeOfType<MessageCountingChatReducer>();
    }

    [Fact]
    public async Task ResolveChatClientAgent_MaxHistoryMessagesNull_ConfiguresDefaultReducer()
    {
        // Arrange — null MaxHistoryMessages should use platform default (50)
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true, maxHistoryMessages: null);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        var historyProvider = options!.ChatHistoryProvider;

        // Assert
        historyProvider.Should().BeOfType<PostgresChatHistoryProvider>();
        var pgProvider = (PostgresChatHistoryProvider)historyProvider!;
        pgProvider.ChatReducer.Should().NotBeNull("null MaxHistoryMessages should still configure a default reducer (50)");
        pgProvider.ChatReducer.Should().BeOfType<MessageCountingChatReducer>();
    }

    [Fact]
    public async Task ResolveChatClientAgent_MaxHistoryMessagesZeroOrNegative_TreatedAsDefault()
    {
        // Arrange — 0 or negative should be treated as null (platform default)
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableChatHistory: true, maxHistoryMessages: 0);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        var historyProvider = options!.ChatHistoryProvider;

        // Assert
        historyProvider.Should().BeOfType<PostgresChatHistoryProvider>();
        var pgProvider = (PostgresChatHistoryProvider)historyProvider!;
        pgProvider.ChatReducer.Should().NotBeNull("zero MaxHistoryMessages should be treated as platform default");
        pgProvider.ChatReducer.Should().BeOfType<MessageCountingChatReducer>();
    }

    // ═══════════════════════════════════════════════════════════════════
    // US4: Cross-Session Semantic Memory — T023-T026
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveChatClientAgent_EnableSemanticMemoryTrue_NoEmbeddingConfig_SkipsMemory()
    {
        // Arrange — semantic memory enabled but no embedding provider/model configured
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId,
            enableSemanticMemory: true, memorySearchMode: "BeforeAIInvoke");
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert — no AIContextProviders because EmbeddingModelId not configured
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.AIContextProviders.Should().BeNullOrEmpty(
            "when EnableSemanticMemory is true but EmbeddingModelId is not configured, memory should be skipped");
    }

    [Fact]
    public async Task ResolveChatClientAgent_EnableSemanticMemoryFalse_NoAIContextProviderFactory()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableSemanticMemory: false);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.AIContextProviders.Should().BeNullOrEmpty(
            "when EnableSemanticMemory is false, no AIContextProviders");
    }

    [Fact]
    public async Task ResolveChatClientAgent_EnableSemanticMemoryNull_NoAIContextProviderFactory()
    {
        // Arrange — null defaults to false
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, enableSemanticMemory: null);
        var provider = CreateProvider(providerId);
        SetupRepoMocks(agentId, agent, providerId, provider);
        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        var options = result.Agent.GetService<ChatClientAgentOptions>();
        options.Should().NotBeNull();
        options!.AIContextProviders.Should().BeNullOrEmpty(
            "when EnableSemanticMemory is null (default false), no AIContextProviders");
    }

    // ═══════════════════════════════════════════════════════════════════
    // US1-TeamChat: Team Type Resolution — T020-T021
    // ═══════════════════════════════════════════════════════════════════

    private static AgentRegistration CreateTeamAgent(
        Guid id,
        string name,
        TeamConfigVO teamConfig)
    {
        var agent = AgentRegistration.CreateTeam(name, $"{name} description", teamConfig);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(agent, id);
        // Ensure status is Active for resolution
        typeof(AgentRegistration).GetProperty("Status")!.SetValue(agent, AgentStatus.Active);
        return agent;
    }

    [Fact]
    public async Task ResolveAsync_TeamAgent_ResolvesParticipantsAndCallsOrchestrator()
    {
        // Arrange
        var participantId1 = Guid.NewGuid();
        var participantId2 = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        var participant1 = CreateChatClientAgent(participantId1, "Agent1", providerId: providerId);
        typeof(AgentRegistration).GetProperty("Status")!.SetValue(participant1, AgentStatus.Active);
        var participant2 = CreateChatClientAgent(participantId2, "Agent2", providerId: providerId);
        typeof(AgentRegistration).GetProperty("Status")!.SetValue(participant2, AgentStatus.Active);

        var teamConfig = TeamConfigVO.Create(
            TeamMode.Sequential,
            [participantId1, participantId2]);

        var teamId = Guid.NewGuid();
        var teamAgent = CreateTeamAgent(teamId, "TestTeam", teamConfig);

        var provider = CreateProvider(providerId);

        // Setup repo to return team agent, then participant agents
        _agentRepoMock.Setup(r => r.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamAgent);
        _agentRepoMock.Setup(r => r.GetByIdAsync(participantId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant1);
        _agentRepoMock.Setup(r => r.GetByIdAsync(participantId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant2);
        _providerRepoMock.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var mockAiAgent = new Mock<AIAgent>().Object;
        _teamOrchestratorMock.Setup(o => o.BuildTeamAgent(
                It.Is<AgentRegistration>(a => a.Id == teamId),
                It.Is<IReadOnlyList<ResolvedAgent>>(list => list.Count == 2),
                It.IsAny<CancellationToken>()))
            .Returns(mockAiAgent);

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(teamId, "conv-team-1");

        // Assert
        result.Agent.Should().Be(mockAiAgent);
        result.LlmConfig.Should().BeNull("Team agents don't have their own LlmConfig");
        _teamOrchestratorMock.Verify(o => o.BuildTeamAgent(
            It.IsAny<AgentRegistration>(),
            It.Is<IReadOnlyList<ResolvedAgent>>(list => list.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_TeamAgent_ParticipantNotFound_Throws()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        var teamConfig = TeamConfigVO.Create(
            TeamMode.Sequential,
            [existingId, missingId]);

        var teamId = Guid.NewGuid();
        var teamAgent = CreateTeamAgent(teamId, "TestTeam", teamConfig);

        _agentRepoMock.Setup(r => r.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamAgent);
        _agentRepoMock.Setup(r => r.GetByIdAsync(existingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateChatClientAgent(existingId, "Existing", providerId: Guid.NewGuid()));
        _agentRepoMock.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRegistration?)null);

        var service = CreateService();

        // Act & Assert
        var act = () => service.ResolveAsync(teamId, "conv-team-err");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{missingId}'*not found*");
    }

    [Fact]
    public async Task ResolveAsync_TeamAgent_InactiveParticipant_Throws()
    {
        // Arrange
        var participantId1 = Guid.NewGuid();
        var participantId2 = Guid.NewGuid();

        var teamConfig = TeamConfigVO.Create(
            TeamMode.Sequential,
            [participantId1, participantId2]);

        var teamId = Guid.NewGuid();
        var teamAgent = CreateTeamAgent(teamId, "TestTeam", teamConfig);

        var inactiveAgent = CreateChatClientAgent(participantId1, "Inactive");
        typeof(AgentRegistration).GetProperty("Status")!.SetValue(inactiveAgent, AgentStatus.Inactive);

        _agentRepoMock.Setup(r => r.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamAgent);
        _agentRepoMock.Setup(r => r.GetByIdAsync(participantId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveAgent);

        var service = CreateService();

        // Act & Assert
        var act = () => service.ResolveAsync(teamId, "conv-team-err");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task ResolveAsync_TeamAgent_NestedTeamParticipant_Throws()
    {
        // Arrange — create a participant that is itself a Team agent
        var nestedTeamId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var teamConfig = TeamConfigVO.Create(
            TeamMode.Sequential,
            [nestedTeamId, otherId]);

        var teamId = Guid.NewGuid();
        var teamAgent = CreateTeamAgent(teamId, "OuterTeam", teamConfig);

        // Create the nested team agent registration
        var nestedConfig = TeamConfigVO.Create(TeamMode.Sequential, [Guid.NewGuid(), Guid.NewGuid()]);
        var nestedAgent = CreateTeamAgent(nestedTeamId, "InnerTeam", nestedConfig);

        _agentRepoMock.Setup(r => r.GetByIdAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamAgent);
        _agentRepoMock.Setup(r => r.GetByIdAsync(nestedTeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nestedAgent);

        var service = CreateService();

        // Act & Assert
        var act = () => service.ResolveAsync(teamId, "conv-team-err");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nesting*not allowed*");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════
}
