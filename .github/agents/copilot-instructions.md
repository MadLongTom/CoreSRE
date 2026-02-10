# CoreSRE Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-09

## Active Technologies
- C# / .NET 10.0 + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0 (002-agent-registry-crud)
- PostgreSQL (Aspire-orchestrated), EF Core `ToJson()` for JSONB value objects (002-agent-registry-crud)
- PostgreSQL (Aspire-orchestrated), JSONB for AgentCard/skills, pgvector for embeddings (P2) (003-agent-semantic-search)
- C# / .NET 10.0 + Microsoft.Agents.AI.Hosting（AgentSessionStore 抽象基类）、EF Core 10.0.2、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 (004-agent-session-persistence)
- PostgreSQL（Aspire 编排），`agent_sessions` 表，JSONB 存储会话数据 (004-agent-session-persistence)

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
- 004-agent-session-persistence: Added C# / .NET 10.0 + Microsoft.Agents.AI.Hosting（AgentSessionStore 抽象基类）、EF Core 10.0.2、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
- 003-agent-semantic-search: Added C# / .NET 10.0 + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0
- 002-agent-registry-crud: Added C# / .NET 10.0 + MediatR 12.4.1, FluentValidation 11.11.0, AutoMapper 13.0.1, EF Core 10.0.2, Npgsql 10.0.0


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
