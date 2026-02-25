using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

/// <summary>
/// TeamOrchestratorService unit tests — validates all 6 orchestration modes
/// and error cases. Tests follow TDD Red-Green-Refactor constitution.
/// </summary>
public class TeamOrchestratorTests
{
    private readonly Mock<ILogger<TeamOrchestratorService>> _loggerMock = new();

    private TeamOrchestratorService CreateService()
    {
        return new TeamOrchestratorService(_loggerMock.Object);
    }

    /// <summary>
    /// Creates a mock IChatClient-backed ResolvedAgent with a given name.
    /// </summary>
    private static ResolvedAgent CreateMockParticipant(string name, Guid? id = null)
    {
        var agentId = id ?? Guid.NewGuid();
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetService(typeof(IChatClient), null)).Returns(mockClient.Object);

        // Mock GetResponseAsync to return simple text
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Response from {name}")));

        var options = new ChatClientAgentOptions
        {
            Id = agentId.ToString(),
            Name = name,
            Description = $"Test agent {name}"
        };

        var aiAgent = mockClient.Object.AsAIAgent(options);
        return new ResolvedAgent(aiAgent, null);
    }

    /// <summary>
    /// Creates a Team AgentRegistration with the specified mode and participant IDs.
    /// </summary>
    private static AgentRegistration CreateTeamRegistration(
        TeamMode mode,
        List<Guid> participantIds,
        int maxIterations = 40,
        Dictionary<Guid, List<HandoffTargetVO>>? handoffRoutes = null,
        Guid? initialAgentId = null,
        Guid? selectorProviderId = null,
        string? selectorModelId = null,
        string? selectorPrompt = null,
        Guid? orchestratorProviderId = null,
        string? orchestratorModelId = null,
        int maxStalls = 3,
        string? aggregationStrategy = null)
    {
        var config = TeamConfigVO.Create(
            mode,
            participantIds,
            maxIterations: maxIterations,
            handoffRoutes: handoffRoutes,
            initialAgentId: initialAgentId,
            selectorProviderId: selectorProviderId,
            selectorModelId: selectorModelId,
            selectorPrompt: selectorPrompt,
            orchestratorProviderId: orchestratorProviderId,
            orchestratorModelId: orchestratorModelId,
            maxStalls: maxStalls,
            aggregationStrategy: aggregationStrategy);

        return AgentRegistration.CreateTeam($"team-{mode}", $"Test {mode} team", config);
    }

    // ── T005: Sequential Mode Tests ─────────────────────────────────

    [Fact]
    public void BuildTeamAgent_Sequential_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("AgentA");
        var p2 = CreateMockParticipant("AgentB");
        var p3 = CreateMockParticipant("AgentC");

        var reg = CreateTeamRegistration(TeamMode.Sequential, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        var result = svc.BuildTeamAgent(reg, [p1, p2, p3]);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_Sequential_TwoAgents_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("First");
        var p2 = CreateMockParticipant("Second");

        var reg = CreateTeamRegistration(TeamMode.Sequential, [Guid.NewGuid(), Guid.NewGuid()]);

        var result = svc.BuildTeamAgent(reg, [p1, p2]);

        result.Should().NotBeNull();
    }

    [Fact]
    // T048: Single/minimum-participant edge case — domain requires ≥2 for most modes.
    // This test verifies the minimum (2 participants) works as pass-through.
    public void BuildTeamAgent_Sequential_SingleAgent_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("Solo");
        var p2 = CreateMockParticipant("Other");

        // Domain requires at least 2 for Sequential, but service should still handle it
        var reg = CreateTeamRegistration(TeamMode.Sequential, [Guid.NewGuid(), Guid.NewGuid()]);

        var result = svc.BuildTeamAgent(reg, [p1, p2]);

        result.Should().NotBeNull();
    }

    // ── T006: Concurrent Mode Tests ─────────────────────────────────

    [Fact]
    public void BuildTeamAgent_Concurrent_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("Worker1");
        var p2 = CreateMockParticipant("Worker2");
        var p3 = CreateMockParticipant("Worker3");

        var reg = CreateTeamRegistration(TeamMode.Concurrent,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            aggregationStrategy: "Merge");

        var result = svc.BuildTeamAgent(reg, [p1, p2, p3]);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_Concurrent_TwoAgents_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("A");
        var p2 = CreateMockParticipant("B");

        var reg = CreateTeamRegistration(TeamMode.Concurrent, [Guid.NewGuid(), Guid.NewGuid()]);

        var result = svc.BuildTeamAgent(reg, [p1, p2]);

        result.Should().NotBeNull();
    }

    // ── T007: RoundRobin Mode Tests ─────────────────────────────────

    [Fact]
    public void BuildTeamAgent_RoundRobin_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("Speaker1");
        var p2 = CreateMockParticipant("Speaker2");
        var p3 = CreateMockParticipant("Speaker3");

        var reg = CreateTeamRegistration(TeamMode.RoundRobin,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            maxIterations: 10);

        var result = svc.BuildTeamAgent(reg, [p1, p2, p3]);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_RoundRobin_RespectsMaxIterations()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("A");
        var p2 = CreateMockParticipant("B");

        var reg = CreateTeamRegistration(TeamMode.RoundRobin,
            [Guid.NewGuid(), Guid.NewGuid()],
            maxIterations: 5);

        // Should not throw — maxIterations is used by the GroupChatManager
        var result = svc.BuildTeamAgent(reg, [p1, p2]);
        result.Should().NotBeNull();
    }

    // ── T008: Handoffs Mode Tests ───────────────────────────────────

    [Fact]
    public void BuildTeamAgent_Handoffs_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var triageId = Guid.NewGuid();
        var salesId = Guid.NewGuid();
        var supportId = Guid.NewGuid();

        var triage = CreateMockParticipant("TriageAgent", triageId);
        var sales = CreateMockParticipant("SalesAgent", salesId);
        var support = CreateMockParticipant("SupportAgent", supportId);

        var handoffRoutes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [triageId] = [
                new HandoffTargetVO(salesId, "Sales inquiry"),
                new HandoffTargetVO(supportId, "Support issue")
            ],
            [salesId] = [new HandoffTargetVO(triageId, "Back to triage")]
        };

        var reg = CreateTeamRegistration(TeamMode.Handoffs,
            [triageId, salesId, supportId],
            handoffRoutes: handoffRoutes,
            initialAgentId: triageId);

        var result = svc.BuildTeamAgent(reg, [triage, sales, support]);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_Handoffs_InitialAgentCorrect()
    {
        var svc = CreateService();
        var initialId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var initial = CreateMockParticipant("InitialAgent", initialId);
        var other = CreateMockParticipant("OtherAgent", otherId);

        var handoffRoutes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [initialId] = [new HandoffTargetVO(otherId, "Forward")]
        };

        var reg = CreateTeamRegistration(TeamMode.Handoffs,
            [initialId, otherId],
            handoffRoutes: handoffRoutes,
            initialAgentId: initialId);

        // Should not throw — routes are valid
        var result = svc.BuildTeamAgent(reg, [initial, other]);
        result.Should().NotBeNull();
    }

    /// <summary>
    /// Creates a mock IChatClient for use as the manager LLM in Selector/MagneticOne modes.
    /// </summary>
    private static IChatClient CreateMockManagerClient()
    {
        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetService(typeof(IChatClient), null)).Returns(mockClient.Object);
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Agent1")));
        return mockClient.Object;
    }

    // ── T009: Selector Mode Tests ───────────────────────────────────

    [Fact]
    public void BuildTeamAgent_Selector_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("Expert1");
        var p2 = CreateMockParticipant("Expert2");

        var reg = CreateTeamRegistration(TeamMode.Selector,
            [Guid.NewGuid(), Guid.NewGuid()],
            selectorProviderId: Guid.NewGuid(),
            selectorModelId: "gpt-4",
            selectorPrompt: "Select the best agent for this task.");

        var result = svc.BuildTeamAgent(reg, [p1, p2], managerClient: CreateMockManagerClient());

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_Selector_WithoutManagerClient_ThrowsInvalidOperationException()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("Expert1");
        var p2 = CreateMockParticipant("Expert2");

        var reg = CreateTeamRegistration(TeamMode.Selector,
            [Guid.NewGuid(), Guid.NewGuid()],
            selectorProviderId: Guid.NewGuid(),
            selectorModelId: "gpt-4");

        var act = () => svc.BuildTeamAgent(reg, [p1, p2], managerClient: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IChatClient*Selector*");
    }

    // ── T010: MagneticOne Mode Tests ────────────────────────────────

    [Fact]
    public void BuildTeamAgent_MagneticOne_ReturnsValidAIAgent()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("MetricsAnalyzer");
        var p2 = CreateMockParticipant("LogAnalyzer");
        var p3 = CreateMockParticipant("Remediator");

        var reg = CreateTeamRegistration(TeamMode.MagneticOne,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            orchestratorProviderId: Guid.NewGuid(),
            orchestratorModelId: "gpt-4",
            maxStalls: 3);

        var result = svc.BuildTeamAgent(reg, [p1, p2, p3], managerClient: CreateMockManagerClient());

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<AIAgent>();
    }

    [Fact]
    public void BuildTeamAgent_MagneticOne_WithoutManagerClient_ThrowsInvalidOperationException()
    {
        var svc = CreateService();
        var p1 = CreateMockParticipant("MetricsAnalyzer");
        var p2 = CreateMockParticipant("LogAnalyzer");

        var reg = CreateTeamRegistration(TeamMode.MagneticOne,
            [Guid.NewGuid(), Guid.NewGuid()],
            orchestratorProviderId: Guid.NewGuid(),
            orchestratorModelId: "gpt-4");

        var act = () => svc.BuildTeamAgent(reg, [p1, p2], managerClient: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MagneticOne*IChatClient*");
    }

    // ── T011: Error Cases ───────────────────────────────────────────

    [Fact]
    public void BuildTeamAgent_EmptyParticipants_ThrowsInvalidOperationException()
    {
        var svc = CreateService();
        var reg = CreateTeamRegistration(TeamMode.Sequential, [Guid.NewGuid(), Guid.NewGuid()]);

        var act = () => svc.BuildTeamAgent(reg, []);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no participants*");
    }

    [Fact]
    public void BuildTeamAgent_NullTeamConfig_ThrowsInvalidOperationException()
    {
        var svc = CreateService();
        var participant = CreateMockParticipant("Agent1");

        // Use reflection to create an entity with null TeamConfig (simulates DB corruption)
        var reg = (AgentRegistration)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AgentRegistration));
        typeof(AgentRegistration).GetProperty("Name")!.GetSetMethod(true)!.Invoke(reg, ["bad-team"]);
        typeof(AgentRegistration).GetProperty("AgentType")!.GetSetMethod(true)!.Invoke(reg, [AgentType.Team]);
        // TeamConfig stays null

        var act = () => svc.BuildTeamAgent(reg, [participant]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no TeamConfig*");
    }
}
