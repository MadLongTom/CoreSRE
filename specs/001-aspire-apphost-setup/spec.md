# Feature Specification: Aspire AppHost 编排与 ServiceDefaults 配置

**Feature Branch**: `001-aspire-apphost-setup`  
**Created**: 2026-02-09  
**Status**: Draft  
**SPEC-ID**: SPEC-000  
**Priority**: P0（前置基础设施）  
**Input**: User description: "搭建 .NET Aspire AppHost 项目和 ServiceDefaults 项目，将后端 API 通过 AddProject<T>() 编排，统一配置 OpenTelemetry、健康检查、HTTP 弹性策略。Aspire Dashboard 作为开发环境的一站式可观测性面板。通过 Aspire AddPostgres() 编排 PostgreSQL 容器，开发环境零配置。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - 开发者一键启动全栈开发环境 (Priority: P1)

作为一名开发者，我希望通过运行单个 AppHost 项目，就能自动启动后端 API 服务和 PostgreSQL 数据库容器，并打开 Aspire Dashboard，无需手动配置连接字符串、安装数据库或启动多个终端。

**Why this priority**: 这是所有后续开发工作的基础。如果开发者无法一键启动完整的开发环境，所有其他模块的开发效率都会受到影响。零配置体验是 Aspire 的核心价值。

**Independent Test**: 克隆项目后，运行 `dotnet run --project CoreSRE.AppHost`，验证 API 服务和 PostgreSQL 容器均已启动，Aspire Dashboard 可在浏览器中访问。

**Acceptance Scenarios**:

1. **Given** 开发者首次克隆项目且未安装 PostgreSQL, **When** 执行 `dotnet run --project CoreSRE.AppHost`, **Then** Aspire 自动拉取 PostgreSQL 容器镜像、启动容器、创建 `coresre` 数据库，API 服务成功连接数据库并启动
2. **Given** AppHost 正在运行, **When** 开发者打开 Aspire Dashboard URL（控制台中输出的地址）, **Then** Dashboard 显示所有编排的资源（API 服务、PostgreSQL），各资源状态为"Running"
3. **Given** API 服务的连接字符串通过 Aspire 自动注入, **When** API 服务启动, **Then** 无需在 `appsettings.json` 中手动配置 PostgreSQL 连接字符串（由 AppHost 环境变量注入）
4. **Given** AppHost 进程被终止, **When** 开发者重新启动 AppHost, **Then** PostgreSQL 数据库中的数据被保留（使用持久化卷）

---

### User Story 2 - 服务健康检查与依赖等待 (Priority: P1)

作为一名开发者，我希望 API 服务在 PostgreSQL 数据库完全就绪之前不会开始接受请求，并且所有服务都暴露标准的健康检查端点，以便运维工具和 Aspire Dashboard 能够监控服务状态。

**Why this priority**: 没有健康检查和依赖等待，服务启动顺序不确定，会导致启动时数据库连接失败。这是生产级别编排的基本要求。

**Independent Test**: 启动 AppHost，观察 API 服务在 PostgreSQL 就绪之前处于"Waiting"状态，就绪后转为"Running"；访问 `/health` 和 `/alive` 端点返回正常。

**Acceptance Scenarios**:

1. **Given** PostgreSQL 容器正在启动尚未就绪, **When** AppHost 编排 API 服务, **Then** API 服务处于等待状态，不会尝试启动，直到 PostgreSQL 健康检查通过
2. **Given** API 服务已启动并运行, **When** 发送 `GET /health` 请求, **Then** 返回 HTTP 200 和所有检查项的聚合状态（包括数据库连接检查）
3. **Given** API 服务已启动并运行, **When** 发送 `GET /alive` 请求, **Then** 返回 HTTP 200（仅验证进程存活，不包含外部依赖检查）
4. **Given** PostgreSQL 数据库不可达, **When** 发送 `GET /health` 请求, **Then** 返回 HTTP 503（Unhealthy），明确指出 PostgreSQL 检查失败

---

### User Story 3 - 全链路可观测性自动配置 (Priority: P1)

