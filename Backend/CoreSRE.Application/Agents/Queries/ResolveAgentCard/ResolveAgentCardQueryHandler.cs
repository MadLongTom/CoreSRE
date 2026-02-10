using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Agents.Queries.ResolveAgentCard;

/// <summary>
/// 处理 AgentCard 解析查询。
/// IAgentCardResolver 实现负责捕获 SDK 异常并转换为标准 .NET 异常。
/// </summary>
public class ResolveAgentCardQueryHandler : IRequestHandler<ResolveAgentCardQuery, Result<ResolvedAgentCardDto>>
{
    private readonly IAgentCardResolver _resolver;
    private readonly ILogger<ResolveAgentCardQueryHandler> _logger;

    public ResolveAgentCardQueryHandler(IAgentCardResolver resolver, ILogger<ResolveAgentCardQueryHandler> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<Result<ResolvedAgentCardDto>> Handle(ResolveAgentCardQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _resolver.ResolveAsync(request.Url, cancellationToken);
            return Result<ResolvedAgentCardDto>.Ok(dto);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "无法连接到远程端点: {Url}", request.Url);
            return Result<ResolvedAgentCardDto>.BadGateway($"无法连接到远程 Agent 端点: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "请求超时: {Url}", request.Url);
            return new Result<ResolvedAgentCardDto>
            {
                Success = false,
                Message = "请求超时，远程端点未在 10 秒内响应",
                ErrorCode = 504
            };
        }
        catch (InvalidOperationException ex) when (ex.Data.Contains("AgentCardParseError"))
        {
            _logger.LogWarning(ex, "AgentCard 解析失败: {Url}", request.Url);
            return new Result<ResolvedAgentCardDto>
            {
                Success = false,
                Message = $"AgentCard 解析失败: {ex.Message}",
                ErrorCode = 422
            };
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "URL 格式无效: {Url}", request.Url);
            return Result<ResolvedAgentCardDto>.Fail($"URL 格式无效: {ex.Message}");
        }
    }
}
