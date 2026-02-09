# Data Model: Aspire AppHost 编排与 ServiceDefaults 配置

**Feature**: SPEC-000 | **Branch**: `001-aspire-apphost-setup` | **Date**: 2026-02-09

## Overview

本 Spec 主要涉及 Aspire 编排资源模型和基础设施配置，不引入新的业务领域实体。数据模型描述 Aspire 编排中的资源关系和配置结构。

---

## 1. Aspire 资源模型

### 1.1 Resource Dependency Graph

```
DistributedApplication (AppHost)
│
├── PostgresServerResource ("postgres")
│   ├── Container: docker.io/library/postgres:latest
│   ├── Volume: auto-named data volume
│   ├── Health Check: Npgsql connection test
│   └── PostgresDatabaseResource ("coresre")
│       ├── Parent: postgres server
│       ├── Connection String: auto-generated (Host, Port, Database, Username, Password)
│       └── Health Check: inherited from parent
│
└── ProjectResource ("api") → CoreSRE
    ├── Type: ASP.NET Core Web Application
    ├── Dependencies:
    │   ├── WithReference(coresre) → injects ConnectionStrings__coresre
    │   └── WaitFor(coresre) → blocks until DB healthy
    ├── Health Probe: WithHttpHealthCheck("/health")
    └── Injected Environment Variables:
        ├── ConnectionStrings__coresre = "Host=...;Port=...;Database=coresre;..."
        ├── OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:{port}"
        ├── OTEL_SERVICE_NAME = "api"
        └── ASPNETCORE_URLS = "http://+:{port}"
```

### 1.2 Resource Types

| Resource | Aspire Type | Name | 生命周期 |
|----------|------------|------|----------|
| PostgreSQL Server | `PostgresServerResource` | `"postgres"` | 容器（Docker），AppHost 管理 |
| CoreSRE Database | `PostgresDatabaseResource` | `"coresre"` | 逻辑资源，归属于 Server |
| API Service | `ProjectResource` | `"api"` | 进程（dotnet run），AppHost 管理 |
| Aspire Dashboard | 内置 | N/A | 自动启动，不需要声明 |

---

## 2. 配置结构

### 2.1 连接字符串

| 环境 | 来源 | 键名 | 值 |
|------|------|------|-----|
| Aspire (AppHost) | 环境变量注入 | `ConnectionStrings:coresre` | 自动生成（含随机密码） |
| 非 Aspire (直接运行 API) | `appsettings.json` | `ConnectionStrings:coresre` | `Host=localhost;Port=5432;Database=coresre;Username=postgres;Password=postgres` |

### 2.2 ServiceDefaults 配置层次

```
AddServiceDefaults()
│
├── OpenTelemetry Configuration
│   ├── Logging
│   │   ├── IncludeFormattedMessage: true
│   │   └── IncludeScopes: true
│   ├── Metrics
│   │   ├── ASP.NET Core Instrumentation
│   │   ├── HttpClient Instrumentation
│   │   └── Runtime Instrumentation
│   ├── Tracing
│   │   ├── ASP.NET Core Instrumentation (filter: exclude /health, /alive)
│   │   ├── HttpClient Instrumentation
│   │   └── Custom Source: ApplicationName
│   └── Exporter: OTLP (conditional on OTEL_EXPORTER_OTLP_ENDPOINT)
│
├── Health Checks
│   ├── Self Check (tag: "live") → HealthCheckResult.Healthy()
│   └── DbContext Check (via EnrichNpgsqlDbContext) → EF Core connectivity
│
├── Service Discovery
│   └── AddServiceDiscovery()
│
└── HTTP Client Defaults
    ├── AddStandardResilienceHandler()
    │   ├── Retry Policy (exponential backoff + jitter)
    │   ├── Timeout Policy
    │   ├── Circuit Breaker
    │   └── Rate Limiter
    └── AddServiceDiscovery()
```

### 2.3 健康检查端点

