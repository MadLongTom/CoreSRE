using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// AIFunction subclass for REST API ToolRegistration entities.
/// Wraps a ToolRegistration with its IToolInvoker to provide invocable AI function support
/// for the FunctionInvokingChatClient pipeline.
/// </summary>
public sealed class ToolRegistrationAIFunction : AIFunction
{
    private readonly ToolRegistration _tool;
    private readonly IToolInvoker _invoker;
    private readonly JsonElement? _jsonSchema;

    public ToolRegistrationAIFunction(ToolRegistration tool, IToolInvoker invoker)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

        // Parse InputSchema string → JsonElement (R7: schema normalization)
        if (tool.ToolSchema?.InputSchema is { } schemaStr && !string.IsNullOrWhiteSpace(schemaStr))
        {
            _jsonSchema = JsonDocument.Parse(schemaStr).RootElement.Clone();
        }
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description ?? string.Empty;

    public override JsonElement JsonSchema => _jsonSchema ?? default;

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
            _tool,
            mcpToolName: null,
            parameters: parameters,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return JsonSerializer.Serialize(new { error = result.Error ?? "Tool invocation failed" });
        }

        return result.Data.HasValue
            ? JsonSerializer.Serialize(result.Data.Value)
            : JsonSerializer.Serialize(new { success = true });
    }
}
