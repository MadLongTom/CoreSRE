# Feature Specification: AgentSession PostgreSQL 持久化

**Feature Branch**: `004-agent-session-persistence`  
**Created**: 2026-02-10  
**Status**: Draft  
**Input**: User description: "实现基于 PostgreSQL 的 AgentSessionStore 子类（PostgresAgentSessionStore），使 Agent 会话在服务重启后可恢复。Agent Framework 仅提供 InMemoryAgentSessionStore（开发用）和 NoopAgentSessionStore（无持久化），没有任何数据库实现。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Agent 会话持久化存储 (Priority: P1)

当 Agent 处理用户对话时，系统将每次会话状态自动持久化到 PostgreSQL 数据库。Agent Framework 在处理消息后调用 `SaveSessionAsync`，系统将会话序列化为 JSON 并存储到 `agent_sessions` 表。这使得会话数据在服务实例之间可共享，且不会因进程终止而丢失。

**Why this priority**: 会话持久化是整个功能的核心价值所在。没有持久化存储，所有后续的恢复、并发处理等功能都无法实现。这是最小可用产品的唯一必要功能。

**Independent Test**: 可以通过注册一个 Agent 并发送消息触发会话保存，然后直接查询数据库验证 `agent_sessions` 表中存在对应记录，且 JSON 数据结构正确。

**Acceptance Scenarios**:

1. **Given** 一个已注册并配置了 PostgresAgentSessionStore 的 Agent，**When** Agent 处理一条用户消息后 Framework 调用 SaveSessionAsync，**Then** 数据库 `agent_sessions` 表中创建一条新记录，包含正确的 agentId、conversationId、序列化的 session JSON 数据和 session 类型
2. **Given** 同一 Agent 和 conversationId 已存在会话记录，**When** Framework 再次调用 SaveSessionAsync（新一轮对话），**Then** 现有记录被更新（而非插入新记录），updated_at 时间戳刷新，session_data 包含最新的会话状态
3. **Given** SaveSessionAsync 被调用时传入的 session 数据有效，**When** 序列化完成并写入数据库，**Then** 数据库中的 JSONB 字段可被 PostgreSQL 原生 JSON 函数查询和索引

---

### User Story 2 - Agent 会话恢复 (Priority: P1)

当服务重启或 Agent 收到已有对话的后续消息时，系统从 PostgreSQL 数据库恢复之前的会话状态。Agent Framework 调用 `GetSessionAsync`，系统从数据库读取序列化的 JSON 数据并反序列化为原始的 `AgentSession` 对象，使 Agent 能够无缝继续之前的对话上下文。

**Why this priority**: 恢复能力与持久化同等重要——仅存储而不能恢复没有实际价值。两个 User Story 共同构成完整的持久化闭环。

**Independent Test**: 先通过 US1 保存一个会话，然后模拟服务重启（重新创建 AgentSessionStore 实例），调用 GetSessionAsync 获取会话，验证返回的 AgentSession 对象与保存时的状态一致。

**Acceptance Scenarios**:

1. **Given** 数据库中存在某 Agent 和 conversationId 的会话记录，**When** Framework 调用 GetSessionAsync 传入相同的 agentId 和 conversationId，**Then** 系统返回正确反序列化的 AgentSession 对象，对话历史和上下文完整保留
2. **Given** 数据库中不存在请求的会话记录，**When** Framework 调用 GetSessionAsync，**Then** 系统调用 `agent.CreateSessionAsync()` 返回新会话，Agent 将以全新状态开始对话
3. **Given** 服务实例 A 保存了会话数据后终止，**When** 服务实例 B 启动后 Framework 为同一对话调用 GetSessionAsync，**Then** 实例 B 成功恢复实例 A 保存的完整会话状态

---

### User Story 3 - DI 注册与零配置集成 (Priority: P2)

平台开发者通过 Agent Framework 提供的 `WithSessionStore()` 扩展方法，将 `PostgresAgentSessionStore` 注册到依赖注入容器。注册过程简洁明了，不需要额外的配置步骤——存储自动复用系统现有的 PostgreSQL 连接（通过 Aspire 编排的数据库资源）。

**Why this priority**: DI 集成是使用层面的便利性需求。核心存储/恢复逻辑可以先通过手动实例化验证，DI 注册使其达到生产就绪状态。

**Independent Test**: 在 DI 容器中注册 PostgresAgentSessionStore 后，解析 AgentSessionStore 服务并验证获取到的实例类型正确、能正常连接数据库。

**Acceptance Scenarios**:

1. **Given** 平台代码使用 WithSessionStore 注册 PostgresAgentSessionStore，**When** DI 容器构建完成后解析 AgentSessionStore，**Then** 获取到 PostgresAgentSessionStore 实例且内部数据库连接可用
2. **Given** 系统使用 Aspire 编排 PostgreSQL，**When** PostgresAgentSessionStore 注册到 DI，**Then** 存储自动使用 Aspire 配置的数据库连接，无需手动指定连接字符串