| 端点 | 路径 | 用途 | 包含的检查项 | HTTP 响应 |
|------|------|------|-------------|-----------|
| Readiness | `/health` | 全量健康检查 | Self + DbContext + 所有注册检查 | 200 (Healthy) / 503 (Unhealthy) |
| Liveness | `/alive` | 进程存活检查 | 仅 tag="live" 的检查 (Self) | 200 (Healthy) / 503 (Unhealthy) |

---

## 3. 项目依赖关系

### 3.1 项目引用图

```
CoreSRE.AppHost (Aspire.AppHost.Sdk/13.1.0)
├── ProjectReference → CoreSRE (API)
└── PackageReference → Aspire.Hosting.PostgreSQL 13.1.0

CoreSRE.ServiceDefaults (IsAspireSharedProject=true)
├── FrameworkReference → Microsoft.AspNetCore.App
├── PackageReference → Microsoft.Extensions.Http.Resilience 10.2.0
├── PackageReference → Microsoft.Extensions.ServiceDiscovery 10.2.0
├── PackageReference → OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0
├── PackageReference → OpenTelemetry.Extensions.Hosting 1.15.0
├── PackageReference → OpenTelemetry.Instrumentation.AspNetCore 1.15.0
├── PackageReference → OpenTelemetry.Instrumentation.Http 1.15.0
└── PackageReference → OpenTelemetry.Instrumentation.Runtime 1.15.0

CoreSRE (API - net10.0 Web)
├── ProjectReference → CoreSRE.Application
├── ProjectReference → CoreSRE.Infrastructure
├── ProjectReference → CoreSRE.ServiceDefaults [NEW]
├── PackageReference → Microsoft.AspNetCore.OpenApi 10.0.2
└── PackageReference → Aspire.Npgsql.EntityFrameworkCore.PostgreSQL 13.1.0 [NEW]

CoreSRE.Application → CoreSRE.Domain (unchanged)
CoreSRE.Infrastructure → CoreSRE.Application (unchanged)
CoreSRE.Domain (no dependencies, unchanged)
```

### 3.2 DDD 分层合规性

| 项目 | DDD 层 | 变更 | 合规性 |
|------|--------|------|--------|
| CoreSRE.Domain | Domain | 无变更 | ✅ 零外部依赖 |
| CoreSRE.Application | Application | 无变更 | ✅ 不引用 Infrastructure |
| CoreSRE.Infrastructure | Infrastructure | 连接字符串键名变更 | ✅ 仅配置变更 |
| CoreSRE (API) | API | 添加 ServiceDefaults 引用 | ✅ API 引用横切关注点 |
| CoreSRE.AppHost | 编排器（不属于 DDD 层） | 新建 | ✅ 独立于 DDD 架构 |
| CoreSRE.ServiceDefaults | 横切关注点（不属于 DDD 层） | 新建 | ✅ 共享基础设施 |

---

## 4. 状态转换

### 4.1 AppHost 资源状态机

```
[NotStarted] → [Starting] → [Running] → [Stopping] → [Stopped]
                    ↓              ↓
               [Waiting]      [Unhealthy]
                    ↓              ↓
               [Running]     [Stopping]
```

### 4.2 API 服务启动序列

```
1. AppHost 启动
2. PostgreSQL 容器创建 → [Starting]
3. PostgreSQL 健康检查轮询
4. PostgreSQL 通过健康检查 → [Running]
5. API 服务解除阻塞（WaitFor satisfied）→ [Starting]
6. API 服务加载 ServiceDefaults → OTel + Health Checks + Resilience
7. API 服务连接数据库（EF Core）
8. API 服务 HTTP 健康检查通过 → [Running]
9. Aspire Dashboard 显示所有资源 Running
```

---

## 5. Validation Rules

| 规则 | 验证时机 | 失败处理 |
|------|----------|----------|
| PostgreSQL 连接字符串非空 | API 启动时 | 启动失败，抛出配置异常 |
| Docker 运行时可用 | AppHost 启动时 | 清晰错误信息，指引安装 Docker |
| PostgreSQL 端口可用 | 容器启动时 | Aspire 自动分配随机端口 |
| OTLP 端点可达 | OTel 导出时 | 降级处理，不影响 API 正常运行 |
| `/health` 端点 200 | AppHost 探针轮询 | API 资源显示 Unhealthy |
