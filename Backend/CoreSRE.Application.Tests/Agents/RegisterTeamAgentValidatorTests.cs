using CoreSRE.Application.Agents.Commands.RegisterAgent;
using CoreSRE.Application.Agents.DTOs;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CoreSRE.Application.Tests.Agents;

/// <summary>
/// RegisterAgentCommandValidator 的 Team 类型验证规则测试。
/// </summary>
public class RegisterTeamAgentValidatorTests
{
    private readonly RegisterAgentCommandValidator _validator = new();

    // ── AgentType Validation ────────────────────────────────────────

    [Fact]
    public void Should_Pass_When_AgentType_Is_Team()
    {
        var command = CreateValidRoundRobinTeamCommand();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.AgentType);
    }

    // ── TeamConfig Required ─────────────────────────────────────────

    [Fact]
    public void Should_Fail_When_Team_And_TeamConfig_Is_Null()
    {
        var command = CreateValidRoundRobinTeamCommand() with { TeamConfig = null };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TeamConfig)
            .WithErrorMessage("TeamConfig is required for Team agents.");
    }

    // ── Mode Validation ─────────────────────────────────────────────

    [Fact]
    public void Should_Fail_When_TeamConfig_Mode_Is_Empty()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto { Mode = "", ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()] }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("TeamConfig.Mode");
    }

    [Fact]
    public void Should_Fail_When_TeamConfig_Mode_Is_Invalid()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto { Mode = "InvalidMode", ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()] }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("TeamConfig.Mode");
    }

    // ── ParticipantIds Validation ───────────────────────────────────

    [Fact]
    public void Should_Fail_When_ParticipantIds_Is_Empty()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto { Mode = "RoundRobin", ParticipantIds = [] }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("TeamConfig.ParticipantIds");
    }

    [Fact]
    public void Should_Fail_When_ParticipantIds_Contains_Empty_Guid()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto { Mode = "RoundRobin", ParticipantIds = [Guid.NewGuid(), Guid.Empty] }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("ParticipantIds must not contain empty GUIDs.");
    }

    // ── MaxIterations Validation ────────────────────────────────────

    [Fact]
    public void Should_Fail_When_MaxIterations_Is_Zero()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "RoundRobin",
                ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()],
                MaxIterations = 0
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("MaxIterations must be greater than 0.");
    }

    // ── Sequential / Concurrent / RoundRobin: min 2 participants ───

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Concurrent")]
    [InlineData("RoundRobin")]
    public void Should_Fail_When_Less_Than_2_Participants_For_Mode(string mode)
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto { Mode = mode, ParticipantIds = [Guid.NewGuid()] }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage($"{mode} Team requires at least 2 participants.");
    }

    // ── Handoffs-specific validation ────────────────────────────────

    [Fact]
    public void Should_Fail_When_Handoffs_Missing_InitialAgentId()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "Handoffs",
                ParticipantIds = [id1, id2],
                InitialAgentId = null,
                HandoffRoutes = new Dictionary<Guid, List<HandoffTargetDto>>
                {
                    [id1] = [new HandoffTargetDto { TargetAgentId = id2 }]
                }
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("InitialAgentId is required for Handoffs mode.");
    }

    [Fact]
    public void Should_Fail_When_Handoffs_InitialAgentId_Not_In_Participants()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "Handoffs",
                ParticipantIds = [id1, id2],
                InitialAgentId = outsider,
                HandoffRoutes = new Dictionary<Guid, List<HandoffTargetDto>>
                {
                    [id1] = [new HandoffTargetDto { TargetAgentId = id2 }]
                }
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("InitialAgentId must be one of the ParticipantIds.");
    }

    [Fact]
    public void Should_Fail_When_Handoffs_Missing_Routes()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "Handoffs",
                ParticipantIds = [id1, id2],
                InitialAgentId = id1,
                HandoffRoutes = null
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("HandoffRoutes is required for Handoffs mode.");
    }

    // ── Selector-specific validation ────────────────────────────────

    [Fact]
    public void Should_Fail_When_Selector_Missing_ProviderId()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "Selector",
                ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()],
                SelectorProviderId = null,
                SelectorModelId = "gpt-4o"
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("SelectorProviderId is required for Selector mode.");
    }

    [Fact]
    public void Should_Fail_When_Selector_Missing_ModelId()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "Selector",
                ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()],
                SelectorProviderId = Guid.NewGuid(),
                SelectorModelId = null
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("SelectorModelId is required for Selector mode.");
    }

    // ── MagneticOne-specific validation ─────────────────────────────

    [Fact]
    public void Should_Fail_When_MagneticOne_Missing_ProviderId()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "MagneticOne",
                ParticipantIds = [Guid.NewGuid()],
                OrchestratorProviderId = null,
                OrchestratorModelId = "gpt-4o"
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("OrchestratorProviderId is required for MagneticOne mode.");
    }

    [Fact]
    public void Should_Fail_When_MagneticOne_Missing_ModelId()
    {
        var command = CreateValidRoundRobinTeamCommand() with
        {
            TeamConfig = new TeamConfigDto
            {
                Mode = "MagneticOne",
                ParticipantIds = [Guid.NewGuid()],
                OrchestratorProviderId = Guid.NewGuid(),
                OrchestratorModelId = null
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveAnyValidationError()
            .WithErrorMessage("OrchestratorModelId is required for MagneticOne mode.");
    }

    // ── Happy Path: all valid modes ─────────────────────────────────

    [Fact]
    public void Should_Pass_For_Valid_RoundRobin_Team()
    {
        var command = CreateValidRoundRobinTeamCommand();

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_For_Valid_Handoffs_Team()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var command = new RegisterAgentCommand
        {
            Name = "handoff-team",
            AgentType = "Team",
            TeamConfig = new TeamConfigDto
            {
                Mode = "Handoffs",
                ParticipantIds = [id1, id2],
                InitialAgentId = id1,
                HandoffRoutes = new Dictionary<Guid, List<HandoffTargetDto>>
                {
                    [id1] = [new HandoffTargetDto { TargetAgentId = id2, Reason = "escalate" }]
                }
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_For_Valid_Selector_Team()
    {
        var command = new RegisterAgentCommand
        {
            Name = "selector-team",
            AgentType = "Team",
            TeamConfig = new TeamConfigDto
            {
                Mode = "Selector",
                ParticipantIds = [Guid.NewGuid(), Guid.NewGuid()],
                SelectorProviderId = Guid.NewGuid(),
                SelectorModelId = "gpt-4o"
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Pass_For_Valid_MagneticOne_Team()
    {
        var command = new RegisterAgentCommand
        {
            Name = "m1-team",
            AgentType = "Team",
            TeamConfig = new TeamConfigDto
            {
                Mode = "MagneticOne",
                ParticipantIds = [Guid.NewGuid()],
                OrchestratorProviderId = Guid.NewGuid(),
                OrchestratorModelId = "gpt-4o"
            }
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Helper ──────────────────────────────────────────────────────

    private static RegisterAgentCommand CreateValidRoundRobinTeamCommand() => new()
    {
        Name = "test-team",
        AgentType = "Team",
        TeamConfig = new TeamConfigDto
        {
            Mode = "RoundRobin",
            ParticipantIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            MaxIterations = 40
        }
    };
}
