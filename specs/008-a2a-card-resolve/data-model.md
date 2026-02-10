# Data Model: A2A AgentCard 自动解析

**Feature**: 008-a2a-card-resolve  
**Date**: 2026-02-10

## Entity Relationship

```
┌─────────────────────────────┐
│   External A2A Agent        │
│   (remote endpoint)         │
│                             │
│   GET /.well-known/         │
│       agent-card.json       │
│                             │
│   Returns: A2A AgentCard    │
│   (full protocol model)     │
└───────────┬─────────────────┘
            │ HTTP GET
            ▼
┌─────────────────────────────┐      maps to      ┌─────────────────────────────┐
│   ResolvedAgentCard (DTO)   │ ─────────────────► │   AgentRegistration         │
│   (Application layer)       │                    │   (existing Domain entity)  │
│                             │                    │                             │
│   name: string              │ ──► Name           │   Name: string              │
│   description: string       │ ──► Description    │   Description: string?      │
│   url: string               │ ──► Endpoint*      │   Endpoint: string?         │
│   skills: [...]             │ ──► AgentCard      │   AgentCard: AgentCardVO?   │
│   interfaces: [...]         │      .Skills       │     .Skills: [AgentSkillVO] │
│   securitySchemes: [...]    │      .Interfaces   │     .Interfaces: [...]      │
│                             │      .SecuritySch. │     .SecuritySchemes: [...]  │
└─────────────────────────────┘                    └─────────────────────────────┘

* Endpoint 取决于 URL 覆写选项：
  - 覆写开启（默认）→ 使用用户输入的 URL
  - 覆写关闭 → 使用 AgentCard.url
```

## Existing Entities (NO CHANGES)

### AgentRegistration (Domain Entity)

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| Id | Guid | No | PK, 自动生成 |
| Name | string | No | Agent 名称, max 200 |
| Description | string? | Yes | Agent 描述 |
| AgentType | AgentType (enum) | No | A2A / ChatClient / Workflow |
| Status | AgentStatus (enum) | No | Active / Inactive |
| Endpoint | string? | Yes | A2A endpoint URL, max 2048 |
| AgentCard | AgentCardVO? | Yes | JSONB, A2A 专用 |
| LlmConfig | LlmConfigVO? | Yes | JSONB, ChatClient 专用 |
| WorkflowRef | Guid? | Yes | Workflow 专用 |
| CreatedAt | DateTime | No | 创建时间 |
| UpdatedAt | DateTime? | Yes | 最后更新时间 |

### AgentCardVO (Value Object, JSONB)

| Field | Type | Description |
|-------|------|-------------|
| Skills | List\<AgentSkillVO\> | 技能列表 |
| Interfaces | List\<AgentInterfaceVO\> | 通信接口列表 |
| SecuritySchemes | List\<SecuritySchemeVO\> | 安全方案列表 |

### AgentSkillVO (Value Object, nested)

| Field | Type | Description |
|-------|------|-------------|
| Name | string | 技能名称 |
| Description | string? | 技能描述 |

### AgentInterfaceVO (Value Object, nested)

| Field | Type | Description |
|-------|------|-------------|
| Protocol | string | 协议类型 (e.g. "HTTP+SSE", "WebSocket") |
| Path | string? | URL 路径 |

### SecuritySchemeVO (Value Object, nested)

| Field | Type | Description |
|-------|------|-------------|
| Type | string | 方案类型 (e.g. "apiKey", "oauth2") |
| Parameters | string? | 配置参数 (JSON string) |

## New Types

### ResolvedAgentCardDto (Application layer DTO)

用于从后端返回解析结果给前端。包含 AgentCard 中表单需要的所有字段。

| Field | Type | Description |
|-------|------|-------------|
| name | string | AgentCard 中的 Agent 名称 |
| description | string | AgentCard 中的 Agent 描述 |
| url | string | AgentCard 中记录的 URL（可能与用户输入不同） |
| version | string | AgentCard 中的版本号 |
| skills | List\<AgentSkillDto\> | 映射后的技能列表 |
| interfaces | List\<AgentInterfaceDto\> | 映射后的接口列表 |
| securitySchemes | List\<SecuritySchemeDto\> | 映射后的安全方案列表 |

### ResolveAgentCardQuery (Application layer MediatR Query)

| Field | Type | Description |
|-------|------|-------------|
| Url | string | 用户输入的 A2A Agent Endpoint URL |

### IAgentCardResolver (Application layer Interface)

| Method | Input | Output | Description |
|--------|-------|--------|-------------|
| ResolveAsync | string url, CancellationToken | ResolvedAgentCardDto | 从远程端点获取并映射 AgentCard |

## Field Mapping: SDK AgentCard → ResolvedAgentCardDto

| SDK Field (`A2A.AgentCard`) | DTO Field | Mapping Rule |
|-----------------------------|-----------|--------------|
| `Name` | `name` | 直接映射 |
| `Description` | `description` | 直接映射 |
| `Url` | `url` | 直接映射 |
| `Version` | `version` | 直接映射 |
| `Skills[].Name` | `skills[].name` | 直接映射 |
| `Skills[].Description` | `skills[].description` | 直接映射 |
| `AdditionalInterfaces[].Transport` | `interfaces[].protocol` | 枚举 → string（e.g. `JsonRpc` → "JsonRpc"） |
| `AdditionalInterfaces[].Url` | `interfaces[].path` | 直接映射（取相对路径或完整 URL） |
| `SecuritySchemes` (dict) | `securitySchemes[]` | 键 → type, 值序列化 → parameters |
| `ProtocolVersion`, `Capabilities`, `Provider`, `IconUrl`, `DocumentationUrl`, etc. | *(不映射)* | 当前 VO 模型不包含这些字段 |

## State Transitions

本功能不涉及实体状态变更。解析操作是无副作用的查询：

```
用户输入 URL → 调用解析 API → 返回 DTO → 前端填充表单 → 用户提交创建（使用现有 RegisterAgent 流程）
```

## Validation Rules

| Rule | Layer | Description |
|------|-------|-------------|
| URL 不为空 | Application (Validator) | FluentValidation: `NotEmpty()` |
| URL 格式合法 | Application (Validator) | 必须是 http/https scheme |
| URL 长度 ≤ 2048 | Application (Validator) | `MaximumLength(2048)` |
| 远程响应可解析为 AgentCard | Infrastructure (Service) | SDK 处理；失败抛出 `A2AException` |
