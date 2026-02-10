# Implementation Plan: 单 Agent 对话界面

**Branch**: `007-agent-chat-ui` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-agent-chat-ui/spec.md`

## Summary

实现一个单 Agent 对话界面，允许用户选择已注册的 Agent 发起对话、查看和恢复历史对话记录、以流式方式展示 Agent 回复。前后端数据传输采用 **AG-UI 协议**（事件驱动的 SSE 流式协议）：后端使用 `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`（`AddAGUI` / `MapAGUI`）将 `ChatClientAgent` 的流式响应自动转换为 AG-UI 事件流；前端使用 `@ag-ui/client` 的 `HttpAgent` + subscriber 模式消费事件，通过自定义 React 组件渲染对话 UI（不依赖 CopilotKit）。对话元数据使用新增 `Conversation` 领域实体，聊天历史复用已有的 `AgentSessionRecord`（SPEC-004），通过 `SessionData` JSONB 中的 `ChatHistoryProviderState.Messages[]` 读取消息记录，不再创建自定义 `ChatMessage` 实体。

## Technical Context

**Language/Version**: C# / .NET 10.0 (Backend), TypeScript 5.9 / React 19.2 (Frontend)
**Primary Dependencies**: 
- Backend: Microsoft.Agents.AI.OpenAI 1.0.0-preview, Microsoft.Agents.AI.Hosting.AGUI.AspNetCore 1.0.0-preview, System.Net.ServerSentEvents, MediatR, AutoMapper, FluentValidation, EF Core 10.0.2 (Npgsql), .NET Aspire
- Frontend: @ag-ui/client + @ag-ui/core (AG-UI protocol SDK), react-router v7.13, shadcn/ui + Radix UI, Tailwind CSS 4, react-hook-form + zod, lucide-react, rxjs
**Storage**: PostgreSQL via Aspire (existing `coresre` connection string)
**Testing**: xUnit + FluentAssertions + Moq (existing test pattern, though test projects not yet created)
**Target Platform**: Linux/Windows server (API), Browser SPA (Frontend, Vite dev server on localhost:5173)
**Project Type**: Web application (Backend + Frontend)
**Protocol**: AG-UI (event-driven SSE, POST + text/event-stream, events: RUN_STARTED → TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT* → TEXT_MESSAGE_END → RUN_FINISHED)
**Performance Goals**: Agent 首字延迟 < 3 秒, 对话列表加载 < 1 秒, 消息历史加载 < 2 秒
**Constraints**: SSE 流式传输需保持长连接; 对话与 Agent 绑定后不可更改; 单用户系统无需身份验证
**Scale/Scope**: 单用户系统, 预期 < 100 并发对话, < 1000 条消息/对话

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | spec.md 已完成，包含 5 个用户故事、12 条功能需求、14 个验收场景 |
| II. Test-Driven Development | ✅ PASS | 计划遵循 Red-Green-Refactor，先写测试再实现 |
| III. Domain-Driven Design | ✅ PASS | 新实体（Conversation, ChatMessage）置于 Domain 层，服务接口在 Domain/Application，实现在 Infrastructure |
| IV. Test Immutability | ✅ PASS | 不修改已有测试 |
| V. Interface-Before-Implementation | ✅ PASS | 先定义 IConversationRepository, IAgentInvoker 等接口 |
| Development Workflow | ✅ PASS | 遵循 Spec→Test→Interface→Implement→Verify 五步流程 |
| DDD Layer Rules | ✅ PASS | Domain 无外部依赖; Application 不含业务规则; Infrastructure 不含业务接口; API 不含业务逻辑 |
| Naming Conventions | ✅ PASS | 后端遵循 PascalCase/CQRS 命名; 前端遵循 PascalCase 组件 + camelCase hooks/utils |

**Gate Result**: ✅ ALL PASS — Proceed to Phase 0

### Post-Design Re-evaluation (after Phase 1)

| Principle | Status | Post-Design Notes |
|-----------|--------|-------------------|
| I. SDD | ✅ PASS | spec.md + data-model.md + contracts/chat-api.yaml provide full behavioral contracts |
| II. TDD | ✅ PASS | All entities, commands, queries are testable. Tests derive from 14 acceptance scenarios |
| III. DDD | ✅ PASS | Conversation (aggregate root) + ChatMessage in Domain; IConversationRepository + IAgentInvoker interfaces; repos/services in Infrastructure; endpoints in API |
| IV. Test Immutability | ✅ PASS | No existing tests will be modified |
| V. Interface-Before-Implementation | ✅ PASS | IConversationRepository (Domain), IAgentInvoker (Application) defined before implementations |
| DDD Layer Rules | ✅ PASS | Domain zero external deps; Application CQRS only; Infrastructure implements; API maps endpoints |

**Gate Result**: ✅ ALL PASS — Ready for Phase 2 (tasks)

## Project Structure

### Documentation (this feature)

```text
specs/007-agent-chat-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── chat-api.yaml    # OpenAPI contract for chat endpoints
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
Backend/
├── CoreSRE/                          # API layer
│   ├── Endpoints/
│   │   ├── ChatEndpoints.cs          # NEW: conversation CRUD endpoints (REST)
│   │   └── AgentChatEndpoints.cs     # NEW: AG-UI MapAGUI endpoint (SSE streaming)
│   └── Program.cs                    # UPDATE: AddAGUI(), register endpoints
├── CoreSRE.Application/              # Application layer (CQRS)
│   ├── Chat/
│   │   ├── Commands/
│   │   │   ├── CreateConversationCommand.cs
│   │   │   ├── CreateConversationCommandHandler.cs
│   │   │   ├── TouchConversationCommand.cs       # Updates UpdatedAt + Title after AG-UI run
│   │   │   ├── TouchConversationCommandHandler.cs
│   │   │   ├── DeleteConversationCommand.cs
│   │   │   └── DeleteConversationCommandHandler.cs
│   │   ├── Queries/
│   │   │   ├── GetConversationsQuery.cs
│   │   │   ├── GetConversationsQueryHandler.cs
│   │   │   ├── GetConversationByIdQuery.cs
│   │   │   └── GetConversationByIdQueryHandler.cs  # Joins Conversation + AgentSessionRecord for messages
│   │   └── Dtos/
│   │       ├── ConversationDto.cs
│   │       ├── ConversationSummaryDto.cs
│   │       └── ChatMessageDto.cs           # DTO projected from AgentSessionRecord.SessionData
│   └── Interfaces/
│       └── IAgentResolver.cs         # NEW: resolves Agent → ChatClientAgent/AIAgent
├── CoreSRE.Domain/                   # Domain layer
│   ├── Entities/
│   │   ├── Conversation.cs           # NEW: aggregate root (references AgentSessionRecord, NO ChatMessage entity)
│   │   └── AgentSessionRecord.cs     # EXISTING (from SPEC-004): stores chat history in SessionData JSONB
│   └── Interfaces/
│       └── IConversationRepository.cs # NEW: repository interface
├── CoreSRE.Infrastructure/           # Infrastructure layer
│   ├── Persistence/
│   │   └── Configurations/
│   │       └── ConversationConfiguration.cs   # NEW: EF Fluent API (no ChatMessageConfiguration)
│   ├── Repositories/
│   │   └── ConversationRepository.cs          # NEW: EF implementation
│   └── Services/
│       └── AgentResolverService.cs            # NEW: builds ChatClientAgent from LlmProvider + AgentRegistration
└── CoreSRE.Infrastructure/Migrations/
    └── YYYYMMDD_AddConversations.cs           # NEW: only conversations table (no chat_messages table)

