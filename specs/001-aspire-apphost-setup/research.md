# Research: Aspire AppHost 编排与 ServiceDefaults 配置

**Feature**: SPEC-000 | **Branch**: `001-aspire-apphost-setup` | **Date**: 2026-02-09

## Research Tasks

本文档记录了 Phase 0 阶段所有技术选型研究，解决 Technical Context 中的待澄清问题和依赖项最佳实践。

---

## R1: Aspire SDK 与 NuGet 包版本矩阵

### Decision
使用 Aspire 13.1.0 生态系统（最新稳定版），搭配 .NET 10.0 SDK。

### Version Matrix

| 组件 | 包名 | 版本 |
|------|------|------|
| AppHost SDK | `Aspire.AppHost.Sdk` | `13.1.0` |
| PostgreSQL Hosting | `Aspire.Hosting.PostgreSQL` | `13.1.0` |
| EF Core PostgreSQL 组件 | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` | `13.1.0` |
| HTTP 弹性策略 | `Microsoft.Extensions.Http.Resilience` | `10.2.0` |
| 服务发现 | `Microsoft.Extensions.ServiceDiscovery` | `10.2.0` |
| OTel OTLP 导出器 | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.15.0` |
| OTel 宿主集成 | `OpenTelemetry.Extensions.Hosting` | `1.15.0` |
| OTel ASP.NET Core | `OpenTelemetry.Instrumentation.AspNetCore` | `1.15.0` |
| OTel HttpClient | `OpenTelemetry.Instrumentation.Http` | `1.15.0` |
| OTel Runtime | `OpenTelemetry.Instrumentation.Runtime` | `1.15.0` |

### Rationale
- Aspire 13.1.0 是 NuGet.org 上的最新稳定版本，与 .NET 10.0 完全兼容
- OpenTelemetry 1.15.0 是最新稳定版（参考仓库使用 1.14.0，NuGet 已更新到 1.15.0）
- Microsoft.Extensions.* 10.2.0 与 .NET 10 SDK 版本对齐

### Alternatives Considered
- Aspire 9.x（旧版本，.NET 10 兼容性不如 13.x）→ 已弃用
- 手动配置 OTel 而非 Aspire ServiceDefaults → 配置复杂度高，不符合 Aspire 约定

---

## R2: 连接字符串注入机制

### Decision
将 Infrastructure DI 中的连接字符串名称从 `"DefaultConnection"` 改为 `"coresre"`，与 Aspire 资源名称对齐。

### Rationale
- Aspire `AddPostgres("postgres").AddDatabase("coresre")` 创建名为 `"coresre"` 的数据库资源
- `WithReference(db)` 默认注入环境变量 `ConnectionStrings__coresre`（Aspire 使用资源名称作为连接字符串键名）
- .NET Configuration 系统将 `ConnectionStrings__coresre` 映射为 `ConnectionStrings:coresre`
- `builder.Configuration.GetConnectionString("coresre")` 可自动获取注入的连接字符串
- `appsettings.json` 保留 `ConnectionStrings:coresre` 作为非 Aspire 环境的回退配置

### Alternatives Considered
- **使用 `WithReference(db, connectionName: "DefaultConnection")`** 保持现有代码不变 → 可行但违背 Aspire 约定，增加理解成本
- **双连接字符串键名** → 增加配置复杂度，不推荐

### Migration Impact
- 修改 `CoreSRE.Infrastructure/DependencyInjection.cs`：`GetConnectionString("DefaultConnection")` → `GetConnectionString("coresre")`
- 修改 `appsettings.json`：`"DefaultConnection"` → `"coresre"`
- 修改 `appsettings.Development.json`：同步更新键名

---

## R3: EF Core 集成策略（DDD 分层保持）

### Decision
在 API 项目中使用 `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 的 `EnrichNpgsqlDbContext<AppDbContext>()` 方法，叠加在 Infrastructure 的 `UseNpgsql()` 注册之上，保持 DDD 分层。

### Rationale
`Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 提供两种使用方式：

| 方式 | 方法 | 适用场景 |
|------|------|----------|
| 完整注册 | `builder.AddNpgsqlDbContext<T>("coresre")` | 简单项目，替换所有 DbContext 注册 |
| 增强叠加 | `builder.EnrichNpgsqlDbContext<T>()` | DDD 分层项目，在已有注册上叠加能力 |

