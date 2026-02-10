using System.Text.Json;
using A2A;
using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 使用 A2A SDK 的 A2ACardResolver 从远程端点解析 AgentCard。
/// </summary>
public class A2ACardResolverService : IAgentCardResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<A2ACardResolverService> _logger;

    public A2ACardResolverService(
        IHttpClientFactory httpClientFactory,
        ILogger<A2ACardResolverService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResolvedAgentCardDto> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("A2ACardResolver");
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var baseUri = new Uri(url);
        var resolver = new A2ACardResolver(baseUri, httpClient, logger: _logger);

        _logger.LogInformation("正在解析 AgentCard: {Url}", url);

        AgentCard agentCard;
        try
        {
            agentCard = await resolver.GetAgentCardAsync(cancellationToken);
        }
        catch (A2AException ex) when (ex.InnerException is HttpRequestException httpEx)
        {
            // SDK wraps HttpRequestException into A2AException — unwrap for handler
            throw httpEx;
        }
        catch (A2AException ex)
        {
            // JSON parse errors or other A2A protocol errors
            var wrapped = new InvalidOperationException($"AgentCard 解析失败: {ex.Message}", ex);
            wrapped.Data["AgentCardParseError"] = true;
            throw wrapped;
        }

        _logger.LogInformation("AgentCard 解析成功: {Name} (v{Version})", agentCard.Name, agentCard.Version);

        return MapToDto(agentCard);
    }

    private static ResolvedAgentCardDto MapToDto(AgentCard card)
    {
        return new ResolvedAgentCardDto
        {
            Name = card.Name,
            Description = card.Description,
            Url = card.Url,
            Version = card.Version,
            Skills = card.Skills?.Select(s => new AgentSkillDto
            {
                Name = s.Name,
                Description = s.Description
            }).ToList() ?? [],
            Interfaces = card.AdditionalInterfaces?.Select(i => new AgentInterfaceDto
            {
                Protocol = i.Transport.Label,
                Path = i.Url
            }).ToList() ?? [],
            SecuritySchemes = MapSecuritySchemes(card.SecuritySchemes)
        };
    }

    private static List<SecuritySchemeDto> MapSecuritySchemes(Dictionary<string, SecurityScheme>? schemes)
    {
        if (schemes is null || schemes.Count == 0)
            return [];

        return schemes.Select(kvp => new SecuritySchemeDto
        {
            Type = kvp.Key,
            Parameters = JsonSerializer.Serialize(kvp.Value)
        }).ToList();
    }
}
