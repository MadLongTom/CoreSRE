using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Queries.GetAgentById;

/// <summary>
/// 按 ID 查询 Agent 详情
/// </summary>
public record GetAgentByIdQuery(Guid Id) : IRequest<Result<AgentRegistrationDto>>;
