using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.Commands.RegisterTool;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.UpdateTool;

/// <summary>
/// 更新工具命令
/// </summary>
public record UpdateToolCommand : IRequest<Result<ToolRegistrationDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public RegisterToolConnectionConfig ConnectionConfig { get; init; } = new();
    public RegisterToolAuthConfig AuthConfig { get; init; } = new();

    /// <summary>输入参数 JSON Schema（可选，REST API 可手动定义）</summary>
    public string? InputSchema { get; init; }
}