---

### Edge Cases

- 当数据库不可达时，SaveSessionAsync 和 GetSessionAsync 应抛出明确的异常，由上层 Agent Framework 处理，不静默吞掉错误
- 当会话数据 JSON 结构因 Agent Framework 版本升级而变化时，session_type 字段标识会话类型，反序列化由 Framework 的 `DeserializeSessionAsync` 处理兼容性
- 当多个服务实例同时对同一 (agentId, conversationId) 调用 SaveSessionAsync 时，采用"最后写入胜出"策略（UPSERT），由数据库主键约束保证数据一致性
- 当 session_data JSON 体积极大时，PostgreSQL JSONB 列支持最大 1GB，正常对话会话远不会达到此限制

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统 MUST 提供一个 `PostgresAgentSessionStore` 实现，继承 Agent Framework 的 `AgentSessionStore` 抽象类
- **FR-002**: `SaveSessionAsync` MUST 将 AgentSession 通过 `AIAgent.SerializeSession()` 序列化为 JSON，并存储到 PostgreSQL `agent_sessions` 表
- **FR-003**: `SaveSessionAsync` MUST 使用 UPSERT 语义——首次保存时插入新记录，后续保存时更新现有记录（基于复合主键 agentId + conversationId）
- **FR-004**: `SaveSessionAsync` MUST 在更新时刷新 `updated_at` 时间戳
- **FR-005**: `GetSessionAsync` MUST 根据 agentId 和 conversationId 从数据库查询会话记录，并通过 `AIAgent.DeserializeSessionAsync()` 反序列化为 `AgentSession` 对象
- **FR-006**: `GetSessionAsync` MUST 在找不到会话记录时调用 `agent.CreateSessionAsync()` 返回新会话（而非返回 null），与 Agent Framework 的行为契约保持一致
- **FR-007**: 系统 MUST 使用 `(agent_id, conversation_id)` 复合主键标识每条会话记录
- **FR-008**: 系统 MUST 将会话数据存储为 PostgreSQL JSONB 类型，以支持原生 JSON 查询能力
- **FR-009**: 系统 MUST 记录会话类型（session_type），标识 AgentSession 的具体子类型（如 ChatClientAgentSession）
- **FR-010**: 系统 MUST 自动记录会话记录的创建时间（created_at）和最后更新时间（updated_at）
- **FR-011**: `PostgresAgentSessionStore` MUST 可通过 Agent Framework 的 DI 扩展方法（WithSessionStore）注册到依赖注入容器
- **FR-012**: `PostgresAgentSessionStore` MUST 复用系统现有的 PostgreSQL 数据库连接（Aspire 编排的 AppDbContext）
- **FR-013**: 系统 MUST 提供数据库迁移以创建 `agent_sessions` 表

### Key Entities

- **AgentSessionRecord**: 表示一条持久化的 Agent 会话记录。核心属性：AgentId（Agent 标识符，字符串）、ConversationId（对话标识符，字符串）、SessionData（序列化的会话 JSON 数据）、SessionType（会话具体类型名称）、CreatedAt（记录创建时间）、UpdatedAt（最后更新时间）。AgentId 和 ConversationId 共同构成唯一标识（复合主键）。此实体不继承系统通用的 BaseEntity（因使用复合字符串主键而非单一 GUID 主键）。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Agent 会话在服务重启后可 100% 恢复到重启前的状态，对话上下文无丢失
- **SC-002**: 会话保存操作在正常负载下 2 秒内完成
- **SC-003**: 会话恢复操作在正常负载下 1 秒内完成
- **SC-004**: 系统支持至少 10,000 条并发活跃会话记录的存储和检索
- **SC-005**: 同一 Agent 对同一对话的重复保存不会产生重复记录，数据库中始终只保留最新状态
- **SC-006**: 新增的持久化组件不影响现有 Agent 注册与管理功能的正常运行

## Assumptions

- Agent Framework 的 `AIAgent.SerializeSession()` 返回的 `JsonElement` 可直接映射到 PostgreSQL JSONB 类型，无需额外转换
- Agent Framework 的 `AIAgent.DeserializeSessionAsync()` 能够从存储的 `JsonElement` 正确恢复 `AgentSession` 对象，包括所有子类型
- 系统当前使用的 EF Core + Npgsql 版本支持 `JsonElement` 到 JSONB 的直接映射
- 会话数据的序列化/反序列化完全由 Agent Framework 负责，本实现仅负责存储和读取 JSON 数据
- `AgentSessionStore` 的 `SaveSessionAsync` 和 `GetSessionAsync` 方法中的 `AIAgent` 参数提供了获取 agentId 和 session 类型信息的途径
- 当前阶段不需要会话过期清理机制，可在未来版本按需添加 TTL 或定时清理
- 当前阶段不需要对 session_data 进行加密存储，会话数据以明文 JSON 形式存储