Frontend/
└── src/
    ├── pages/
    │   └── ChatPage.tsx              # NEW: main chat page
    ├── components/
    │   └── chat/
    │       ├── ConversationList.tsx   # NEW: conversation sidebar
    │       ├── MessageArea.tsx        # NEW: message display area with AG-UI subscriber
    │       ├── MessageBubble.tsx      # NEW: single message bubble
    │       ├── MessageInput.tsx       # NEW: text input + send button
    │       ├── AgentSelector.tsx      # NEW: agent dropdown (lockable)
    │       └── DeleteConversationDialog.tsx  # NEW: confirm delete
    ├── hooks/
    │   └── use-agent-chat.ts         # NEW: custom hook wrapping HttpAgent + subscriber
    ├── lib/
    │   └── api/
    │       └── chat.ts               # NEW: conversation CRUD API client (REST)
    └── types/
        └── chat.ts                   # NEW: TypeScript interfaces
```

**Structure Decision**: Backend uses AG-UI's `MapAGUI` to expose a single SSE streaming endpoint per agent invocation. The `MapAGUI` extension handles all SSE formatting and AG-UI event translation automatically — converting `ChatClientAgent` streaming responses into `TEXT_MESSAGE_START/CONTENT/END` events. Conversation CRUD remains as standard REST endpoints. Frontend uses `@ag-ui/client`'s `HttpAgent` + subscriber pattern (no CopilotKit dependency) with custom React components for the chat UI.

## Complexity Tracking

> No violations to justify. All new code fits cleanly into existing 4-layer DDD architecture.
