using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.DeleteTool;

/// <summary>
/// 删除工具命令
/// </summary>
public record DeleteToolCommand(Guid Id) : IRequest<Result<bool>>;
