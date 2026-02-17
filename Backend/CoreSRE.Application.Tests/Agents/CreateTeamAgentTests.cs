using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Application.Tests.Agents;

/// <summary>
/// AgentRegistration.CreateTeam() 工厂方法单元测试。
/// 覆盖 happy path + 所有 6 种 TeamMode 验证场景。
/// </summary>
public class CreateTeamAgentTests
{
    private static readonly Guid Agent1 = Guid.NewGuid();
    private static readonly Guid Agent2 = Guid.NewGuid();
    private static readonly Guid Agent3 = Guid.NewGuid();
    private static readonly Guid ProviderId = Guid.NewGuid();

    // ── Happy Path Tests ────────────────────────────────────────────

    [Fact]
    public void CreateTeam_Sequential_HappyPath_SetsCorrectProperties()
    {
        var config = TeamConfigVO.Create(
            TeamMode.Sequential,
            [Agent1, Agent2, Agent3],
            maxIterations: 10);

        var agent = AgentRegistration.CreateTeam("seq-team", "Sequential team", config);

        agent.Name.Should().Be("seq-team");
        agent.Description.Should().Be("Sequential team");
        agent.AgentType.Should().Be(AgentType.Team);
        agent.Status.Should().Be(AgentStatus.Registered);
        agent.TeamConfig.Should().NotBeNull();
        agent.TeamConfig!.Mode.Should().Be(TeamMode.Sequential);
        agent.TeamConfig.ParticipantIds.Should().HaveCount(3);
        agent.TeamConfig.MaxIterations.Should().Be(10);
    }

    [Fact]
    public void CreateTeam_Concurrent_HappyPath_SetsCorrectProperties()
    {
        var config = TeamConfigVO.Create(
            TeamMode.Concurrent,
            [Agent1, Agent2],
            aggregationStrategy: "Merge");

        var agent = AgentRegistration.CreateTeam("conc-team", null, config);

        agent.AgentType.Should().Be(AgentType.Team);
        agent.TeamConfig!.Mode.Should().Be(TeamMode.Concurrent);
        agent.TeamConfig.AggregationStrategy.Should().Be("Merge");
    }

    [Fact]
    public void CreateTeam_RoundRobin_HappyPath_SetsCorrectProperties()
    {
        var config = TeamConfigVO.Create(
            TeamMode.RoundRobin,
            [Agent1, Agent2, Agent3]);

        var agent = AgentRegistration.CreateTeam("rr-team", "Round robin", config);

        agent.TeamConfig!.Mode.Should().Be(TeamMode.RoundRobin);
        agent.TeamConfig.ParticipantIds.Should().HaveCount(3);
        agent.TeamConfig.MaxIterations.Should().Be(40); // default
    }

    [Fact]
    public void CreateTeam_Handoffs_HappyPath_SetsCorrectProperties()
    {
        var routes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [Agent1] = [new HandoffTargetVO(Agent2, "escalate"), new HandoffTargetVO(Agent3, "translate")],
            [Agent2] = [new HandoffTargetVO(Agent1, "return")]
        };

        var config = TeamConfigVO.Create(
            TeamMode.Handoffs,
            [Agent1, Agent2, Agent3],
            handoffRoutes: routes,
            initialAgentId: Agent1);

        var agent = AgentRegistration.CreateTeam("handoff-team", "Handoffs team", config);

