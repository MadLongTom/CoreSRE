using CoreSRE.Application.Common.Interfaces;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Infrastructure.Persistence.Sessions;
using CoreSRE.Infrastructure.Services;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;

namespace CoreSRE.Infrastructure;

/// <summary>
/// Infrastructure 层依赖注入配置
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("coresre")));

        // IDbContextFactory for services that need to create DbContext outside of scoped lifetime
        // (e.g., singleton AgentSessionStore)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("coresre")), ServiceLifetime.Scoped);

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IAgentRegistrationRepository, AgentRegistrationRepository>();
        services.AddScoped<ILlmProviderRepository, LlmProviderRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Agent resolver — resolves AgentRegistration → AIAgent for AG-UI chat
        services.AddScoped<IAgentResolver, AgentResolverService>();

        // Chat history reader — reads messages from AgentSessionRecord.SessionData
        services.AddScoped<IChatHistoryReader, ChatHistoryReader>();

        // Model discovery service + named HttpClient
        services.AddHttpClient("ModelDiscovery");
        services.AddScoped<IModelDiscoveryService, ModelDiscoveryService>();

        // A2A AgentCard resolver + named HttpClient
        services.AddHttpClient("A2ACardResolver");
        services.AddScoped<IAgentCardResolver, A2ACardResolverService>();

        // Tool Gateway services
        services.AddDataProtection();
        services.AddScoped<IToolRegistrationRepository, ToolRegistrationRepository>();
        services.AddScoped<IMcpToolItemRepository, McpToolItemRepository>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<ICredentialEncryptionService, CredentialEncryptionService>();
        services.AddScoped<IMcpToolDiscoveryService, McpToolDiscoveryService>();
        services.AddScoped<IOpenApiParserService, OpenApiParserService>();

        // MCP Discovery background service + channel
        var mcpDiscoveryChannel = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        services.AddSingleton(mcpDiscoveryChannel);
        services.AddHostedService<McpDiscoveryBackgroundService>();

        // Tool Invoker services + factory + named HttpClient
        services.AddHttpClient("ToolInvoker");
        services.AddScoped<IToolInvoker, RestApiToolInvoker>();
        services.AddScoped<IToolInvoker, McpToolInvoker>();
        services.AddScoped<IToolInvokerFactory, ToolInvokerFactory>();

        // Tool-to-AIFunction conversion factory (for ChatClient tool binding)
        services.AddScoped<IToolFunctionFactory, ToolFunctionFactory>();

        return services;
    }

    /// <summary>
    /// 创建 PostgresAgentSessionStore 工厂委托，用于 Agent Framework 的 WithSessionStore 注册。
    /// <para>
    /// 使用示例（在 Program.cs 中）:
    /// <code>
    /// agentBuilder.WithSessionStore(DependencyInjection.CreatePostgresSessionStore);
    /// </code>
    /// </para>
    /// </summary>
    public static AgentSessionStore CreatePostgresSessionStore(IServiceProvider serviceProvider, string agentName)
    {
        var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return new PostgresAgentSessionStore(contextFactory);
    }
}
