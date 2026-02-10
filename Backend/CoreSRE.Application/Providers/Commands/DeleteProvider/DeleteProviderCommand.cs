using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Providers.Commands.DeleteProvider;

/// <summary>
/// 删除 LLM Provider 命令
/// </summary>
public record DeleteProviderCommand(Guid Id) : IRequest<Result<bool>>;
