using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class AgentResolverServiceTests
{
    private readonly Mock<IAgentRegistrationRepository> _agentRepoMock = new();
    private readonly Mock<ILlmProviderRepository> _providerRepoMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<IToolFunctionFactory> _toolFunctionFactoryMock = new();

    private AgentResolverService CreateService()
    {
        return new AgentResolverService(
            _agentRepoMock.Object,
            _providerRepoMock.Object,
            _httpClientFactoryMock.Object,
            _toolFunctionFactoryMock.Object);
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
        bool? allowMultipleToolCalls = null)
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

    [Fact]
    public async Task ResolveChatClientAgent_EmptyToolRefs_ReturnsAgentWithoutFunctionInvocation()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var agent = CreateChatClientAgent(agentId, providerId: providerId, toolRefs: []);
        var provider = CreateProvider(providerId);

        _agentRepoMock.Setup(r => r.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _providerRepoMock.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        result.Should().NotBeNull();
        // ToolFunctionFactory should NOT be called when toolRefs is empty
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

        _agentRepoMock.Setup(r => r.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _providerRepoMock.Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var mockFunctions = new List<AIFunction>();
        _toolFunctionFactoryMock.Setup(f => f.CreateFunctionsAsync(
                It.Is<IReadOnlyList<Guid>>(refs => refs.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFunctions.AsReadOnly());

        var service = CreateService();

        // Act
        var result = await service.ResolveAsync(agentId, "conv-1");

        // Assert
        result.Should().NotBeNull();
        _toolFunctionFactoryMock.Verify(
            f => f.CreateFunctionsAsync(
                It.Is<IReadOnlyList<Guid>>(refs => refs.Contains(toolRef1) && refs.Contains(toolRef2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