**选择 `EnrichNpgsqlDbContext`** 的原因：
- Infrastructure 层的 `AddInfrastructure()` 已负责 DbContext 注册（`UseNpgsql()`）和仓储注入
- `EnrichNpgsqlDbContext` 在已有注册上叠加：EF 连接重试、DbContext 健康检查、OTel 追踪/指标
- 不会替换 Infrastructure 的 DI 配置，保持 DDD 分层一致性
- API 项目只需添加一行增强调用

### Auto-configured by EnrichNpgsqlDbContext
| 能力 | 默认状态 | 配置键 |
|------|----------|--------|
| EF 连接重试 (`EnableRetryOnFailure`) | ✅ 启用 | `DisableRetry` |
| DbContext 健康检查 (`DbContextHealthCheck`) | ✅ 启用 | `DisableHealthChecks` |
| OTel 追踪 (`Npgsql` tracer) | ✅ 启用 | `DisableTracing` |
| OTel 指标 | ✅ 启用 | `DisableMetrics` |

### Alternatives Considered
- **`AddNpgsqlDbContext<T>()`**（完整注册）→ 会替换 Infrastructure 的 DI，破坏 DDD 分层
- **不使用 Aspire EF 组件** → 需手动配置健康检查和 OTel 追踪，重复造轮子

---

## R4: WaitFor 行为与健康检查

### Decision
使用 `WaitFor(db)` 阻塞 API 服务启动直到 PostgreSQL 数据库健康。

### Rationale
- `WaitFor()` 添加一个 `HealthCheckAnnotation` 到依赖资源
- AppHost **阻塞依赖资源的启动**（API 不会被启动），直到 PostgreSQL 通过健康检查
- `AddPostgres()` 自动注册 Npgsql 健康检查（打开连接并执行查询）
- `AddDatabase()` 也注册其自身的健康检查
- 对数据库子资源调用 `WaitFor(db)` 时，Aspire 自动链式等待父服务器资源

### Technical Details
- `WaitFor()` 设置 `WaitType.WaitUntilHealthy`
- 替代方案 `WaitForCompletion()` 等待资源运行完成（不适用于持续运行的数据库）
- Dashboard 中 API 服务显示"Waiting"状态，PostgreSQL 就绪后转为"Starting" → "Running"

---

## R5: PostgreSQL 数据持久化

### Decision
显式调用 `.WithDataVolume()` 确保开发数据在 AppHost 重启后保留。

### Rationale
- `AddPostgres()` **不自动创建 Docker Volume** — 默认使用容器临时文件系统
- 不调用 `.WithDataVolume()` → AppHost 每次重启都会丢失所有数据
- `.WithDataVolume()` 创建 Docker 命名卷（名称基于 App + Resource 自动生成）
- 命名卷映射到 `/var/lib/postgresql/data`，数据在容器重建后保留
- 对于开发环境，数据持久化是必要的（避免反复 seed 数据）

### Alternatives Considered
- **`.WithDataBindMount(source)`** — 绑定挂载主机目录 → 跨平台兼容性差，不推荐用于开发
- **不持久化** → 每次重启丢失数据，开发体验差

---

## R6: ServiceDefaults 模式最佳实践

### Decision
采用 Aspire 官方模板的 ServiceDefaults 模式，包含 4 个核心扩展方法。

### Pattern Structure

```
Extensions.cs
├── AddServiceDefaults<TBuilder>()      # 入口方法，聚合所有配置
│   ├── ConfigureOpenTelemetry()        # OTel Traces + Metrics + Logs
│   ├── AddDefaultHealthChecks()        # /health + /alive
│   ├── AddServiceDiscovery()           # 服务发现
│   └── ConfigureHttpClientDefaults()   # Polly 弹性 + 服务发现
└── MapDefaultEndpoints()               # 映射健康检查端点（中间件侧）
```

### Key Implementation Details

1. **OpenTelemetry 配置**:
   - Logs: `IncludeFormattedMessage = true`, `IncludeScopes = true`
   - Metrics: ASP.NET Core + HttpClient + Runtime instrumentation
   - Traces: ASP.NET Core + HttpClient instrumentation，**过滤 `/health` 和 `/alive` 路径**
   - OTLP 导出：通过 `OTEL_EXPORTER_OTLP_ENDPOINT` 环境变量配置（Aspire 自动注入）

2. **健康检查**:
   - `/health` — Readiness 检查，包含所有注册的健康检查（含数据库连接）
   - `/alive` — Liveness 检查，仅包含标记为 `"live"` 的检查（self check）
   - 健康检查端点仅在 Development 环境映射（`MapDefaultEndpoints` 检查 `IsDevelopment()`）

