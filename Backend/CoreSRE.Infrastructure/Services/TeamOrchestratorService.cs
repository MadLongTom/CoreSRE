using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

// IChatClient is used for LLM-based manager modes (Selector, MagneticOne)

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Builds multi-agent Workflow pipelines from TeamConfigVO using Microsoft.Agents.AI.Workflows.
/// Maps each TeamMode to the appropriate AgentWorkflowBuilder method.
/// </summary>
public class TeamOrchestratorService : ITeamOrchestrator
{
    private readonly ILogger<TeamOrchestratorService> _logger;

    public TeamOrchestratorService(ILogger<TeamOrchestratorService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public AIAgent BuildTeamAgent(
        AgentRegistration teamRegistration,
        IReadOnlyList<ResolvedAgent> participants,
        CancellationToken ct = default,
        ConcurrentQueue<TeamChatEventDto>? eventQueue = null,
        IChatClient? managerClient = null)
    {
        var teamConfig = teamRegistration.TeamConfig
            ?? throw new InvalidOperationException($"Team agent '{teamRegistration.Name}' has no TeamConfig.");

        if (participants.Count == 0)
            throw new InvalidOperationException($"Team agent '{teamRegistration.Name}' has no participants.");

        // Validate participant name uniqueness (critical for Handoffs tool naming)
        ValidateParticipantNames(teamRegistration.Name, participants, teamConfig.Mode);

        _logger.LogInformation(
            "Building team agent '{TeamName}' with mode {Mode} and {Count} participants: [{Participants}]",
            teamRegistration.Name, teamConfig.Mode, participants.Count,
            string.Join(", ", participants.Select(p => p.Agent.Name)));

        var workflow = teamConfig.Mode switch
        {
            TeamMode.Sequential => BuildSequential(participants),
            TeamMode.Concurrent => BuildConcurrent(participants, teamConfig),
            TeamMode.RoundRobin => BuildRoundRobin(participants, teamConfig),
            TeamMode.Handoffs => BuildHandoffs(participants, teamConfig),
            TeamMode.Selector => BuildSelector(participants, teamConfig, managerClient),
            TeamMode.MagneticOne => BuildMagneticOne(participants, teamConfig, eventQueue, managerClient),
            _ => throw new NotSupportedException($"Team mode '{teamConfig.Mode}' is not supported.")
        };

        return workflow.AsAgent(
            id: teamRegistration.Id.ToString(),
            name: teamRegistration.Name,
            description: teamRegistration.Description ?? $"{teamConfig.Mode} team agent");
    }

    /// <summary>T012: Sequential — output of agent N → input of agent N+1</summary>
    private Workflow BuildSequential(IReadOnlyList<ResolvedAgent> participants)
    {
        var agents = participants.Select(p => p.Agent).ToArray();
        return AgentWorkflowBuilder.BuildSequential(agents);
    }

    /// <summary>T012: Concurrent — fan-out to all agents, aggregate results</summary>
    private Workflow BuildConcurrent(IReadOnlyList<ResolvedAgent> participants, Domain.ValueObjects.TeamConfigVO config)
    {
        var agents = participants.Select(p => p.Agent).ToArray();

        // Default aggregator: concatenate all responses with agent name labels
        Func<IList<List<ChatMessage>>, List<ChatMessage>> aggregator = results =>
        {
            var merged = new List<ChatMessage>();
            foreach (var agentMessages in results)
            {
                merged.AddRange(agentMessages);
            }
            return merged;
        };

        return AgentWorkflowBuilder.BuildConcurrent(agents, aggregator);
    }

    /// <summary>T013: RoundRobin — shared history, RoundRobinGroupChatManager cycles speakers</summary>
    private Workflow BuildRoundRobin(IReadOnlyList<ResolvedAgent> participants, Domain.ValueObjects.TeamConfigVO config)
    {
        var agents = participants.Select(p => p.Agent).ToArray();
        var maxIterations = config.MaxIterations;

        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agentList =>
            {
                var manager = new RoundRobinGroupChatManager(agentList);
                manager.MaximumIterationCount = maxIterations;
                return manager;
            })
            .AddParticipants(agents)
            .Build();
    }