作为一名开发者，我希望所有 API 请求的 Traces、Metrics 和 Logs 自动导出到 Aspire Dashboard，无需手动配置 OpenTelemetry SDK，以便在开发阶段就能快速定位性能瓶颈和错误。

**Why this priority**: 可观测性是后续所有模块（Agent 调用追踪、工作流执行监控、AIOps 告警）的数据基础。尽早建立可观测性管道，后续模块可以直接使用，无需重复配置。

**Independent Test**: 启动 AppHost 后向 API 发送若干请求，打开 Aspire Dashboard 验证 Traces、Metrics、Logs 三类数据均可查看。

**Acceptance Scenarios**:

1. **Given** API 服务通过 ServiceDefaults 启动, **When** 发送任意 HTTP 请求到 API, **Then** Aspire Dashboard 的 Traces 页面显示对应的请求 Span（包含 HTTP 方法、路径、状态码、耗时）
2. **Given** API 服务正在运行, **When** 查看 Aspire Dashboard 的 Metrics 页面, **Then** 可看到 ASP.NET Core HTTP 指标（请求率、延迟分布、错误率）和 .NET 运行时指标（GC、线程池）
3. **Given** API 服务内产生日志输出, **When** 查看 Aspire Dashboard 的 Logs 页面, **Then** 可看到结构化日志（含 Message、Scope、LogLevel），并可按服务名称过滤
4. **Given** 请求发送到 `/health` 或 `/alive` 端点, **When** 查看 Traces 页面, **Then** 这些健康检查请求不会出现在 Traces 中（已被过滤）

---

### User Story 4 - HTTP 客户端弹性策略 (Priority: P2)

作为一名开发者，我希望所有通过 `HttpClient` 发出的外部调用（如调用远程 Agent、MCP Server、外部 API）自动具备重试、超时和熔断策略，避免因外部服务故障导致平台级联崩溃。

**Why this priority**: 弹性策略是生产环境的必要保障，但在开发初期不影响核心功能验证。等到 Tool Gateway (M2) 和 AIOps (M4) 开始调用外部服务时，弹性策略的价值才会显现。

**Independent Test**: 在 API 服务中注入 `IHttpClientFactory` 创建 HttpClient 实例，模拟外部服务不可达，验证自动重试行为和超时保护。

**Acceptance Scenarios**:

1. **Given** API 服务通过 ServiceDefaults 配置了全局 HTTP 弹性策略, **When** 使用 `IHttpClientFactory` 创建 HttpClient 并发送请求到不可达的外部服务, **Then** 请求被 Polly 重试策略自动重试（指数退避+抖动），最终超时返回错误
2. **Given** 外部服务持续返回 5xx 错误, **When** 错误率超过熔断器阈值, **Then** 后续请求被熔断器快速拒绝（不再实际发送），一段时间后半开状态允许探测

---

### Edge Cases

