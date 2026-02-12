using CoreSRE.Application.Common.Interfaces;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Infrastructure.Persistence.Sessions;
using CoreSRE.Infrastructure.Services;
using CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;
using k8s;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        // Singleton IDbContextFactory for services outside scoped lifetime (e.g., AgentSessionStore).
        // Cannot use AddDbContextFactory here because AddDbContext already registered
        // DbContextOptions<AppDbContext> as Scoped, causing a lifetime conflict.
        services.AddSingleton<IDbContextFactory<AppDbContext>>(sp =>
        {
            var connStr = configuration.GetConnectionString("coresre")!;
            return new StandaloneDbContextFactory(connStr);
        });

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
        services.AddScoped<IWorkflowExecutionRepository, WorkflowExecutionRepository>();
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

        // Sandbox tools — Kubernetes Pod 容器隔离沙盒
        // 使用 KubernetesClient 通过 K8s API 管理 Pod 生命周期
        // 开发环境：Docker Desktop 内置 K8s（使用默认 kubeconfig）
        services.AddSingleton<k8s.Kubernetes>(sp =>
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();
            // Docker Desktop 使用自签名证书，WebSocket exec 时 .NET 证书链验证会
            // 触发 NullReferenceException (StorePal.LinkFromCertificateCollection)。
            // 跳过 TLS 验证以兼容本地开发环境；生产环境应使用受信任证书。
            config.SkipTlsVerify = true;
            return new k8s.Kubernetes(config);
        });
        // SandboxPodPool: Singleton Pod 池 + IHostedService（启动清孤儿/关闭删 Pod）
        services.AddSingleton<SandboxPodPool>();
        services.AddHostedService(sp => sp.GetRequiredService<SandboxPodPool>());
        services.AddScoped<ISandboxToolProvider, KubernetesSandboxToolProvider>();

        // Agent session store — PostgreSQL persistence for framework-managed chat history
        services.AddSingleton<AgentSessionStore>(sp =>
        {
            var contextFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresAgentSessionStore>();
            return new PostgresAgentSessionStore(contextFactory, logger);
        });

        // Semantic Memory — VectorStore is created per-agent in AgentResolverService
        // using the agent's configured EmbeddingProviderId + EmbeddingModelId.
        // No static VectorStore/IEmbeddingGenerator registration needed here.

        // Workflow Execution Engine + background service + channel
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        var workflowExecutionChannel = System.Threading.Channels.Channel.CreateUnbounded<ExecuteWorkflowRequest>();
        services.AddSingleton(workflowExecutionChannel);
        services.AddHostedService<WorkflowExecutionBackgroundService>();

        return services;
    }
}
