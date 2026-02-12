using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Converts ToolRefs (Guid list) into invocable AIFunction instances.
/// Queries both ToolRegistration (REST API) and McpToolItem (MCP sub-tool) repositories,
/// creates the appropriate AIFunction subclass for each match, and skips unmatched IDs.
/// </summary>
public sealed class ToolFunctionFactory : IToolFunctionFactory
{
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IMcpToolItemRepository _mcpRepo;
    private readonly IToolInvokerFactory _invokerFactory;
    private readonly ILogger<ToolFunctionFactory> _logger;

    public ToolFunctionFactory(
        IToolRegistrationRepository toolRepo,
        IMcpToolItemRepository mcpRepo,
        IToolInvokerFactory invokerFactory,
        ILogger<ToolFunctionFactory> logger)
    {
        _toolRepo = toolRepo;
        _mcpRepo = mcpRepo;
        _invokerFactory = invokerFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AIFunction>> CreateFunctionsAsync(
        IReadOnlyList<Guid> toolRefs,
        CancellationToken cancellationToken = default)
    {
        if (toolRefs.Count == 0)
            return Array.Empty<AIFunction>();

        // Query both repositories sequentially (same scoped DbContext — cannot run in parallel)
        var toolRegs = (await _toolRepo.GetByIdsAsync(toolRefs, cancellationToken)).ToList();
        var mcpItems = (await _mcpRepo.GetByIdsAsync(toolRefs, cancellationToken)).ToList();

        var functions = new List<AIFunction>();
        var matchedIds = new HashSet<Guid>();

        // Create ToolRegistrationAIFunction for each REST API match
        foreach (var tool in toolRegs)
        {
            try
            {
                var invoker = _invokerFactory.GetInvoker(tool.ToolType);
                functions.Add(new ToolRegistrationAIFunction(tool, invoker));
                matchedIds.Add(tool.Id);

                if (tool.Status == ToolStatus.Inactive)
                {
                    _logger.LogInformation(
                        "Tool '{ToolName}' (ID: {ToolId}) is inactive but will be included in AIFunction list",
                        tool.Name, tool.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create AIFunction for ToolRegistration '{ToolName}' (ID: {ToolId})", tool.Name, tool.Id);
            }
        }

        // Create McpToolAIFunction for each MCP sub-tool match
        foreach (var mcpItem in mcpItems)
        {
            try
            {
                var parentTool = mcpItem.ToolRegistration
                    ?? throw new InvalidOperationException(
                        $"McpToolItem '{mcpItem.ToolName}' (ID: {mcpItem.Id}) has no parent ToolRegistration loaded");

                var invoker = _invokerFactory.GetInvoker(parentTool.ToolType);
                functions.Add(new McpToolAIFunction(mcpItem, parentTool, invoker));
                matchedIds.Add(mcpItem.Id);

                if (parentTool.Status == ToolStatus.Inactive)
                {
                    _logger.LogInformation(
                        "MCP tool '{McpToolName}' parent '{ParentToolName}' (ID: {ParentToolId}) is inactive but will be included",
                        mcpItem.ToolName, parentTool.Name, parentTool.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create AIFunction for McpToolItem '{McpToolName}' (ID: {McpToolId})", mcpItem.ToolName, mcpItem.Id);
            }
        }

        // Log warnings for unmatched IDs (deleted tools)
        foreach (var id in toolRefs)
        {
            if (!matchedIds.Contains(id))
            {
                _logger.LogWarning("ToolRef '{ToolRefId}' not found in any repository — tool may have been deleted", id);
            }
        }

        _logger.LogDebug(
            "Created {FunctionCount} AIFunctions from {ToolRefCount} ToolRefs ({MatchedCount} matched, {UnmatchedCount} unmatched)",
            functions.Count, toolRefs.Count, matchedIds.Count, toolRefs.Count - matchedIds.Count);

        return functions;
    }
}
