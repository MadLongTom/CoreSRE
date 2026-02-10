using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.DeleteAgent;

/// <summary>
/// 注销（删除）Agent 命令
/// </summary>
public record DeleteAgentCommand(Guid Id) : IRequest<Result<bool>>;
