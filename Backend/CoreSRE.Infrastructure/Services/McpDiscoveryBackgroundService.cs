using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// MCP 发现后台服务。通过 Channel&lt;Guid&gt; 接收 ToolRegistrationId，
/// 异步执行 MCP 握手并保存发现的工具项。
/// </summary>
public class McpDiscoveryBackgroundService : BackgroundService
{
    private readonly Channel<Guid> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<McpDiscoveryBackgroundService> _logger;

    public McpDiscoveryBackgroundService(
        Channel<Guid> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<McpDiscoveryBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP Discovery BackgroundService started.");

        await foreach (var toolRegistrationId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessDiscoveryAsync(toolRegistrationId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing MCP discovery for ToolRegistration {Id}.", toolRegistrationId);
            }
        }
    }

    private async Task ProcessDiscoveryAsync(Guid toolRegistrationId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var toolRepo = scope.ServiceProvider.GetRequiredService<IToolRegistrationRepository>();
        var mcpToolRepo = scope.ServiceProvider.GetRequiredService<IMcpToolItemRepository>();
        var discoveryService = scope.ServiceProvider.GetRequiredService<IMcpToolDiscoveryService>();

        var tool = await toolRepo.GetByIdAsync(toolRegistrationId, cancellationToken);
        if (tool is null)
        {
            _logger.LogWarning("ToolRegistration {Id} not found for MCP discovery.", toolRegistrationId);
            return;
        }

        _logger.LogInformation("Starting MCP discovery for '{Name}' ({Id}).", tool.Name, tool.Id);

        // Delete existing MCP tool items before re-discovery
        await mcpToolRepo.DeleteByToolRegistrationIdAsync(toolRegistrationId, cancellationToken);

        var result = await discoveryService.DiscoverToolsAsync(tool, cancellationToken);

        if (result.Success && result.Data is not null)
        {
            foreach (var mcpToolItem in result.Data)
            {
                await mcpToolRepo.AddAsync(mcpToolItem, cancellationToken);
            }

            tool.MarkActive();
            await toolRepo.UpdateAsync(tool, cancellationToken);

            _logger.LogInformation("MCP discovery succeeded for '{Name}': {Count} tools discovered.", tool.Name, result.Data.Count);
        }
        else
        {
            tool.MarkInactive(result.Message);
            await toolRepo.UpdateAsync(tool, cancellationToken);

            _logger.LogWarning("MCP discovery failed for '{Name}': {Error}", tool.Name, result.Message);
        }
    }
}
