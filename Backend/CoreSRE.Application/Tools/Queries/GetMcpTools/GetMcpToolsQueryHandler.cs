using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Queries.GetMcpTools;

/// <summary>
/// 查询 MCP Server 下的子工具项列表处理器
/// </summary>
public class GetMcpToolsQueryHandler : IRequestHandler<GetMcpToolsQuery, Result<List<McpToolItemDto>>>
{
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IMcpToolItemRepository _mcpToolRepo;
    private readonly IMapper _mapper;

    public GetMcpToolsQueryHandler(
        IToolRegistrationRepository toolRepo,
        IMcpToolItemRepository mcpToolRepo,
        IMapper mapper)
    {
        _toolRepo = toolRepo;
        _mcpToolRepo = mcpToolRepo;
        _mapper = mapper;
    }

    public async Task<Result<List<McpToolItemDto>>> Handle(
        GetMcpToolsQuery request,
        CancellationToken cancellationToken)
    {
        var tool = await _toolRepo.GetByIdAsync(request.ToolRegistrationId, cancellationToken);
        if (tool is null)
            return Result<List<McpToolItemDto>>.NotFound($"Tool with ID '{request.ToolRegistrationId}' not found.");

        if (tool.ToolType != ToolType.McpServer)
            return Result<List<McpToolItemDto>>.Fail($"Tool '{tool.Name}' is not an MCP Server. Only McpServer type tools have sub-tools.");

        var items = await _mcpToolRepo.GetByToolRegistrationIdAsync(request.ToolRegistrationId, cancellationToken);
        var dtos = _mapper.Map<List<McpToolItemDto>>(items);

        return Result<List<McpToolItemDto>>.Ok(dtos);
    }
}