        agent.TeamConfig!.Mode.Should().Be(TeamMode.Handoffs);
        agent.TeamConfig.InitialAgentId.Should().Be(Agent1);
        agent.TeamConfig.HandoffRoutes.Should().HaveCount(2);
        agent.TeamConfig.HandoffRoutes![Agent1].Should().HaveCount(2);
    }

    [Fact]
    public void CreateTeam_Selector_HappyPath_SetsCorrectProperties()
    {
        var config = TeamConfigVO.Create(
            TeamMode.Selector,
            [Agent1, Agent2],
            selectorProviderId: ProviderId,
            selectorModelId: "gpt-4o",
            selectorPrompt: "Pick the best agent",
            allowRepeatedSpeaker: false);

        var agent = AgentRegistration.CreateTeam("selector-team", null, config);

        agent.TeamConfig!.Mode.Should().Be(TeamMode.Selector);
        agent.TeamConfig.SelectorProviderId.Should().Be(ProviderId);
        agent.TeamConfig.SelectorModelId.Should().Be("gpt-4o");
        agent.TeamConfig.SelectorPrompt.Should().Be("Pick the best agent");
        agent.TeamConfig.AllowRepeatedSpeaker.Should().BeFalse();
    }

    [Fact]
    public void CreateTeam_MagneticOne_HappyPath_SetsCorrectProperties()
    {
        var config = TeamConfigVO.Create(
            TeamMode.MagneticOne,
            [Agent1, Agent2],
            orchestratorProviderId: ProviderId,
            orchestratorModelId: "gpt-4o",
            maxStalls: 5,
            finalAnswerPrompt: "Summarize results");

        var agent = AgentRegistration.CreateTeam("m1-team", "MagneticOne", config);

        agent.TeamConfig!.Mode.Should().Be(TeamMode.MagneticOne);
        agent.TeamConfig.OrchestratorProviderId.Should().Be(ProviderId);
        agent.TeamConfig.OrchestratorModelId.Should().Be("gpt-4o");
        agent.TeamConfig.MaxStalls.Should().Be(5);
        agent.TeamConfig.FinalAnswerPrompt.Should().Be("Summarize results");
    }

    // ── Validation Tests: Name ──────────────────────────────────────

    [Fact]
    public void CreateTeam_NullName_ThrowsArgumentException()
    {
        var config = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2]);

        var act = () => AgentRegistration.CreateTeam(null!, null, config);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void CreateTeam_EmptyName_ThrowsArgumentException()
    {
        var config = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2]);

        var act = () => AgentRegistration.CreateTeam("", null, config);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void CreateTeam_LongName_ThrowsArgumentException()
    {
        var config = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2]);
        var longName = new string('x', 201);

        var act = () => AgentRegistration.CreateTeam(longName, null, config);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    // ── Validation Tests: TeamConfig ────────────────────────────────

    [Fact]
    public void CreateTeam_NullTeamConfig_ThrowsArgumentNullException()
    {
        var act = () => AgentRegistration.CreateTeam("test", null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("teamConfig");
    }

    // ── TeamConfigVO Validation Tests ───────────────────────────────

    [Fact]
    public void TeamConfigVO_EmptyParticipants_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.RoundRobin, []);

        act.Should().Throw<ArgumentException>().WithParameterName("participantIds");
    }

    [Fact]
    public void TeamConfigVO_ParticipantWithEmptyGuid_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Guid.Empty]);

        act.Should().Throw<ArgumentException>().WithParameterName("participantIds");
    }

    [Fact]
    public void TeamConfigVO_ZeroMaxIterations_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2], maxIterations: 0);

        act.Should().Throw<ArgumentException>().WithParameterName("maxIterations");
    }

    // ── Sequential Validation ───────────────────────────────────────

    [Fact]
    public void TeamConfigVO_Sequential_SingleParticipant_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.Sequential, [Agent1]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("participantIds")
            .WithMessage("*at least 2*");
    }

    // ── Concurrent Validation ───────────────────────────────────────

    [Fact]
    public void TeamConfigVO_Concurrent_SingleParticipant_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.Concurrent, [Agent1]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("participantIds")
            .WithMessage("*at least 2*");
    }

    // ── RoundRobin Validation ───────────────────────────────────────

    [Fact]
    public void TeamConfigVO_RoundRobin_SingleParticipant_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1]);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("participantIds")
            .WithMessage("*at least 2*");
    }

    // ── Handoffs Validation ─────────────────────────────────────────

    [Fact]
    public void TeamConfigVO_Handoffs_MissingInitialAgentId_ThrowsArgumentException()
    {
        var routes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [Agent1] = [new HandoffTargetVO(Agent2)]
        };

        var act = () => TeamConfigVO.Create(
            TeamMode.Handoffs, [Agent1, Agent2],
            handoffRoutes: routes, initialAgentId: null);

        act.Should().Throw<ArgumentException>().WithParameterName("initialAgentId");
    }

    [Fact]
    public void TeamConfigVO_Handoffs_InitialAgentNotInParticipants_ThrowsArgumentException()
    {
        var outsider = Guid.NewGuid();
        var routes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [Agent1] = [new HandoffTargetVO(Agent2)]
        };

        var act = () => TeamConfigVO.Create(
            TeamMode.Handoffs, [Agent1, Agent2],
            handoffRoutes: routes, initialAgentId: outsider);

        act.Should().Throw<ArgumentException>().WithParameterName("initialAgentId");
    }

    [Fact]
    public void TeamConfigVO_Handoffs_MissingRoutes_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.Handoffs, [Agent1, Agent2],
            initialAgentId: Agent1, handoffRoutes: null);

        act.Should().Throw<ArgumentException>().WithParameterName("handoffRoutes");
    }

    [Fact]
    public void TeamConfigVO_Handoffs_RouteSourceNotInParticipants_ThrowsArgumentException()
    {
        var outsider = Guid.NewGuid();
        var routes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [outsider] = [new HandoffTargetVO(Agent2)]
        };

        var act = () => TeamConfigVO.Create(
            TeamMode.Handoffs, [Agent1, Agent2],
            handoffRoutes: routes, initialAgentId: Agent1);

        act.Should().Throw<ArgumentException>().WithParameterName("handoffRoutes");
    }

    [Fact]
    public void TeamConfigVO_Handoffs_RouteTargetNotInParticipants_ThrowsArgumentException()
    {
        var outsider = Guid.NewGuid();
        var routes = new Dictionary<Guid, List<HandoffTargetVO>>
        {
            [Agent1] = [new HandoffTargetVO(outsider)]
        };

        var act = () => TeamConfigVO.Create(
            TeamMode.Handoffs, [Agent1, Agent2],
            handoffRoutes: routes, initialAgentId: Agent1);

        act.Should().Throw<ArgumentException>().WithParameterName("handoffRoutes");
    }

    // ── Selector Validation ─────────────────────────────────────────

    [Fact]
    public void TeamConfigVO_Selector_SingleParticipant_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.Selector, [Agent1],
            selectorProviderId: ProviderId, selectorModelId: "gpt-4o");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("participantIds")
            .WithMessage("*at least 2*");
    }

    [Fact]
    public void TeamConfigVO_Selector_MissingProviderId_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.Selector, [Agent1, Agent2],
            selectorProviderId: null, selectorModelId: "gpt-4o");

        act.Should().Throw<ArgumentException>().WithParameterName("selectorProviderId");
    }

    [Fact]
    public void TeamConfigVO_Selector_MissingModelId_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.Selector, [Agent1, Agent2],
            selectorProviderId: ProviderId, selectorModelId: null);

        act.Should().Throw<ArgumentException>().WithParameterName("selectorModelId");
    }

    // ── MagneticOne Validation ──────────────────────────────────────

    [Fact]
    public void TeamConfigVO_MagneticOne_MissingProviderId_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.MagneticOne, [Agent1],
            orchestratorProviderId: null, orchestratorModelId: "gpt-4o");

        act.Should().Throw<ArgumentException>().WithParameterName("orchestratorProviderId");
    }

    [Fact]
    public void TeamConfigVO_MagneticOne_MissingModelId_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.MagneticOne, [Agent1],
            orchestratorProviderId: ProviderId, orchestratorModelId: null);

        act.Should().Throw<ArgumentException>().WithParameterName("orchestratorModelId");
    }

    [Fact]
    public void TeamConfigVO_MagneticOne_ZeroMaxStalls_ThrowsArgumentException()
    {
        var act = () => TeamConfigVO.Create(
            TeamMode.MagneticOne, [Agent1],
            orchestratorProviderId: ProviderId, orchestratorModelId: "gpt-4o",
            maxStalls: 0);

        act.Should().Throw<ArgumentException>().WithParameterName("maxStalls");
    }

    // ── Update Tests ────────────────────────────────────────────────

    [Fact]
    public void Update_TeamAgent_WithValidTeamConfig_Succeeds()
    {
        var config = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2]);
        var agent = AgentRegistration.CreateTeam("team", "desc", config);

        var newConfig = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2, Agent3], maxIterations: 10);
        agent.Update("team-updated", "new desc", null, null, null, null, newConfig);

        agent.Name.Should().Be("team-updated");
        agent.Description.Should().Be("new desc");
        agent.TeamConfig!.ParticipantIds.Should().HaveCount(3);
        agent.TeamConfig.MaxIterations.Should().Be(10);
    }

    [Fact]
    public void Update_TeamAgent_WithNullTeamConfig_ThrowsArgumentNullException()
    {
        var config = TeamConfigVO.Create(TeamMode.RoundRobin, [Agent1, Agent2]);
        var agent = AgentRegistration.CreateTeam("team", null, config);

        var act = () => agent.Update("team", null, null, null, null, null, null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("teamConfig");
    }

    // ── HandoffTargetVO Tests ───────────────────────────────────────

    [Fact]
    public void HandoffTargetVO_EmptyTargetAgentId_ThrowsArgumentException()
    {
        var act = () => new HandoffTargetVO(Guid.Empty, "reason");

        act.Should().Throw<ArgumentException>().WithParameterName("targetAgentId");
    }

    [Fact]
    public void HandoffTargetVO_ValidTarget_SetsProperties()
    {
        var target = new HandoffTargetVO(Agent1, "escalate to senior");

        target.TargetAgentId.Should().Be(Agent1);
        target.Reason.Should().Be("escalate to senior");
    }

    [Fact]
    public void HandoffTargetVO_NullReason_Allowed()
    {
        var target = new HandoffTargetVO(Agent1);

        target.TargetAgentId.Should().Be(Agent1);
        target.Reason.Should().BeNull();
    }
}
