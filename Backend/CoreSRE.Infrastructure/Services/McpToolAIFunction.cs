using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// AIFunction subclass for MCP sub-tool items (McpToolItem).
/// Wraps a McpToolItem with its parent ToolRegistration and IToolInvoker to provide
/// invocable AI function support for the FunctionInvokingChatClient pipeline.
/// </summary>
public sealed class McpToolAIFunction : AIFunction
{
    private readonly McpToolItem _mcpItem;
    private readonly ToolRegistration _parentTool;
    private readonly IToolInvoker _invoker;

    public McpToolAIFunction(McpToolItem mcpItem, ToolRegistration parentTool, IToolInvoker invoker)
    {
        _mcpItem = mcpItem ?? throw new ArgumentNullException(nameof(mcpItem));
        _parentTool = parentTool ?? throw new ArgumentNullException(nameof(parentTool));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    public override string Name => _mcpItem.ToolName;

    public override string Description => _mcpItem.Description ?? string.Empty;

    /// <summary>
    /// McpToolItem.InputSchema is already JsonElement? — use directly (R7: no normalization needed).
    /// </summary>
    public override JsonElement JsonSchema => _mcpItem.InputSchema ?? default;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Convert AIFunctionArguments to IDictionary<string, object?>
        var parameters = new Dictionary<string, object?>();
        foreach (var kvp in arguments)
        {
            parameters[kvp.Key] = kvp.Value;
        }

        var result = await _invoker.InvokeAsync(
            _parentTool,
            mcpToolName: _mcpItem.ToolName,
            parameters: parameters,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return JsonSerializer.Serialize(new { error = result.Error ?? "MCP tool invocation failed" });
        }

        return result.Data.HasValue
            ? JsonSerializer.Serialize(result.Data.Value)
            : JsonSerializer.Serialize(new { success = true });
    }
}
