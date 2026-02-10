using CoreSRE.Application;
using CoreSRE.Endpoints;
using CoreSRE.Infrastructure;
using CoreSRE.Infrastructure.Persistence;
using CoreSRE.Middleware;

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

// ===== 自动迁移数据库（开发环境）=====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();
