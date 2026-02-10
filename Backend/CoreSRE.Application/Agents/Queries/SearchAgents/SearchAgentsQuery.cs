using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.SearchAgents;

/// <summary>
/// 按技能关键词搜索 Agent 查询
/// </summary>
public record SearchAgentsQuery(string Query) : IRequest<Result<AgentSearchResponse>>;
