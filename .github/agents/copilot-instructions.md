# CoreSRE Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-09

## Active Technologies
- C# / .NET 10.0 + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0 (002-agent-registry-crud)
- PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB value objects (002-agent-registry-crud)
- PostgreSQL (Aspire-orchestrated), JSONB for AgentCard/skills, pgvector for embeddings (P2) (003-agent-semantic-search)
- C# / .NET 10.0 + Microsoft.Agents.AI.Hosting（AgentSessionStore 抽象基类）、EF Core 10.0.2、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 (004-agent-session-persistence)
- PostgreSQL（Aspire 编排），`agent_sessions` 表，JSONB 存储会话数据 (004-agent-session-persistence)
- TypeScript ~5.9.3 / React 19.2 + React Router（路由）、shadcn/ui（组件库）、Tailwind CSS v4（样式）、lucide-react（图标）、radix-ui（无障碍基础） (005-frontend-pages)
- N/A（前端无本地持久化，所有数据通过 REST API 获取） (005-frontend-pages)
- C# / .NET 10.0 (Backend), TypeScript ~5.9.3 (Frontend) + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0, IHttpClientFactory (Backend); React 19, React Router 7, shadcn/ui (Frontend) (006-llm-provider-config)
- PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB, 新表 `llm_providers` (006-llm-provider-config)
- C# / .NET 10.0 (Backend), TypeScript 5.9 / React 19.2 (Frontend) (007-agent-chat-ui)
- PostgreSQL via Aspire (existing `coresre` connection string) (007-agent-chat-ui)
- Backend: C# 14 / .NET 10.0; Frontend: TypeScript 5.9 / React 19.2 + Backend: MediatR 12.4, FluentValidation 11.11, AutoMapper 13, EF Core 10, Microsoft.Agents.AI.* 1.0.0-preview; Frontend: Vite 7.3, react-router 7.13, shadcn/ui (radix-ui 1.4), react-hook-form 7.71, zod 4.3 (008-a2a-card-resolve)
- PostgreSQL (via .NET Aspire + Npgsql), JSONB columns for value objects (008-a2a-card-resolve)
- C# / .NET 10.0 + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0, Microsoft.OpenApi (OpenAPI 文档解析), Microsoft.AspNetCore.DataProtection (凭据加密) (009-tool-gateway-crud)
- PostgreSQL (Aspire-hosted, Npgsql), EF Core Code-First, JSONB columns for value objects (010-chatclient-tool-binding)
- C# / .NET 10.0 + MediatR (CQRS), FluentValidation, EF Core 10.0, Microsoft.Agents.AI.Workflows 1.0.0-preview (runtime mapping, SPEC-021 scope) (011-workflow-crud)
- PostgreSQL (via Npgsql.EntityFrameworkCore.PostgreSQL), JSONB for value objects (011-workflow-crud)
- C# / .NET 10.0 (net10.0) + Agent Framework (`Microsoft.Agents.AI.Workflows` 1.0.0-preview.260209.1), MediatR (CQRS), FluentValidation, AutoMapper, EF Core 10.0.2 + Npgsql (PostgreSQL) (012-workflow-execution-engine)
- PostgreSQL (via Aspire Npgsql), JSONB for NodeExecutionVO 列表和 DAG 图快照 (012-workflow-execution-engine)
- TypeScript 5.9 / React 19.2 / Vite 7.3 + @xyflow/react (React Flow v12), @dagrejs/dagre, shadcn/ui, react-router 7.13, lucide-react, zod 4.3, react-hook-form 7.71 (013-workflow-frontend)
- N/A (frontend only, all persistence via backend API) (013-workflow-frontend)
- C# / .NET 10.0 (Backend), TypeScript (Frontend — Vite + React) + Microsoft.Agents.AI.Hosting 1.0.0-preview.260209.1, Microsoft.Extensions.AI 10.2.0, Entity Framework Core 10.0.2, Npgsql 10.0.0 (014-agent-memory-history)
- PostgreSQL (via Aspire + Npgsql EF Core), pgvector extension (new for semantic memory) (014-agent-memory-history)
- C# / .NET 10 (`net10.0`) + MediatR 12.4.1, AutoMapper 13.0.1, FluentValidation 11.11.0, Microsoft.Extensions.AI.Abstractions 10.2.0, Microsoft.Agents.AI.* 1.0.0-preview, EF Core 10.0.2 (015-workflow-engine-fix)
- PostgreSQL via Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 (015-workflow-engine-fix)
- C# / .NET 10 (net10.0) + Microsoft.Extensions.AI (MEAI), AutoMapper, System.Text.Json, EF Core 10 (016-workflow-dataflow-engine)
- PostgreSQL with JSONB columns (graph_snapshot, node_executions) — no SQL migration needed; new fields use C# defaults that serialize transparently (016-workflow-dataflow-engine)

- C# / .NET 10.0 (`net10.0`) (001-aspire-apphost-setup)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# / .NET 10.0 (`net10.0`)

## Code Style

C# / .NET 10.0 (`net10.0`): Follow standard conventions

## Recent Changes
- 016-workflow-dataflow-engine: Added C# / .NET 10 (net10.0) + Microsoft.Extensions.AI (MEAI), AutoMapper, System.Text.Json, EF Core 10
- 015-workflow-engine-fix: Added C# / .NET 10 (`net10.0`) + MediatR 12.4.1, AutoMapper 13.0.1, FluentValidation 11.11.0, Microsoft.Extensions.AI.Abstractions 10.2.0, Microsoft.Agents.AI.* 1.0.0-preview, EF Core 10.0.2
- 014-agent-memory-history: Added C# / .NET 10.0 (Backend), TypeScript (Frontend — Vite + React) + Microsoft.Agents.AI.Hosting 1.0.0-preview.260209.1, Microsoft.Extensions.AI 10.2.0, Entity Framework Core 10.0.2, Npgsql 10.0.0


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
