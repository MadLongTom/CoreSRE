using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.ResolveAgentCard;

/// <summary>
/// 解析远程 A2A Agent 端点的 AgentCard 查询
/// </summary>
public record ResolveAgentCardQuery(string Url) : IRequest<Result<ResolvedAgentCardDto>>;
