using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetAvailableFunctions;

/// <summary>
/// 查询所有可绑定工具函数处理器
/// 扁平化 REST API 工具和 MCP 子工具为统一的 BindableToolDto 列表。
/// </summary>
public class GetAvailableFunctionsQueryHandler
    : IRequestHandler<GetAvailableFunctionsQuery, Result<IEnumerable<BindableToolDto>>>
{
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IMcpToolItemRepository _mcpToolRepo;

    public GetAvailableFunctionsQueryHandler(
        IToolRegistrationRepository toolRepo,
        IMcpToolItemRepository mcpToolRepo)
    {
        _toolRepo = toolRepo;
        _mcpToolRepo = mcpToolRepo;
    }

    public async Task<Result<IEnumerable<BindableToolDto>>> Handle(
        GetAvailableFunctionsQuery request,
        CancellationToken cancellationToken)
    {
        // Determine status filter: default to Active, "all" returns both
        ToolStatus? statusFilter = ToolStatus.Active;
        if (!string.IsNullOrEmpty(request.Status))
        {
            if (request.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                statusFilter = null;
            }
            else if (Enum.TryParse<ToolStatus>(request.Status, ignoreCase: true, out var parsed))
            {
                statusFilter = parsed;
            }
        }

        // Fetch all tools (we need both RestApi and McpServer to flatten)
        var allTools = await _toolRepo.GetByTypeAsync(null, cancellationToken);
        var toolsList = allTools.ToList();

        var result = new List<BindableToolDto>();

        foreach (var tool in toolsList)
        {
            // Apply status filter
            if (statusFilter.HasValue && tool.Status != statusFilter.Value)
                continue;

            if (tool.ToolType == ToolType.RestApi)
            {
                // REST API tools become one entry each
                if (MatchesSearch(tool.Name, tool.Description, request.Search))
                {
                    result.Add(new BindableToolDto
                    {
                        Id = tool.Id,
                        Name = tool.Name,
                        Description = tool.Description,
                        ToolType = "RestApi",
                        ParentName = null,
                        Status = tool.Status.ToString()
                    });
                }
            }
            else if (tool.ToolType == ToolType.McpServer)
            {
                // MCP servers: each sub-tool becomes a separate entry
                var mcpTools = await _mcpToolRepo.GetByToolRegistrationIdAsync(tool.Id, cancellationToken);

                foreach (var mcp in mcpTools)
                {
                    if (MatchesSearch(mcp.ToolName, mcp.Description, request.Search))
                    {
                        result.Add(new BindableToolDto
                        {
                            Id = mcp.Id,
                            Name = mcp.ToolName,
                            Description = mcp.Description,
                            ToolType = "McpTool",
                            ParentName = tool.Name,
                            Status = tool.Status.ToString()  // MCP sub-tools inherit parent status
                        });
                    }
                }
            }
        }

        // Sort alphabetically by name within each tool type group
        var sorted = result
            .OrderBy(t => t.ToolType)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<IEnumerable<BindableToolDto>>.Ok(sorted);
    }

    private static bool MatchesSearch(string name, string? description, string? search)
    {
        if (string.IsNullOrEmpty(search))
            return true;

        return name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