    /// <summary>T014: Handoffs — triage agent delegates via handoff tool calls</summary>
    private Workflow BuildHandoffs(IReadOnlyList<ResolvedAgent> participants, Domain.ValueObjects.TeamConfigVO config)
    {
        if (config.InitialAgentId is null)
            throw new InvalidOperationException("Handoffs mode requires InitialAgentId.");

        // Build a map from agent ID → AIAgent for route resolution
        var agentMap = new Dictionary<string, AIAgent>();
        foreach (var p in participants)
        {
            agentMap[p.Agent.Id ?? ""] = p.Agent;
        }

        // Find the initial agent
        var initialAgentIdStr = config.InitialAgentId.Value.ToString();
        AIAgent? initialAgent = null;
        foreach (var p in participants)
        {
            if (string.Equals(p.Agent.Id, initialAgentIdStr, StringComparison.OrdinalIgnoreCase))
            {
                initialAgent = p.Agent;
                break;
            }
        }

        if (initialAgent is null)
        {
            // Fallback: use first participant as initial
            _logger.LogWarning("InitialAgentId '{Id}' not found in resolved participants. Using first participant.", config.InitialAgentId);
            initialAgent = participants[0].Agent;
        }

        // Build handoff workflow
        var builder = AgentWorkflowBuilder.CreateHandoffBuilderWith(initialAgent);

        // Wire handoff routes from TeamConfigVO
        if (config.HandoffRoutes is { Count: > 0 })
        {
            foreach (var (sourceId, targets) in config.HandoffRoutes)
            {
                var sourceIdStr = sourceId.ToString();
                if (!agentMap.TryGetValue(sourceIdStr, out var sourceAgent))
                {
                    _logger.LogWarning("Handoff source '{Id}' not found in resolved participants. Skipping.", sourceId);
                    continue;
                }

                foreach (var target in targets)
                {
                    var targetIdStr = target.TargetAgentId.ToString();
                    if (!agentMap.TryGetValue(targetIdStr, out var targetAgent))
                    {
                        _logger.LogWarning("Handoff target '{Id}' not found in resolved participants. Skipping.", target.TargetAgentId);
                        continue;
                    }

                    builder = builder.WithHandoff(sourceAgent, targetAgent);
                }
            }
        }

        return builder.Build();
    }

    /// <summary>T016: Selector — LLM selects next speaker via custom GroupChatManager</summary>
    private Workflow BuildSelector(
        IReadOnlyList<ResolvedAgent> participants,
        Domain.ValueObjects.TeamConfigVO config,
        IChatClient? managerClient)
    {
        var agents = participants.Select(p => p.Agent).ToArray();
        var maxIterations = config.MaxIterations;
        var selectorPrompt = config.SelectorPrompt;
        var allowRepeated = config.AllowRepeatedSpeaker;

        if (managerClient is null)
            throw new InvalidOperationException(
                "Selector mode requires an IChatClient for LLM-based selection. " +
                "Ensure SelectorProviderId and SelectorModelId are configured.");

        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agentList =>
            {
                var manager = new LlmSelectorGroupChatManager(
                    agentList,
                    managerClient,
                    selectorPrompt,
                    allowRepeated);
                manager.MaximumIterationCount = maxIterations;
                return manager;
            })
            .AddParticipants(agents)
            .Build();
    }

    /// <summary>T018: MagneticOne — dual-loop ledger with orchestrator LLM</summary>
    private Workflow BuildMagneticOne(
        IReadOnlyList<ResolvedAgent> participants,
        Domain.ValueObjects.TeamConfigVO config,
        ConcurrentQueue<TeamChatEventDto>? eventQueue,
        IChatClient? managerClient)
    {
        var agents = participants.Select(p => p.Agent).ToArray();
        var maxIterations = config.MaxIterations;
        var maxStalls = config.MaxStalls;

        if (managerClient is null)
            throw new InvalidOperationException(
                "MagneticOne mode requires an IChatClient for orchestrator LLM. " +
                "Ensure OrchestratorProviderId and OrchestratorModelId are configured.");

        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agentList =>
            {
                var manager = new MagneticOneGroupChatManager(
                    agentList,
                    managerClient,
                    maxStalls,
                    config.FinalAnswerPrompt,
                    logger: _logger);
                manager.MaximumIterationCount = maxIterations;

                // Wire ledger update callback to push events to the queue
                if (eventQueue is not null)
                {
                    manager.OnLedgerUpdate = dto => eventQueue.Enqueue(dto);
                }

                return manager;
            })
            .AddParticipants(agents)
            .Build();
    }

    /// <summary>T019: Validate participant names are unique (required for Handoffs tool naming)</summary>
    private void ValidateParticipantNames(string teamName, IReadOnlyList<ResolvedAgent> participants, TeamMode mode)
    {
        var nameGroups = participants
            .GroupBy(p => p.Agent.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (nameGroups.Count > 0)
        {
            var duplicates = string.Join(", ", nameGroups.Select(g => $"'{g.Key}' (×{g.Count()})"));
            if (mode == TeamMode.Handoffs)
            {
                throw new InvalidOperationException(
                    $"Team '{teamName}' has duplicate participant names ({duplicates}). " +
                    "Handoffs mode requires unique names for tool function generation.");
            }

            _logger.LogWarning(
                "Team '{TeamName}' has duplicate participant names: {Duplicates}. This may cause issues.",
                teamName, duplicates);
        }
    }
}
