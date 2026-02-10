using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.GetAgents;

/// <summary>
/// 查询 Agent 列表（支持按类型过滤）
/// </summary>
public record GetAgentsQuery(AgentType? Type = null) : IRequest<Result<List<AgentSummaryDto>>>;
