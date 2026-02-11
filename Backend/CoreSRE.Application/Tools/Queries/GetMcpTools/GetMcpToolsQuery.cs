using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetMcpTools;

/// <summary>
/// 查询 MCP Server 下的子工具项列表
/// </summary>
public record GetMcpToolsQuery(Guid ToolRegistrationId) : IRequest<Result<List<McpToolItemDto>>>;