- **Docker 未安装或未运行**: AppHost 启动失败时，应提供清晰的错误信息指引开发者安装 Docker Desktop
- **PostgreSQL 端口冲突**: 如果本机已有 PostgreSQL 占用 5432 端口，Aspire 应使用随机端口分配（默认行为）
- **Aspire Dashboard 端口冲突**: Dashboard 默认端口被占用时，应自动选择其他端口并在控制台输出正确地址
- **AppHost 非正常退出后重启**: PostgreSQL 容器可能仍在运行，AppHost 应优雅处理已存在的容器
- **网络离线环境**: 首次启动需拉取 PostgreSQL Docker 镜像，无网络时应提供明确错误提示

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统 MUST 提供 `CoreSRE.AppHost` 项目，使用 `Aspire.AppHost.Sdk` SDK，通过 `DistributedApplication.CreateBuilder()` 创建编排器
- **FR-002**: AppHost MUST 通过 `AddProject<T>()` 编排后端 API 项目（`CoreSRE`），并通过 `WithHttpHealthCheck("/health")` 配置健康检查探针
- **FR-003**: AppHost MUST 通过 `AddPostgres("postgres")` 创建 PostgreSQL 服务器资源，并通过 `.AddDatabase("coresre")` 创建应用数据库
- **FR-004**: AppHost MUST 通过 `WithReference(db)` 将 PostgreSQL 数据库连接字符串自动注入到 API 服务，通过 `WaitFor(db)` 确保 API 服务在数据库就绪后才启动
- **FR-005**: 系统 MUST 提供 `CoreSRE.ServiceDefaults` 共享项目（`IsAspireSharedProject=true`），封装 `AddServiceDefaults()` 扩展方法
- **FR-006**: `AddServiceDefaults()` MUST 配置 OpenTelemetry — Traces（ASP.NET Core + HttpClient 仪表化，过滤 `/health` 和 `/alive` 端点）、Metrics（ASP.NET Core + HttpClient + Runtime 仪表化）、Logs（含 FormattedMessage 和 Scopes），通过 OTLP 导出到 Aspire Dashboard
- **FR-007**: `AddServiceDefaults()` MUST 配置默认健康检查，通过 `MapDefaultEndpoints()` 映射 `/health`（全量 Readiness 检查）和 `/alive`（仅 Liveness 存活检查，tag 为 `"live"`）
- **FR-008**: `AddServiceDefaults()` MUST 通过 `ConfigureHttpClientDefaults()` 为所有 HttpClient 实例配置 Polly 标准弹性策略（`AddStandardResilienceHandler()`：重试、超时、熔断、并发限制）
- **FR-009**: `AddServiceDefaults()` MUST 配置服务发现（`AddServiceDiscovery()`），使服务间可通过 Aspire 资源名称相互调用
- **FR-010**: API 项目的 `Program.cs` MUST 调用 `builder.AddServiceDefaults()` 注册所有默认基础设施，并调用 `app.MapDefaultEndpoints()` 映射健康检查端点
- **FR-011**: Solution 文件 (`CoreSRE.slnx`) MUST 将 `CoreSRE.AppHost` 和 `CoreSRE.ServiceDefaults` 项目纳入管理

### Key Entities

- **AppHost (CoreSRE.AppHost)**: 分布式应用编排入口，负责声明所有资源（服务、数据库、容器）及其依赖关系。产出为可运行的控制台程序。不含业务逻辑。
- **ServiceDefaults (CoreSRE.ServiceDefaults)**: 共享项目（非独立部署的服务），被所有需要可观测性和弹性能力的服务引用。提供 `AddServiceDefaults()` 和 `MapDefaultEndpoints()` 两个核心扩展方法。
- **PostgreSQL 资源**: 由 Aspire 编排的 PostgreSQL 容器实例，包含一个 `postgres` 服务器资源和一个 `coresre` 数据库子资源。连接字符串由 Aspire 自动生成（含随机密码）并注入到依赖服务。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 开发者从克隆项目到首次成功运行全栈环境（API + PostgreSQL + Dashboard），操作步骤不超过 3 步（clone → restore → run AppHost）
- **SC-002**: API 服务启动后，`GET /health` 在 1 秒内返回 HTTP 200，包含数据库连接检查结果
- **SC-003**: 任意 API 请求的 Trace Span 在 5 秒内出现在 Aspire Dashboard 中
- **SC-004**: API 服务的结构化日志在 Aspire Dashboard 的 Logs 页面中可按服务名过滤查看
- **SC-005**: PostgreSQL 容器启动失败或不可达时，API 服务的 `/health` 端点返回 HTTP 503

## Assumptions

- 开发者的机器上已安装 Docker Desktop（或兼容的容器运行时），因为 Aspire 通过容器运行 PostgreSQL
- 使用 .NET 10 SDK，该版本内置对 Aspire 9.x 的支持
- 开发环境中 Aspire Dashboard 的 OTLP 端点由 AppHost 自动配置，无需手动设置 `OTEL_EXPORTER_OTLP_ENDPOINT` 环境变量
- PostgreSQL 在开发环境中使用 Aspire 自动生成的随机密码，不需要开发者指定固定密码
- 现有 `appsettings.json` 中的 `DefaultConnection` 连接字符串将作为非 Aspire 环境（如直接 `dotnet run` API 项目）的回退配置
- Aspire `AddPostgres()` 自动注册 PostgreSQL 健康检查，`WaitFor()` 利用该健康检查阻塞依赖服务启动
