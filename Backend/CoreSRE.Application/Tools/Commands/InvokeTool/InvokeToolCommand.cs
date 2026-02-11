using FluentValidation;
using MediatR;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using System.Text.Json;

namespace CoreSRE.Application.Tools.Commands.InvokeTool;

/// <summary>
/// 统一工具调用命令。
/// </summary>
public record InvokeToolCommand : IRequest<Result<ToolInvocationResultDto>>
{
    /// <summary>工具注册 ID</summary>
    public Guid ToolRegistrationId { get; init; }

    /// <summary>MCP 工具名称（仅 McpServer 类型必填）</summary>
    public string? McpToolName { get; init; }

    /// <summary>调用参数（Body 参数或 MCP 调用参数）</summary>
    public IDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>REST 查询参数（Query String）。仅 REST API 类型使用。</summary>
    public IDictionary<string, string>? QueryParameters { get; init; }

    /// <summary>REST 自定义请求头参数。仅 REST API 类型使用。</summary>
    public IDictionary<string, string>? HeaderParameters { get; init; }
}

/// <summary>
/// InvokeToolCommand 验证器。
/// </summary>
public class InvokeToolCommandValidator : AbstractValidator<InvokeToolCommand>
{
    public InvokeToolCommandValidator()
    {
        RuleFor(x => x.ToolRegistrationId)
            .NotEmpty()
            .WithMessage("ToolRegistrationId is required.");

        RuleFor(x => x.Parameters)
            .NotNull()
            .WithMessage("Parameters must not be null.");
    }
}
