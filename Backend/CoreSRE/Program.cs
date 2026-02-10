using CoreSRE.Application;
using CoreSRE.Endpoints;
using CoreSRE.Infrastructure;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ===== Aspire ServiceDefaults =====
builder.AddServiceDefaults();

// ===== 服务注册 =====
builder.Services.AddOpenApi();

// DDD 分层服务注册
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Aspire EF Core 增强（健康检查 + OTel 追踪 + 连接重试）
builder.EnrichNpgsqlDbContext<AppDbContext>();

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

app.UseCors();

// ===== API 端点 =====
app.MapAgentEndpoints();
app.MapProviderEndpoints();

// ===== 自动迁移数据库（开发环境）=====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // 如果数据库是由 EnsureCreatedAsync 创建的，没有 __EFMigrationsHistory 表。
    // 需要先创建该表并标记旧迁移为已应用，再执行新迁移。
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory')";
        var exists = (bool)(await cmd.ExecuteScalarAsync())!;
        if (!exists)
        {
            logger.LogInformation("未发现 __EFMigrationsHistory 表，正在从 EnsureCreated 模式迁移到 Migration 模式...");
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260209104609_AddAgentRegistration', '10.0.2'),
                       ('20260210053451_AddAgentSessions', '10.0.2');
                """;
            await createCmd.ExecuteNonQueryAsync();
            logger.LogInformation("已标记旧迁移为已应用");
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