3. **HTTP 弹性策略**:
   - `AddStandardResilienceHandler()` 包含：重试（指数退避+抖动）、超时、熔断、速率限制
   - 通过 `ConfigureHttpClientDefaults()` 应用到所有 `IHttpClientFactory` 创建的实例

4. **泛型约束**:
   - 使用 `TBuilder : IHostApplicationBuilder` 泛型约束，兼容 `WebApplicationBuilder` 和 `HostApplicationBuilder`

### Rationale
直接复用 Aspire 官方模板模式，因为：
- 社区广泛采用，文档丰富
- 与 Aspire Dashboard 完美集成
- 模板经过生产验证
- 后续新增服务只需调用 `AddServiceDefaults()` 即可获得全部能力

---

## R7: API Program.cs 改造策略

### Decision
保留现有 DDD DI 注册，在其前后添加 Aspire ServiceDefaults 调用。

### Rationale
现有 `Program.cs` 结构：
```
builder.Services.AddOpenApi();
builder.Services.AddApplication();        // DDD Application 层
builder.Services.AddInfrastructure(...);   // DDD Infrastructure 层
builder.Services.AddCors(...);
```

改造后：
```
builder.AddServiceDefaults();             // [NEW] Aspire 默认基础设施
builder.Services.AddOpenApi();
builder.Services.AddApplication();        // DDD Application 层（保持不变）
builder.Services.AddInfrastructure(...);   // DDD Infrastructure 层（保持不变）
builder.EnrichNpgsqlDbContext<AppDbContext>(); // [NEW] Aspire EF 增强
builder.Services.AddCors(...);

// ... 中间件管道 ...

app.MapDefaultEndpoints();                // [NEW] 健康检查端点
```

### 需要移除的代码
- 现有的手动 `/api/health` 端点（由 `MapDefaultEndpoints()` 替代）
- 现有的 `EnsureCreatedAsync()` 自动迁移逻辑需保留（Aspire 不管理 schema）

### Alternatives Considered
- 完全重写 Program.cs → 风险高，改动面大
- 保留手动 `/api/health` 端点 → 与 `MapDefaultEndpoints()` 冲突，路径重复

---

## R8: Solution 文件更新

### Decision
在 `CoreSRE.slnx` 中添加 AppHost 和 ServiceDefaults 项目到独立的 `/aspire/` 文件夹。

### Rationale
现有 solution 结构：
```xml
<Folder Name="/src/">
  <Project Path="CoreSRE.csproj" />
  <Project Path="..\CoreSRE.Domain\CoreSRE.Domain.csproj" />
  <Project Path="..\CoreSRE.Application\CoreSRE.Application.csproj" />
  <Project Path="..\CoreSRE.Infrastructure\CoreSRE.Infrastructure.csproj" />
</Folder>
```

新增：
```xml
<Folder Name="/aspire/">
  <Project Path="..\CoreSRE.AppHost\CoreSRE.AppHost.csproj" />
  <Project Path="..\CoreSRE.ServiceDefaults\CoreSRE.ServiceDefaults.csproj" />
</Folder>
```

### Rationale
- AppHost 和 ServiceDefaults 不属于 DDD 任何层（`/src/`），归入 `/aspire/` 文件夹更清晰
- 保持现有 `/src/` 文件夹结构不变

---

## Summary: All NEEDS CLARIFICATION Resolved

| # | 问题 | 决策 | 状态 |
|---|------|------|------|
| R1 | Aspire SDK 版本 | 13.1.0 | ✅ Resolved |
| R2 | 连接字符串键名 | `"coresre"` 对齐 Aspire 资源名 | ✅ Resolved |
| R3 | EF Core 集成策略 | `EnrichNpgsqlDbContext` 保持 DDD | ✅ Resolved |
| R4 | WaitFor 行为 | 阻塞 API 启动直到 DB 健康 | ✅ Resolved |
| R5 | 数据持久化 | 显式 `.WithDataVolume()` | ✅ Resolved |
| R6 | ServiceDefaults 模式 | 官方模板 4 方法模式 | ✅ Resolved |
| R7 | Program.cs 改造 | 保留 DDD DI + 添加 Aspire 调用 | ✅ Resolved |
| R8 | Solution 结构 | `/aspire/` 文件夹 | ✅ Resolved |
