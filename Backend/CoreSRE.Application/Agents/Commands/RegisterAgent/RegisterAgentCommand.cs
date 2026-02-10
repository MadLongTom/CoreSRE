using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.RegisterAgent;

/// <summary>
/// 注册 Agent 命令
/// </summary>
public record RegisterAgentCommand : IRequest<Result<AgentRegistrationDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string AgentType { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public AgentCardDto? AgentCard { get; init; }
    public LlmConfigDto? LlmConfig { get; init; }
    public Guid? WorkflowRef { get; init; }
}
