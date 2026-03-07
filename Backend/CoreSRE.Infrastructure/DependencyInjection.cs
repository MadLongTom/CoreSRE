using CoreSRE.Application.Common.Interfaces;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Infrastructure.Persistence.Sessions;
using CoreSRE.Infrastructure.Services;
using CoreSRE.Infrastructure.Services.DataSources;
using CoreSRE.Infrastructure.Services.Sandbox;
using CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;
using CoreSRE.Infrastructure.Services.Storage;
using k8s;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace CoreSRE.Infrastructure;

/// <summary>
/// Infrastructure 层依赖注入配置
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("coresre"));
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(dataSource));

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

        // Team orchestrator — builds multi-agent workflow pipelines for Team-type agents
        services.AddScoped<ITeamOrchestrator, TeamOrchestratorService>();

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

        // Expression Engine — V8 (ClearScript) JavaScript 表达式求值器
        services.AddScoped<IExpressionEvaluator, V8ExpressionEvaluator>();

        // Workflow Execution Notifier — 实时推送执行事件
        services.AddScoped<IWorkflowExecutionNotifier, NullWorkflowExecutionNotifier>();

        // Workflow Execution Engine + background service + channel
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        var workflowExecutionChannel = System.Threading.Channels.Channel.CreateUnbounded<ExecuteWorkflowRequest>();
        services.AddSingleton(workflowExecutionChannel);
        services.AddHostedService<WorkflowExecutionBackgroundService>();

        // ── S3 File Storage (MinIO) ──
        services.AddScoped<IFileStorageService, MinioFileStorageService>();
        services.AddHostedService<BucketInitializationService>();

        // ── Skill Registration Repository ──
        services.AddScoped<ISkillRegistrationRepository, SkillRegistrationRepository>();

        // ── DataSource Registration Repository ──
        services.AddScoped<IDataSourceRegistrationRepository, DataSourceRegistrationRepository>();

        // ── Alert & Incident Repositories ──
        services.AddScoped<IAlertRuleRepository, AlertRuleRepository>();
        services.AddScoped<IIncidentRepository, IncidentRepository>();

        // ── Spec 025: Canary & Prompt Suggestion Repositories ──
        services.AddScoped<ICanaryResultRepository, CanaryResultRepository>();
        services.AddScoped<IPromptSuggestionRepository, PromptSuggestionRepository>();

        // ── Alert Payload Parser ──
        services.AddSingleton<Application.Alerts.Interfaces.IAlertmanagerPayloadParser, AlertmanagerPayloadParser>();

        // ── Incident Dispatcher ──
        services.AddScoped<Application.Alerts.Interfaces.IIncidentDispatcher, IncidentDispatcherService>();

        // ── Active Incident Session Tracker (Singleton — shared across scopes) ──
        services.AddSingleton<ActiveIncidentSessionTracker>();
        services.AddSingleton<Application.Alerts.Interfaces.IActiveIncidentTracker>(sp =>
            sp.GetRequiredService<ActiveIncidentSessionTracker>());

        // ── SOP Parser ──
        services.AddSingleton<Application.Alerts.Interfaces.ISopParserService, SopParserService>();

        // ── SOP Validator (Spec 022) ──
        services.AddScoped<Application.Alerts.Interfaces.ISopValidator, SopValidatorService>();

        // ── SOP Structured Parser (Spec 024) ──
        services.AddSingleton<Application.Alerts.Interfaces.ISopStructuredParser, SopStructuredParserService>();

        // ── Agent Caller (abstracts Agent framework for Application layer) ──
        services.AddScoped<Application.Alerts.Interfaces.IAgentCaller, AgentCallerService>();

        // ── Notification Channels ──
        services.AddHttpClient("NotificationChannel");
        services.AddScoped<Application.Alerts.Interfaces.INotificationChannel, Services.Notifications.SlackNotificationChannel>();
        services.AddScoped<Application.Alerts.Interfaces.INotificationChannel, Services.Notifications.TeamsNotificationChannel>();
        services.AddScoped<Application.Alerts.Interfaces.INotificationDispatcher, Services.Notifications.NotificationDispatcher>();

        // ── DataSource Querier services + factory + named HttpClient ──
        services.AddHttpClient("DataSourceQuerier");
        services.AddScoped<IDataSourceQuerier, PrometheusQuerier>();
        services.AddScoped<IDataSourceQuerier, LokiQuerier>();
        services.AddScoped<IDataSourceQuerier, JaegerQuerier>();
        services.AddScoped<IDataSourceQuerier, AlertmanagerQuerier>();
        services.AddScoped<IDataSourceQuerier, KubernetesQuerier>();
        services.AddScoped<IDataSourceQuerier, ArgoCDQuerier>();
        services.AddScoped<IDataSourceQuerier, GitHubQuerier>();
        services.AddScoped<IDataSourceQuerier, GitLabQuerier>();
        services.AddScoped<IDataSourceQuerierFactory, DataSourceQuerierFactory>();

        // DataSource-to-AIFunction conversion factory (for ChatClient datasource binding)
        services.AddScoped<IDataSourceFunctionFactory, DataSourceFunctionFactory>();

        // DataSource periodic health check background service
        services.AddHostedService<DataSourceHealthCheckBackgroundService>();

        // ── Skill SKILL.md Import/Export Service ──
        services.AddScoped<ISkillMdService, SkillMdService>();

        // ── Sandbox Instance Repository + Manager ──
        services.AddScoped<ISandboxInstanceRepository, SandboxInstanceRepository>();
        services.AddScoped<IPersistentSandboxManager, PersistentSandboxManager>();
        services.AddHostedService<SandboxAutoStopService>();

        return services;
    }
}
