# Implementation Plan: Tool Gateway — 工具注册、管理与统一调用

**Branch**: `009-tool-gateway-crud` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/009-tool-gateway-crud/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

实现 Tool Gateway 模块的核心功能（SPEC-010~013）：REST API 工具注册与管理、MCP Server 工具注册与自动发现、OpenAPI 文档批量导入生成工具节点、统一工具调用代理。采用 DDD 分层架构：Domain 层定义 `ToolRegistration` 聚合根（含 ConnectionConfigVO、AuthConfigVO、ToolSchemaVO 值对象）和 `McpToolItem` 实体（MCP 子工具项）；Application 层通过 MediatR CQRS 实现命令/查询处理，FluentValidation 按工具类型条件校验，后台任务异步执行 MCP 握手与 Tool 发现；Infrastructure 层使用 EF Core `ToJson()` 存储复杂值对象为 PostgreSQL JSONB 列，ASP.NET Core Data Protection API 加密凭据，MCP 客户端连接管理和 REST HTTP 代理调用；API 层通过 Minimal API `MapGroup` 暴露 RESTful 端点。

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0, Microsoft.OpenApi (OpenAPI 文档解析), Microsoft.AspNetCore.DataProtection (凭据加密)  
**Storage**: PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB value objects  
**Testing**: xUnit + FluentAssertions + Moq (Domain/Application unit tests), WebApplicationFactory (API integration tests)  
**Target Platform**: Linux container (via Aspire AppHost), development on Windows  
**Project Type**: Web — DDD 4-layer backend (Domain → Application → Infrastructure → API)  
**Performance Goals**: 工具注册 < 1 秒响应 (不含 MCP 异步握手), OpenAPI 导入 < 10 秒 (≤50 接口), 列表查询 < 500ms (≤200 条)  
**Constraints**: Domain 层零外部包依赖, 值对象不可变, 聚合根通过工厂方法创建, 凭据必须加密存储  
**Scale/Scope**: 初期 < 200 工具注册, 无分页需求; MCP Server 子工具项 < 50/Server

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. SDD | ✅ PASS | spec.md 已存在，20 条 FR，8 条 SC，6 个 User Story 含 23 条 Acceptance Scenarios |
| II. TDD | ✅ PASS | Plan 产出后，实现阶段将遵循 Red→Green→Refactor。Domain 95%, Application 90%, Infrastructure 80%, API 80% |
| III. DDD | ✅ PASS | ToolRegistration 聚合根 + McpToolItem 实体在 Domain 层，工厂方法创建，值对象不可变。CQRS 在 Application 层，EF Core + MCP/HTTP 客户端在 Infrastructure 层，Minimal API 在 API 层 |
| IV. Test Immutability | ✅ PASS | 测试将从 spec 的 acceptance scenarios 推导，提交后锁定断言 |
| V. Interface-Before-Implementation | ✅ PASS | IToolRegistrationRepository、IToolInvoker、IMcpToolDiscoveryService、ICredentialEncryptionService 定义在 Domain/Application 的 Interfaces 目录 |

### Post-Design Check

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Spec-Driven Development | ✅ PASS | data-model.md 中所有实体和 VO 直接来源于 spec.md FR-001~FR-020；contracts/tools-api.yaml 覆盖全部 6 条 User Story 的端点；研究决策 R1-R10 均有 spec 追溯 |
| 2 | Test-Driven Development | ✅ PASS | Project Structure 中 Tests 文件列出所有测试文件，与 Domain/Application/Infrastructure 源文件一一对应；quickstart.md 包含全部端点的 curl 示例可用于验收 |
| 3 | Domain-Driven Design | ✅ PASS | ToolRegistration 为聚合根（factory methods + domain guards）；McpToolItem 为子实体；ConnectionConfigVO/AuthConfigVO/ToolSchemaVO 为 Value Objects；4 枚举覆盖所有领域概念；仓储接口在 Domain 层 |
| 4 | Test Immutability | ✅ PASS | 无已有测试需要修改；所有测试文件均为新增 |
| 5 | Interface-Before-Implementation | ✅ PASS | contracts/tools-api.yaml 定义完整 API 契约在先；data-model.md 定义 Domain Interface (IToolRegistrationRepository, IToolInvoker, IToolInvokerFactory) 在先；实现类 (RestApiToolInvoker, McpServerToolInvoker) 需先编写接口 |

