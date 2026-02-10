# Research: LLM Provider 配置与模型发现

**Feature**: 006-llm-provider-config | **Date**: 2026-02-10

## Decision 1: OpenAI `/models` API 响应格式

**Decision**: 解析标准 OpenAI `GET /v1/models` 响应格式，仅持久化 `data[].id` 字段。

**Rationale**: 所有 OpenAI 兼容 Provider（Ollama、LM Studio、vLLM、LocalAI 等）均返回 `{ "object": "list", "data": [{ "id": "model-id", "object": "model", "created": 1234, "owned_by": "..." }] }` 格式。`id` 是唯一在所有 Provider 间一致且有实际用途的字段（传递给 `/v1/chat/completions` 的 `model` 参数）。`owned_by` 和 `created` 在不同 Provider 间含义不一致（有的返回 `0`），存储意义不大。

**Alternatives considered**:
- 存储完整 model 对象（含 `owned_by`、`created`）：增加存储复杂度，`owned_by` 在跨 Provider 场景下无统一语义，收益低。
- 使用 Provider 特有的 API 获取更丰富的模型信息：破坏 OpenAI 兼容性约束，不同 Provider 的扩展字段不一致。

## Decision 2: API Key 安全存储策略

**Decision**: v1 阶段 API Key 以明文存储于 PostgreSQL，仅在 API 响应中掩码（最后 4 位可见）。后续迭代可引入 AES-256 加密或 HashiCorp Vault。

**Rationale**: 引入加密需要密钥管理（密钥轮换、安全存储加密密钥本身），增加 MVP 复杂度。当前场景为内部工具，无公网暴露风险。API 层严格掩码确保 Key 不通过 HTTP 响应泄露，是最关键的安全防线。

**Alternatives considered**:
- AES-256 应用层加密：需要管理加密密钥（环境变量/Key Vault），增加 Infrastructure 层复杂度，MVP 阶段收益不大。
- HashiCorp Vault / Azure Key Vault：需要额外基础设施，对开发环境依赖重。
- 仅存储 Key hash（不可逆）：无法在模型发现时使用 Key，功能无法实现。

## Decision 3: 模型发现结果存储方式

**Decision**: 发现的模型 ID 列表作为 `LlmProvider` 聚合根内的值对象集合（`List<string>`），以 JSONB 列存储在 `llm_providers` 表中，不单独建表。

**Rationale**: 模型列表与 Provider 生命周期完全绑定（创建/刷新/删除随 Provider 同步），无独立的业务标识（不需要按模型 ID 主键查询），数据量小（通常 < 1000 个字符串）。JSONB 列存储避免了额外的表/关系/仓储，复杂度最低。

**Alternatives considered**:
- 独立 `discovered_models` 表（外键关联 Provider）：引入 1:N 关系、独立仓储、级联删除配置，但模型无独立生命周期，过度设计。
- 仅缓存不持久化（每次按需发现）：用户体验差，每次选择 Provider 都需等待外部 API 调用，不满足 FR-005。

## Decision 4: IModelDiscoveryService 接口归属层

**Decision**: `IModelDiscoveryService` 接口定义在 `CoreSRE.Application/Common/Interfaces/`，实现在 `CoreSRE.Infrastructure/Services/`。

**Rationale**: 模型发现是一个涉及 HTTP 外部调用的应用服务，不属于纯领域逻辑（Domain 层禁止外部包依赖）。将接口放在 Application 层的 Common/Interfaces 目录（与 DDD 分层一致），Handler 通过接口调用，Infrastructure 提供 `HttpClient` 实现，保持依赖方向正确（Application → Infrastructure）。

**Alternatives considered**:
- 接口放在 Domain/Interfaces：Domain 层不应知晓 HTTP 调用概念，即使通过接口也会引入 "发现" 这一非领域概念。
- 直接在 Handler 中调用 HttpClient：违反 DDD 分层（Application 层不应直接依赖 Infrastructure 实现）。

## Decision 5: LlmConfigVO 扩展策略（向后兼容）

**Decision**: 在现有 `LlmConfigVO` 中新增 `ProviderId`（`Guid?`），现有 Agent 的 `ProviderId` 为 null，向后兼容。

**Rationale**: 现有 ChatClient Agent 已有 `modelId` 字段（手动输入的文本），新增可选的 `ProviderId` 不会破坏已有数据。EF Core Migration 会将新字段设为 nullable。API 层在 `ProviderId` 为 null 时仍正常工作（仅显示 modelId，不显示 Provider 关联信息）。

**Alternatives considered**:
- 创建新的 `LlmConfigV2VO` 替代旧版：需要数据迁移脚本，增加复杂度。
- 强制要求 ProviderId 不可为 null：需要迁移所有现有 Agent 数据，且可能没有合适的 Provider 可关联。

## Decision 6: HttpClient 配置策略

**Decision**: 使用 `IHttpClientFactory` 注册命名 HttpClient（"ModelDiscovery"），配合 Aspire 的 Standard Resilience Handler（自动 retry + circuit breaker + timeout）。

**Rationale**: 现有 `Program.cs` 已配置 `ConfigureHttpClientDefaults` 添加 `StandardResilienceHandler` 和 `ServiceDiscovery`。通过 `IHttpClientFactory` 注册的 HttpClient 自动继承这些能力，包括 OpenTelemetry 跟踪。命名 HttpClient 比 typed HttpClient 更简单，适合只有一个 HTTP 调用的场景。

**Alternatives considered**:
- Typed HttpClient：需要额外的 wrapper 类，对单一调用场景过度封装。
- 直接 `new HttpClient()`：不受 DI 管理，无法利用 Aspire 的 resilience/OTel 能力。

## Decision 7: Provider 删除保护实现

**Decision**: 删除 Provider 时，Handler 通过查询 `IAgentRegistrationRepository` 检查是否有 ChatClient Agent 的 `LlmConfig.ProviderId` 引用该 Provider，有则拒绝删除并返回关联数量。

**Rationale**: 这是简单的查询检查，不需要引入外键约束（因为 `ProviderId` 存储在 JSONB 列内部，PostgreSQL 外键无法约束 JSONB 内部字段）。Application 层 Handler 承担此校验职责，符合 CQRS 模式。

**Alternatives considered**:
- PostgreSQL 外键约束：`ProviderId` 在 JSONB 中，无法建立 FK。
- Domain Event + Saga：复杂度远超需求。
- 不做保护（允许删除孤儿引用）：违反 FR-011。
