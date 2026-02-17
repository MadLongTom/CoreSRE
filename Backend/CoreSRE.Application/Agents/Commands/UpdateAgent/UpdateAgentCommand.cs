using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.UpdateAgent;

/// <summary>
/// 更新 Agent 命令。agentType 不在请求中——从现有实体读取。
/// </summary>
public record UpdateAgentCommand : IRequest<Result<AgentRegistrationDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Endpoint { get; init; }
    public AgentCardDto? AgentCard { get; init; }
    public LlmConfigDto? LlmConfig { get; init; }
    public Guid? WorkflowRef { get; init; }
    public TeamConfigDto? TeamConfig { get; init; }
}