## Project Structure

### Documentation (this feature)

```text
specs/009-tool-gateway-crud/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology research
├── data-model.md        # Phase 1 output — entity/VO definitions
├── quickstart.md        # Phase 1 output — developer quick start
├── contracts/           # Phase 1 output — API contracts
│   └── tools-api.yaml   # OpenAPI 3.0 spec for Tool Gateway endpoints
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── Entities/
│   │   ├── BaseEntity.cs                    # existing
│   │   ├── ToolRegistration.cs              # NEW — aggregate root (RestApi/McpServer)
│   │   └── McpToolItem.cs                   # NEW — MCP Server 发现的子工具项
│   ├── Enums/
│   │   ├── ToolType.cs                      # NEW — RestApi / McpServer
│   │   ├── ToolStatus.cs                    # NEW — Active / Inactive / CircuitOpen
│   │   ├── AuthType.cs                      # NEW — None / ApiKey / Bearer / OAuth2
│   │   └── TransportType.cs                 # NEW — Rest / StreamableHttp / Stdio
│   ├── ValueObjects/
│   │   ├── ConnectionConfigVO.cs            # NEW — Endpoint, Protocol, TransportType
│   │   ├── AuthConfigVO.cs                  # NEW — AuthType, EncryptedCredential
│   │   └── ToolSchemaVO.cs                  # NEW — InputSchema, OutputSchema, Annotations
│   └── Interfaces/
│       ├── IRepository.cs                   # existing
│       ├── IUnitOfWork.cs                   # existing
│       ├── IToolRegistrationRepository.cs   # NEW — extends IRepository<ToolRegistration>
│       └── IMcpToolItemRepository.cs        # NEW — extends IRepository<McpToolItem>
│
├── CoreSRE.Application/
│   ├── Interfaces/
│   │   ├── ICredentialEncryptionService.cs  # NEW — 凭据加密/解密/掩码
│   │   ├── IMcpToolDiscoveryService.cs      # NEW — MCP 握手 + tools/list 发现
│   │   ├── IToolInvoker.cs                  # NEW — 统一工具调用接口
│   │   └── IOpenApiParserService.cs         # NEW — OpenAPI 文档解析
│   └── Tools/
│       ├── DTOs/
│       │   ├── ToolRegistrationDto.cs       # NEW
│       │   ├── ToolSummaryDto.cs            # NEW
│       │   ├── McpToolItemDto.cs            # NEW
│       │   ├── ToolInvocationResultDto.cs   # NEW
│       │   ├── OpenApiImportResultDto.cs    # NEW
│       │   └── ToolMappingProfile.cs        # NEW — AutoMapper
│       ├── Commands/
│       │   ├── RegisterTool/
│       │   │   ├── RegisterToolCommand.cs
│       │   │   ├── RegisterToolCommandHandler.cs
│       │   │   └── RegisterToolCommandValidator.cs
│       │   ├── UpdateTool/
│       │   │   ├── UpdateToolCommand.cs
│       │   │   ├── UpdateToolCommandHandler.cs
│       │   │   └── UpdateToolCommandValidator.cs
│       │   ├── DeleteTool/
│       │   │   ├── DeleteToolCommand.cs
│       │   │   └── DeleteToolCommandHandler.cs
│       │   ├── ImportOpenApi/
│       │   │   ├── ImportOpenApiCommand.cs
│       │   │   ├── ImportOpenApiCommandHandler.cs
│       │   │   └── ImportOpenApiCommandValidator.cs
│       │   └── InvokeTool/
│       │       ├── InvokeToolCommand.cs
│       │       ├── InvokeToolCommandHandler.cs
│       │       └── InvokeToolCommandValidator.cs
│       └── Queries/
│           ├── GetTools/
│           │   ├── GetToolsQuery.cs
│           │   └── GetToolsQueryHandler.cs
│           ├── GetToolById/
│           │   ├── GetToolByIdQuery.cs
│           │   └── GetToolByIdQueryHandler.cs
│           └── GetMcpTools/
│               ├── GetMcpToolsQuery.cs
│               └── GetMcpToolsQueryHandler.cs
│
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs                        # MODIFIED — add DbSet<ToolRegistration>, DbSet<McpToolItem>
│   │   ├── ToolRegistrationRepository.cs          # NEW
│   │   ├── McpToolItemRepository.cs               # NEW
│   │   └── Configurations/
│   │       ├── ToolRegistrationConfiguration.cs   # NEW — EF Core ToJson() + JSONB mapping
│   │       └── McpToolItemConfiguration.cs        # NEW
│   ├── Services/
│   │   ├── CredentialEncryptionService.cs         # NEW — AES via DataProtection API
│   │   ├── McpToolDiscoveryService.cs             # NEW — MCP initialize + tools/list
│   │   ├── RestApiToolInvoker.cs                  # NEW — HTTP 调用 + 认证注入
│   │   ├── McpToolInvoker.cs                      # NEW — MCP tools/call
│   │   ├── ToolInvokerFactory.cs                  # NEW — 按 ToolType 选择 Invoker
│   │   └── OpenApiParserService.cs                # NEW — Microsoft.OpenApi 解析
│   └── DependencyInjection.cs                     # MODIFIED — register Tool repos + services
│
├── CoreSRE/                                       # API layer
│   ├── Endpoints/
│   │   └── ToolEndpoints.cs                       # NEW — MapGroup + handlers
│   └── Program.cs                                 # MODIFIED — app.MapToolEndpoints()
│
├── CoreSRE.Application.Tests/
│   └── Tools/
│       ├── Commands/
│       │   ├── RegisterToolCommandHandlerTests.cs    # NEW
│       │   ├── UpdateToolCommandHandlerTests.cs      # NEW
│       │   ├── DeleteToolCommandHandlerTests.cs      # NEW
│       │   ├── ImportOpenApiCommandHandlerTests.cs   # NEW
│       │   └── InvokeToolCommandHandlerTests.cs      # NEW
│       └── Queries/
│           ├── GetToolsQueryHandlerTests.cs          # NEW
│           ├── GetToolByIdQueryHandlerTests.cs       # NEW
│           └── GetMcpToolsQueryHandlerTests.cs       # NEW
│
└── CoreSRE.Infrastructure.Tests/
    └── Services/
        ├── CredentialEncryptionServiceTests.cs        # NEW
        ├── McpToolDiscoveryServiceTests.cs            # NEW
        ├── OpenApiParserServiceTests.cs               # NEW
        ├── RestApiToolInvokerTests.cs                 # NEW
        └── McpToolInvokerTests.cs                     # NEW
```

**Structure Decision**: 遵循现有 DDD 4 层架构，新增文件全部放入对应层的约定目录。Application 层采用 Vertical Slice 组织（`Tools/Commands/Queries` 分目录）。McpToolItem 作为独立实体（非 ToolRegistration 内嵌）以支持独立查询和级联删除。统一调用通过 `IToolInvoker` 接口 + `ToolInvokerFactory` 策略模式实现协议适配。

## Complexity Tracking

> No constitution violations. Complexity is higher than SPEC-002 (Agent CRUD) due to:
> 1. **Credential encryption** — requires Infrastructure-layer encryption service, must never leak to Domain
> 2. **Async MCP discovery** — BackgroundService / IHostedService pattern for non-blocking tool discovery
> 3. **Dual-protocol invocation** — Strategy pattern (IToolInvoker) with REST and MCP implementations
> 4. **OpenAPI parsing** — External library (Microsoft.OpenApi) integration in Infrastructure layer
> 
> All complexity is isolated in Infrastructure layer per DDD constraints. Domain layer remains zero-dependency.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
