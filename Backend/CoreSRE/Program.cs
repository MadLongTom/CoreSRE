using CoreSRE.Application;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Endpoints;
using CoreSRE.Hubs;
using CoreSRE.Infrastructure;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Middleware;
using Microsoft.Agents.AI.Hosting.AGUI;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ===== Aspire ServiceDefaults =====
builder.AddServiceDefaults();

// ===== 服务注册 =====
builder.Services.AddOpenApi();

// JSON 序列化 — 支持枚举字符串 + 数字双模式
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// DDD 分层服务注册
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// AG-UI 协议中间件
builder.Services.AddAGUI();

// SignalR — 工作流执行实时推送
builder.Services.AddSignalR();

// 覆盖 NullWorkflowExecutionNotifier，使用 SignalR 实时推送
builder.Services.AddScoped<IWorkflowExecutionNotifier, SignalRWorkflowNotifier>();

// Incident 实时推送
builder.Services.AddScoped<CoreSRE.Application.Alerts.Interfaces.IIncidentNotifier, SignalRIncidentNotifier>();

// Aspire EF Core 增强（健康检查 + OTel 追踪 + 连接重试）
builder.EnrichNpgsqlDbContext<AppDbContext>();

// MinIO S3 客户端（通过 Aspire Resource Reference 自动注入连接字符串）
builder.AddMinioClient("minio");

// CORS - 允许前端访问
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ===== 中间件管道 =====
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.UseCors();

// ===== API 端点 =====
app.MapAgentEndpoints();
app.MapProviderEndpoints();
app.MapChatEndpoints();
app.MapAgentChatEndpoints();
app.MapToolEndpoints();
app.MapDataSourceEndpoints();
app.MapWebhookEndpoints();
app.MapWorkflowEndpoints();
app.MapFileEndpoints();
app.MapSkillEndpoints();
app.MapSandboxEndpoints();
app.MapAlertRuleEndpoints();
app.MapIncidentEndpoints();

// ===== SignalR Hub =====
app.MapHub<WorkflowHub>("/hubs/workflow");
app.MapHub<IncidentHub>("/hubs/incident");

// ===== 自动迁移数据库（开发环境）=====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // 如果数据库是由 EnsureCreatedAsync 创建的，没有 __EFMigrationsHistory 表。
    // 需要先创建该表并标记已存在的表对应的迁移为已应用，避免 MigrateAsync 重复创建。
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory')";
        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        if (!exists)
        {
            logger.LogInformation("未发现 __EFMigrationsHistory 表，正在从 EnsureCreated 模式迁移到 Migration 模式...");

            // 检测哪些表已经存在（由 EnsureCreated 创建）
            var migrationTableMap = new (string migrationId, string tableName)[]
            {
                ("20260209104609_AddAgentRegistration", "agent_registrations"),
                ("20260210053451_AddAgentSessions", "agent_sessions"),
                ("20260210082547_AddLlmProviders", "llm_providers"),
                ("20260210095051_AddConversations", "conversations"),
            };

            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                """;
            await createCmd.ExecuteNonQueryAsync();

            foreach (var (migrationId, tableName) in migrationTableMap)
            {
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = '{tableName}')";
                var tableExists = (bool)(await checkCmd.ExecuteScalarAsync())!;
                if (tableExists)
                {
                    await using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = $"""
                        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                        VALUES ('{migrationId}', '10.0.2')
                        ON CONFLICT DO NOTHING;
                        """;
                    await insertCmd.ExecuteNonQueryAsync();
                    logger.LogInformation("表 {Table} 已存在，标记迁移 {Migration} 为已应用", tableName, migrationId);
                }
            }

            logger.LogInformation("EnsureCreated → Migration 模式迁移完成");
        }
        else
        {
            // __EFMigrationsHistory 已存在，但旧的兼容逻辑可能错误地标记了某些迁移（对应表实际不存在）。
            // 检测并修复：如果迁移记录存在但对应表不存在，删除该迁移记录让 MigrateAsync 重新执行。
            var migrationTableMap2 = new (string migrationId, string tableName)[]
            {
                ("20260209104609_AddAgentRegistration", "agent_registrations"),
                ("20260210053451_AddAgentSessions", "agent_sessions"),
                ("20260210082547_AddLlmProviders", "llm_providers"),
                ("20260210095051_AddConversations", "conversations"),
            };

            foreach (var (migrationId, tableName) in migrationTableMap2)
            {
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"""
                    SELECT
                        EXISTS (SELECT FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{migrationId}') AS migration_exists,
                        EXISTS (SELECT FROM information_schema.tables WHERE table_name = '{tableName}') AS table_exists
                    """;
                await using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var migrationExists = reader.GetBoolean(0);
                    var tableExists = reader.GetBoolean(1);
                    if (migrationExists && !tableExists)
                    {
                        await reader.CloseAsync();
                        await using var deleteCmd = conn.CreateCommand();
                        deleteCmd.CommandText = $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{migrationId}'";
                        await deleteCmd.ExecuteNonQueryAsync();
                        logger.LogWarning("迁移 {Migration} 已标记为应用但表 {Table} 不存在，已移除记录以重新执行", migrationId, tableName);
                    }
                }
            }
        }
    }

    var pending = await db.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        logger.LogInformation("发现 {Count} 个待执行迁移，正在应用: {Migrations}",
            pending.Count(), string.Join(", ", pending));
        await db.Database.MigrateAsync();
        logger.LogInformation("数据库迁移完成");
    }
    else
    {
        logger.LogInformation("没有待执行的数据库迁移");
    }
}

app.Run();
