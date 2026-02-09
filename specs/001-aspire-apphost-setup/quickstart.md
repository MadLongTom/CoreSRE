# Quickstart: Aspire AppHost 一键启动

**Feature**: SPEC-000 | **Branch**: `001-aspire-apphost-setup` | **Date**: 2026-02-09

## 前置条件

| 要求 | 版本 | 检查命令 |
|------|------|----------|
| .NET SDK | 10.0+ | `dotnet --version` |
| Docker Desktop | 最新版 | `docker --version` |
| Docker 运行中 | N/A | `docker info`（无错误输出） |

> ⚠️ **Docker 必须运行**。Aspire 通过 Docker 容器运行 PostgreSQL，未安装或未启动 Docker 将导致 AppHost 启动失败。

---

## 3 步启动

### Step 1: 克隆项目

```bash
git clone <repo-url> CoreSRE
cd CoreSRE
```

### Step 2: 还原依赖

```bash
dotnet restore Backend/CoreSRE/CoreSRE.slnx
```

### Step 3: 运行 AppHost

```bash
dotnet run --project Backend/CoreSRE.AppHost
```

控制台将输出类似信息：

```
info: Aspire.Hosting.DistributedApplication[0]
      Aspire version: 13.1.0
info: Aspire.Hosting.DistributedApplication[0]
      Distributed application starting.
info: Aspire.Hosting.DistributedApplication[0]
      Dashboard is available at: https://localhost:17225/login?t=<token>
info: Aspire.Hosting.DistributedApplication[0]
      Resource "postgres" started.
info: Aspire.Hosting.DistributedApplication[0]
      Resource "coresre" started.
info: Aspire.Hosting.DistributedApplication[0]
      Resource "api" started.
```

---

## 验证

### 1. Aspire Dashboard

打开控制台输出的 Dashboard URL（如 `https://localhost:17225`），你将看到：

| 资源名 | 类型 | 状态 |
|--------|------|------|
| postgres | Container | Running |
| coresre | Database | Running |
| api | Project | Running |

### 2. 健康检查

```bash
# Readiness（包含数据库检查）
curl http://localhost:{api-port}/health
# 预期: HTTP 200, {"status":"Healthy",...}

# Liveness（仅进程存活）
curl http://localhost:{api-port}/alive
# 预期: HTTP 200, {"status":"Healthy",...}
```

> 💡 `{api-port}` 是 Aspire 分配的端口，在 Dashboard 的 "api" 资源行中可看到。

### 3. OpenTelemetry

1. 向 API 发送任意请求（如 `curl http://localhost:{api-port}/api/health`）
2. 打开 Dashboard → **Traces** 页面 → 可看到请求 Span
3. 打开 Dashboard → **Metrics** 页面 → 可看到 ASP.NET Core HTTP 指标
4. 打开 Dashboard → **Logs** 页面 → 可看到结构化日志

### 4. 非 Aspire 启动（回退模式）

如不使用 Aspire 编排，可直接运行 API 项目（需要本地 PostgreSQL）：

```bash
dotnet run --project Backend/CoreSRE
```

此模式使用 `appsettings.json` 中的 `ConnectionStrings:coresre` 连接本地 PostgreSQL。

---

## 常见问题

### Docker 未安装

```
Unable to connect to Docker. Ensure Docker Desktop is installed and running.
```

**解决**: 安装 [Docker Desktop](https://www.docker.com/products/docker-desktop/) 并确保已启动。

### 端口冲突

```
Port 5432 is already in use.
```

**解决**: Aspire 默认使用随机端口分配，此错误通常不会出现。如遇到，停止占用 5432 端口的进程。

### Dashboard 无法访问

**解决**: 检查控制台输出的 Dashboard URL 和登录 token。Dashboard 使用 HTTPS，浏览器可能提示证书警告。

### 首次启动慢

**原因**: 首次运行需拉取 PostgreSQL Docker 镜像（约 400MB）。后续启动将使用本地缓存。

---

## 项目结构速览

```
Backend/
├── CoreSRE.AppHost/         ← 运行这个！编排入口
│   └── Program.cs           ← 声明 PostgreSQL + API 编排
├── CoreSRE.ServiceDefaults/ ← 共享基础设施配置
│   └── Extensions.cs        ← OTel + Health + Resilience
├── CoreSRE/                 ← API 服务
├── CoreSRE.Domain/          ← 领域层
├── CoreSRE.Application/     ← 应用层
└── CoreSRE.Infrastructure/  ← 基础设施层
```
