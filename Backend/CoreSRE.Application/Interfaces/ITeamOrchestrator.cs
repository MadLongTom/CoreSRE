using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Domain.Entities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// Builds a Workflow-backed AIAgent from resolved participant agents and TeamConfig.
/// Called by AgentResolverService after all participants are individually resolved.
/// The orchestrator encapsulates the TeamMode → AgentWorkflowBuilder mapping logic.
/// </summary>
public interface ITeamOrchestrator
{
    /// <summary>
    /// Build a composite AIAgent that orchestrates multiple participant agents
    /// according to the team's configured mode (Sequential, Concurrent, RoundRobin,
    /// Handoffs, Selector, MagneticOne).
    /// </summary>
    /// <param name="teamRegistration">The Team-type AgentRegistration containing TeamConfigVO.</param>
    /// <param name="participants">Resolved participant AIAgents in the order specified by ParticipantIds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="eventQueue">Optional queue for MagneticOne ledger update events. When provided,
    /// the MagneticOne manager will push TeamChatEventDto instances for the streaming loop to drain.</param>
    /// <param name="managerClient">Optional IChatClient for modes that require an LLM-based manager:
    /// Selector mode uses this as the selector LLM, MagneticOne mode uses it as the orchestrator LLM.
    /// Built from TeamConfigVO.SelectorProviderId/ModelId or OrchestratorProviderId/ModelId.</param>
    /// <returns>A composite AIAgent wrapping the workflow pipeline.</returns>
    AIAgent BuildTeamAgent(
        AgentRegistration teamRegistration,
        IReadOnlyList<ResolvedAgent> participants,
        CancellationToken ct = default,
        ConcurrentQueue<TeamChatEventDto>? eventQueue = null,
        IChatClient? managerClient = null);
}
