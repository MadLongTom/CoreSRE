using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// Tests for US3: Mock Agent Execution Mode — MockChatClient behavior and AgentResolver fallback.
/// </summary>
public class MockAgentTests
{
    // ========== T022: MockChatClient_ReturnsResponseWithAgentNameAndInput ==========

    [Fact]
    public async Task MockChatClient_ReturnsResponseWithAgentNameAndInput()
    {
        // Arrange
        var client = new MockChatClient("test-agent");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello, please summarize this document.")
        };

        // Act
        var response = await client.GetResponseAsync(messages);

        // Assert
        var text = response.Messages.Last(m => m.Role == ChatRole.Assistant).Text;
        text.Should().NotBeNull();

        using var doc = JsonDocument.Parse(text!);
        var root = doc.RootElement;
        root.GetProperty("mock").GetBoolean().Should().BeTrue();
        root.GetProperty("agentName").GetString().Should().Be("test-agent");
        root.GetProperty("inputSummary").GetString().Should().Contain("Hello");
        root.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // ========== T023: MockChatClient_TruncatesLongInput ==========

    [Fact]
    public async Task MockChatClient_TruncatesLongInput()
    {
        // Arrange
        var client = new MockChatClient("test-agent");
        var longInput = new string('A', 500);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, longInput)
        };

        // Act
        var response = await client.GetResponseAsync(messages);

        // Assert
        var text = response.Messages.Last(m => m.Role == ChatRole.Assistant).Text;
        using var doc = JsonDocument.Parse(text!);
        var summary = doc.RootElement.GetProperty("inputSummary").GetString();
        summary.Should().HaveLength(200);
    }

    // ========== T024: AgentResolver_MockMode_ReturnsMockAgent ==========

    [Fact]
    public async Task AgentResolver_MockMode_ReturnsMockAgent()
    {
        // Arrange
        var llmConfig = new LlmConfigVO { ProviderId = Guid.NewGuid(), ModelId = "gpt-4" };
        var agent = AgentRegistration.CreateChatClient("test-agent", null, llmConfig);
        var agentId = agent.Id;

        var agentRepoMock = new Mock<IAgentRegistrationRepository>();
        agentRepoMock
            .Setup(r => r.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var providerRepoMock = new Mock<ILlmProviderRepository>();

        var configData = new Dictionary<string, string?>
        {
            { "Workflow:MockAgentMode", "true" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var resolver = CreateAgentResolverService(
            agentRepoMock.Object, providerRepoMock.Object, configuration);

        // Act
        var resolved = await resolver.ResolveAsync(agentId, "conv-1");

        // Assert — the resolved agent should use MockChatClient
        var chatClient = resolved.Agent.GetService<IChatClient>();
        chatClient.Should().NotBeNull();

        // Verify it returns mock response
        var response = await chatClient!.GetResponseAsync(
            new List<ChatMessage> { new(ChatRole.User, "test input") });
        var text = response.Messages.Last(m => m.Role == ChatRole.Assistant).Text;
        text.Should().Contain("mock");
        text.Should().Contain("true");
    }

    // ========== T025: AgentResolver_NoLlmProvider_FallsBackToMock ==========

    [Fact]
    public async Task AgentResolver_NoLlmProvider_FallsBackToMock()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        var llmConfig = new LlmConfigVO { ProviderId = providerId, ModelId = "gpt-4" };
        var agent = AgentRegistration.CreateChatClient("no-provider-agent", null, llmConfig);
        var agentId = agent.Id;

        var agentRepoMock = new Mock<IAgentRegistrationRepository>();
        agentRepoMock
            .Setup(r => r.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        // Provider repo returns null — provider not found
        var providerRepoMock = new Mock<ILlmProviderRepository>();
        providerRepoMock
            .Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.LlmProvider?)null);

        var configData = new Dictionary<string, string?>
        {
            { "Workflow:MockAgentMode", "false" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var resolver = CreateAgentResolverService(
            agentRepoMock.Object, providerRepoMock.Object, configuration);

        // Act
        var resolved = await resolver.ResolveAsync(agentId, "conv-1");

        // Assert — falls back to MockChatClient instead of throwing
        var chatClient = resolved.Agent.GetService<IChatClient>();
        chatClient.Should().NotBeNull();

        var response = await chatClient!.GetResponseAsync(
            new List<ChatMessage> { new(ChatRole.User, "test") });
        var text = response.Messages.Last(m => m.Role == ChatRole.Assistant).Text;
        text.Should().Contain("mock");
    }

    // ========== Helpers ==========

    private static AgentResolverService CreateAgentResolverService(
        IAgentRegistrationRepository agentRepo,
        ILlmProviderRepository providerRepo,
        IConfiguration configuration)
    {
        return new AgentResolverService(
            agentRepo,
            providerRepo,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IToolFunctionFactory>().Object,
            new Mock<IDataSourceFunctionFactory>().Object,
            new Mock<ISandboxToolProvider>().Object,
            new Mock<ISkillRegistrationRepository>().Object,
            new Mock<IFileStorageService>().Object,
            configuration,
            new Mock<ILogger<AgentResolverService>>().Object,
            new Mock<ILoggerFactory>().Object);
    }
}
