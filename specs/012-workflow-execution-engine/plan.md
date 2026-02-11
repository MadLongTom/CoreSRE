# Implementation Plan: 工作流执行引擎（顺序 + 并行 + 条件分支）

**Branch**: `012-workflow-execution-engine` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/012-workflow-execution-engine/spec.md`

## Summary

实现工作流执行引擎，将已发布的 WorkflowDefinition 的 JSON DAG 转换为 Agent Framework 的 `Workflow` 对象并执行。支持三种基础编排模式：顺序（Sequential）、并行（FanOut/FanIn）、条件分支（Conditional Edge）。每次执行创建 `WorkflowExecution` 聚合根记录，实时更新各节点执行状态。提供启动执行、查询执行列表和执行详情三个 API 端点。执行为异步操作，通过 `Channel<T>` + `BackgroundService` 模式在后台运行。

## Technical Context

**Language/Version**: C# / .NET 10.0 (net10.0)  
**Primary Dependencies**: Agent Framework (`Microsoft.Agents.AI.Workflows` 1.0.0-preview.260209.1), MediatR (CQRS), FluentValidation, AutoMapper, EF Core 10.0.2 + Npgsql (PostgreSQL)  
**Storage**: PostgreSQL (via Aspire Npgsql), JSONB for NodeExecutionVO 列表和 DAG 图快照  
**Testing**: xUnit 2.9.3, Moq 4.20, FluentAssertions 8.3 — 无全局 using，每个测试文件需显式 `using Xunit;`  
**Target Platform**: Linux server (ASP.NET Core Minimal API), Aspire AppHost 编排  
**Project Type**: Web application — Clean Architecture 四层 (Domain/Application/Infrastructure/API)  
**Performance Goals**: 执行请求 < 5 秒响应（202 返回），执行列表查询 < 1 秒（100 条记录内）  
**Constraints**: 节点执行超时默认 5 分钟，DAG 图最多 100 节点建议上限  
**Scale/Scope**: 100+ 并发执行实例，每次执行最多 100 个节点

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | Spec-Driven Development | **PASS** | spec.md 完成，23 个 FR，14 个 Acceptance Scenarios，8 个 Edge Cases |
| II | Test-Driven Development | **PASS** | 计划遵循 Red-Green-Refactor：先写测试（domain + application tests），再写实现 |
| III | Domain-Driven Design | **PASS** | WorkflowExecution 为聚合根在 Domain 层；NodeExecutionVO 为值对象；IWorkflowExecutionRepository 接口在 Domain；实现在 Infrastructure；执行引擎服务在 Application（IWorkflowEngine 接口）；API 端点在 API 层 |
| IV | Test Immutability | **PASS** | 不修改任何已有测试 |
| V | Interface-Before-Implementation | **PASS** | IWorkflowExecutionRepository、IWorkflowEngine、IConditionEvaluator 接口先于实现定义 |

## Project Structure

### Documentation (this feature)

```text
specs/012-workflow-execution-engine/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-contract.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE.Domain/
│   ├── Entities/
│   │   └── WorkflowExecution.cs          # 新增 — 聚合根
│   ├── ValueObjects/
│   │   └── NodeExecutionVO.cs            # 新增 — 节点执行值对象
│   ├── Enums/
│   │   ├── ExecutionStatus.cs            # 新增
│   │   └── NodeExecutionStatus.cs        # 新增
│   └── Interfaces/
│       ├── IWorkflowExecutionRepository.cs  # 新增
│       └── IWorkflowEngine.cs               # 新增 — 执行引擎接口
│
├── CoreSRE.Application/
│   ├── Workflows/
│   │   ├── Commands/
│   │   │   └── ExecuteWorkflow/
│   │   │       ├── ExecuteWorkflowCommand.cs
│   │   │       ├── ExecuteWorkflowCommandValidator.cs
│   │   │       └── ExecuteWorkflowCommandHandler.cs
│   │   ├── Queries/
│   │   │   ├── GetWorkflowExecutions/
│   │   │   │   ├── GetWorkflowExecutionsQuery.cs
│   │   │   │   └── GetWorkflowExecutionsQueryHandler.cs
│   │   │   └── GetWorkflowExecutionById/
│   │   │       ├── GetWorkflowExecutionByIdQuery.cs
│   │   │       └── GetWorkflowExecutionByIdQueryHandler.cs
│   │   └── DTOs/
│   │       ├── WorkflowExecutionDto.cs          # 新增
│   │       ├── WorkflowExecutionSummaryDto.cs   # 新增
│   │       └── NodeExecutionDto.cs              # 新增
│   └── Interfaces/
│       └── IConditionEvaluator.cs               # 新增 — 条件表达式求值接口
│
├── CoreSRE.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/
│   │   │   └── WorkflowExecutionConfiguration.cs  # 新增
│   │   └── WorkflowExecutionRepository.cs          # 新增
│   └── Services/
│       ├── WorkflowEngine.cs                       # 新增 — DAG→Workflow 转换 + 执行
│       ├── ConditionEvaluator.cs                   # 新增 — JSON Path 条件求值
│       └── WorkflowExecutionBackgroundService.cs   # 新增 — 后台执行服务
│
├── CoreSRE/
│   └── Endpoints/
│       └── WorkflowEndpoints.cs                    # 修改 — 新增 3 个执行端点
│
├── CoreSRE.Application.Tests/
│   └── Workflows/
│       ├── Commands/ExecuteWorkflow/
│       │   ├── ExecuteWorkflowCommandHandlerTests.cs
│       │   └── ExecuteWorkflowCommandValidatorTests.cs
│       └── Queries/
│           ├── GetWorkflowExecutions/
│           │   └── GetWorkflowExecutionsQueryHandlerTests.cs
│           └── GetWorkflowExecutionById/
│               └── GetWorkflowExecutionByIdQueryHandlerTests.cs
│
└── CoreSRE.Infrastructure.Tests/
    └── Workflows/
        ├── WorkflowExecutionTests.cs              # 聚合根单元测试
        ├── ConditionEvaluatorTests.cs             # 条件求值单元测试
        └── WorkflowEngineTests.cs                 # DAG 转换 + 编排模式测试
```

**Structure Decision**: 延续已有 Clean Architecture 四层结构。新增 `WorkflowExecution` 聚合根及其相关文件。执行引擎核心逻辑（`IWorkflowEngine`）接口定义在 Domain 层，实现在 Infrastructure 层（因为依赖 Agent Framework 外部包）。异步执行采用与 `McpDiscoveryBackgroundService` 相同的 `Channel<T>` + `BackgroundService` 模式。
