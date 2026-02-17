# Implementation Plan: Team Agent 领域模型与 CRUD

**Branch**: `018-team-agent-model` | **Date**: 2026-02-17 | **Spec**: [spec.md](spec.md)
**Input**: [TEAM-AGENT-SPEC-INDEX](../../docs/specs/TEAM-AGENT-SPEC-INDEX.md)

## Summary

在 `AgentType` 枚举中新增 `Team` 值，引入 `TeamMode` 枚举（6 种编排模式）和 `TeamConfigVO` / `HandoffTargetVO` 值对象。扩展 `AgentRegistration` 聚合根支持 `CreateTeam()` 工厂方法及 Team 专属验证。更新 CQRS 命令/查询、DTO 映射、FluentValidation 规则。新增 EF Core Migration 为 `AgentRegistration` 表添加 `TeamConfig` JSONB 列。前端 Agent 注册表单新增 Team 类型配置 UI。

## Technical Context

**Language/Version**: C# / .NET 10, TypeScript 5.9 / React 19.2  
**Primary Dependencies**: Microsoft.Agents.AI.Workflows (agent-framework), EF Core 10, Npgsql, MediatR, FluentValidation, AutoMapper  
**Storage**: PostgreSQL 17 (EF Core 10 + JSONB)  
**Testing**: xUnit + Moq (`CoreSRE.Application.Tests`)  
**Target Platform**: Linux/Windows server + SPA (Vite)  
**Constraints**: 单实例部署，STJ 序列化

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Spec-Driven Development | ✅ PASS | spec.md 完成，含 3 User Stories, 13 FRs, 3 NFRs, 3 SCs |
| II. TDD (NON-NEGOTIABLE) | ✅ PASS | 先写 AgentRegistration.CreateTeam() 单元测试 + Validator 测试 |
| III. DDD Layer Rules | ✅ PASS | 枚举/VO/Entity ∈ Domain，Command/Query/DTO ∈ Application，Migration ∈ Infrastructure |
| IV. Test Immutability | ✅ PASS | 不修改已有测试，仅新增 |
| V. Interface-Before-Implementation | ✅ PASS | 先定义 VO record + 工厂方法签名，再实现 |

## Project Structure

### New Files

```text
Backend/CoreSRE.Domain/
├── Enums/
│   └── TeamMode.cs                          # NEW — 6 种编排模式枚举
├── ValueObjects/
│   ├── TeamConfigVO.cs                      # NEW — Team 配置值对象
│   └── HandoffTargetVO.cs                   # NEW — Handoff 交接目标
├── Entities/
│   └── AgentRegistration.cs                 # MODIFY — +TeamConfig, +CreateTeam()

Backend/CoreSRE.Application/
├── Agents/
│   ├── Commands/
│   │   ├── RegisterAgent/
│   │   │   ├── RegisterAgentCommand.cs      # MODIFY — +TeamConfig field
│   │   │   ├── RegisterAgentCommandHandler.cs # MODIFY — handle Team type
│   │   │   └── RegisterAgentCommandValidator.cs # MODIFY — Team validation
│   │   └── UpdateAgent/
│   │       ├── UpdateAgentCommand.cs        # MODIFY — +TeamConfig field
│   │       ├── UpdateAgentCommandHandler.cs # MODIFY — handle Team type
│   │       └── UpdateAgentCommandValidator.cs # MODIFY — Team validation
│   └── DTOs/
│       ├── AgentRegistrationDto.cs          # MODIFY — +TeamConfigDto
│       ├── TeamConfigDto.cs                 # NEW — Team 配置 DTO
│       ├── HandoffTargetDto.cs              # NEW — Handoff 目标 DTO
│       └── AgentMappingProfile.cs           # MODIFY — Team mapping

Backend/CoreSRE.Infrastructure/
├── Migrations/
│   └── YYYYMMDDHHMMSS_AddTeamConfig.cs     # NEW — Add TeamConfig JSONB column
├── Persistence/
│   └── AppDbContext.cs                      # MODIFY — TeamConfig JSONB mapping

Backend/CoreSRE.Application.Tests/
├── Agents/
│   ├── CreateTeamAgentTests.cs              # NEW — CreateTeam domain tests
│   ├── RegisterTeamAgentValidatorTests.cs   # NEW — Validator tests
│   └── UpdateTeamAgentValidatorTests.cs     # NEW — Update validator tests

Frontend/src/
├── features/agents/
│   ├── components/
│   │   ├── TeamConfigForm.tsx               # NEW — Team 配置表单
│   │   ├── ParticipantSelector.tsx          # NEW — 参与者多选
│   │   └── HandoffRouteEditor.tsx           # NEW — Handoff 路由编辑
│   └── types/
│       └── agent.ts                         # MODIFY — +TeamConfig types
```

## Key Design Decisions

### 1. TeamConfigVO 为单一 VO 而非按 Mode 拆分

**决策**: 所有 TeamMode 的配置共用一个 `TeamConfigVO`，通过 nullable 字段区分模式专属配置。

**理由**: 
- JSONB 存储天然支持 nullable 字段，反序列化自动忽略缺失字段
- 避免复杂的多态 JSONB 反序列化配置
- 验证逻辑按 Mode 分支处理，已足够清晰

### 2. 首期禁止 Team 嵌套

**决策**: `CreateTeam()` 验证 ParticipantIds 中不包含 `AgentType.Team` 类型的 Agent。

**理由**: 避免递归解析复杂度，后续按需开放。

### 3. TeamMode 枚举值包含 Selector 和 MagneticOne

**决策**: 虽然 SPEC-102/103 尚未实现执行逻辑，但领域模型先定义完整枚举值。

**理由**: 避免后续增加枚举值导致数据库迁移，模型先行。
