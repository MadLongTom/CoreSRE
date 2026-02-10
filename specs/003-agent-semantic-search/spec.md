# Feature Specification: Agent 能力语义搜索

**Feature Branch**: `003-agent-semantic-search`  
**Created**: 2026-02-09  
**Status**: Draft  
**Input**: User description: "支持按自然语言描述搜索 Agent 的 skill。基于 Agent 注册时的 skill description 字段，使用向量嵌入（调用 LLM Embedding API）进行语义匹配，返回最相关的 Agent 列表。初期可用关键词模糊匹配实现，后续升级为向量搜索。端点: GET /api/agents/search?q={query}"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 按关键词搜索 Agent 技能（Priority: P1） 🎯 MVP

作为平台 Orchestrator 或管理员，我需要通过自然语言描述（如"处理客户投诉"、"代码审查"）搜索已注册 Agent 的技能，以便快速找到最适合处理当前任务的 Agent。

我在搜索框中输入查询文本，系统在所有 A2A Agent 的 skill name 和 skill description 字段中执行大小写不敏感的关键词模糊匹配，返回包含匹配技能的 Agent 列表。每个结果包含 Agent 的摘要信息以及匹配到的技能列表，按匹配度排序（匹配技能数量多的 Agent 排在前面）。

**Why this priority**: 关键词匹配是搜索功能的基础形态，零外部依赖（纯数据库查询），可立即交付可用的搜索体验。同时也是后续向量搜索的兜底方案——即使语义搜索不可用，关键词搜索仍然有效。

**Independent Test**: 注册若干 A2A Agent（各含不同 skills），发送 `GET /api/agents/search?q=customer`，验证返回包含 skill name 或 description 中含 "customer" 的 Agent 列表。

**Acceptance Scenarios**:

1. **Given** 系统中已注册 3 个 A2A Agent，其中 Agent-A 有 skill "answer-customer-questions"（description: "Answer questions about products and orders"），Agent-B 有 skill "code-review"（description: "Review code for bugs"），Agent-C 有 skill "customer-onboarding"（description: "Guide new customers through setup"），**When** 请求 `GET /api/agents/search?q=customer`，**Then** 返回 HTTP 200，结果列表包含 Agent-A 和 Agent-C（均匹配 "customer"），不包含 Agent-B
2. **Given** 系统中已注册若干 Agent，**When** 请求 `GET /api/agents/search?q=Customer`（大写），**Then** 搜索为大小写不敏感，返回与 `?q=customer` 相同的结果
3. **Given** 系统中无任何 Agent 的 skill 匹配搜索词，**When** 请求 `GET /api/agents/search?q=nonexistent`，**Then** 返回 HTTP 200 和空列表
4. **Given** 搜索请求未提供查询参数 `q`，**When** 请求 `GET /api/agents/search`，**Then** 返回 HTTP 400，错误信息指出查询参数 `q` 为必填
5. **Given** 搜索请求 `q` 为空字符串，**When** 请求 `GET /api/agents/search?q=`，**Then** 返回 HTTP 400，错误信息指出查询文本不能为空
6. **Given** 系统中有 ChatClient 和 Workflow 类型的 Agent（无 skills），**When** 搜索任何关键词，**Then** 结果仅包含 A2A 类型 Agent（因为只有 A2A Agent 有 AgentCard 中的 skills）

---

### User Story 2 — 按向量嵌入语义搜索 Agent 技能（Priority: P2）

作为平台 Orchestrator，我需要通过语义相近的描述找到 Agent——即使搜索词与 skill 的名称或描述不完全匹配，只要语义相关也应被找到。例如搜索"帮客户解决问题"应能匹配到 skill description 为"Answer customer questions about products"的 Agent。

系统将搜索查询文本通过 Embedding API 转换为向量，与预先计算并存储的 skill description 向量进行相似度比较（余弦相似度），返回相似度超过阈值的 Agent 列表，按相似度降序排列。

**Why this priority**: 语义搜索是该功能的最终形态，提供远超关键词匹配的搜索精度。但依赖外部 Embedding API（需配置 LLM 提供商）和向量存储扩展，增加了基础设施复杂度，适合在关键词搜索验证可用后再实现。

**Independent Test**: 注册 Agent 含 skill "Answer customer questions about products"，搜索 "帮客户解决问题"（语义相关但无关键词重叠），验证该 Agent 出现在结果中。

**Acceptance Scenarios**:

1. **Given** 系统中有 A2A Agent 含 skill description "Answer customer questions about products and orders"，该 skill 的向量已预计算并存储，**When** 请求 `GET /api/agents/search?q=help users with product inquiries`，**Then** 返回该 Agent（语义匹配，尽管无关键词重叠），结果包含相似度评分
2. **Given** 搜索查询语义与所有已注册 skill 均不相关，**When** 搜索 `GET /api/agents/search?q=quantum physics simulation`，**Then** 返回空列表（所有相似度均低于阈值）
3. **Given** Embedding API 服务不可用（超时或返回错误），**When** 请求语义搜索，**Then** 系统自动降级为关键词搜索并返回结果，响应中包含提示信息表明使用了降级模式
4. **Given** 新注册了一个 A2A Agent 含新 skill，**When** 注册完成后，**Then** 系统在后台异步计算该 skill 的向量嵌入并存储，后续搜索可匹配到该新 skill

---

### Edge Cases

- 搜索文本超过 500 字符时，系统返回 HTTP 400 并提示查询文本过长
- 搜索文本仅包含空格或特殊字符时，系统返回 HTTP 400
- Agent 注册时 skills 列表为空（合法状态），该 Agent 不出现在任何搜索结果中
- 一个 Agent 有多个 skill 匹配搜索词时，结果中该 Agent 只出现一次，但附带所有匹配的 skill 列表
- 并发场景下新注册 Agent 的 skill 尚未完成向量嵌入，此时语义搜索不会返回该 Agent（最终一致性），但关键词搜索可立即匹配
- 搜索查询中包含 SQL 注入或 JSONB 操作符特殊字符时，系统正常处理（参数化查询防注入）

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统必须提供 Agent 技能搜索端点 `GET /api/agents/search?q={query}`，通过查询参数 `q` 接受搜索文本
- **FR-002**: 搜索必须在所有 A2A 类型 Agent 的 AgentCard.Skills 中的 name 和 description 字段执行匹配
- **FR-003**: 关键词搜索必须为大小写不敏感的模糊匹配（包含即匹配，非精确匹配）
- **FR-004**: 搜索结果必须返回匹配 Agent 的摘要信息（id、name、agentType、status、createdAt）以及匹配到的 skill 列表
- **FR-005**: 搜索结果必须按相关性排序——关键词模式下按匹配 skill 数量降序排列
- **FR-006**: 查询参数 `q` 为必填且不能为空字符串，违反时返回 HTTP 400 和结构化错误信息
- **FR-007**: 搜索文本长度限制为 500 字符，超过时返回 HTTP 400
- **FR-008**: 搜索结果仅包含 A2A 类型的 Agent（ChatClient 和 Workflow 类型无 skills 数据，自动排除）
- **FR-009**: 系统必须支持语义搜索模式——将查询文本和 skill description 转换为向量嵌入，通过余弦相似度匹配，相似度超过配置阈值的结果纳入返回
- **FR-010**: 语义搜索的向量嵌入必须通过 `IEmbeddingGenerator<string, Embedding<float>>` 抽象接口生成，不直接耦合具体 LLM 提供商
- **FR-011**: A2A Agent 注册或更新时，系统必须异步计算其 skill description 的向量嵌入并持久化存储
- **FR-012**: 当 Embedding 服务不可用时，语义搜索必须自动降级为关键词搜索，并在响应中标明降级状态
- **FR-013**: 语义搜索结果必须包含相似度评分，并按评分降序排列

### Key Entities

- **AgentRegistration**（已有聚合根）: 搜索目标。搜索操作读取其 AgentCard.Skills 中的 name/description 字段进行匹配。本 Spec 不修改该实体结构。
- **AgentSkillVO**（已有值对象）: 搜索匹配的最小粒度。包含 Name（string, 必填）和 Description（string?, 可选）。嵌套在 AgentCardVO 中，存储为 JSONB。
- **SkillEmbedding**（新实体，仅 P2）: 存储 skill description 的向量嵌入。关联到 AgentRegistration 和具体 skill（通过 AgentId + SkillName 复合标识）。包含向量数据（float 数组）和生成时间。用于语义搜索时的余弦相似度计算。

## Assumptions

- **仅搜索 A2A Agent**: 只有 A2A 类型的 Agent 拥有 AgentCard（含 skills 列表）。ChatClient 和 Workflow Agent 不在搜索范围内。如果未来其他 Agent 类型也需要技能搜索，需扩展数据模型。
- **Skill 数量可控**: 每个 Agent 的 skills 列表预期不超过 20 条，全平台 Agent 总数预期不超过 100。此规模下全表扫描 + 文本匹配性能足够，无需额外索引优化。
- **向量搜索基础设施**: P2 语义搜索依赖向量存储扩展和外部 Embedding API。扩展需由基础设施配置安装。Embedding API 的具体提供商通过 DI 配置，不硬编码。
- **相似度阈值**: 语义搜索的默认相似度阈值设为 0.7（余弦相似度），可通过应用配置调整。低于阈值的结果不返回。
- **异步嵌入计算**: Agent 注册/更新时，向量嵌入计算为异步操作（后台任务），不阻塞注册/更新响应。在嵌入计算完成前，新注册的 skill 无法通过语义搜索匹配（最终一致性），但关键词搜索立即可用。
- **降级策略**: 当 Embedding 服务不可用时，自动降级为关键词搜索，不中断用户搜索体验。降级状态通过响应字段告知调用方。
- **搜索结果不分页**: 与 Agent 列表查询一致（SPEC-001 决策），搜索结果初期不实现分页。预期搜索结果集较小（匹配 Agent 数量远少于总数）。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 用户可通过关键词搜索在 1 秒内获得匹配结果，在平台 Agent 总数 ≤ 100 的场景下
- **SC-002**: 关键词搜索准确度达到 100%——包含搜索词的 skill name 或 description 所属的 Agent 全部出现在结果中，不遗漏、不误报
- **SC-003**: 语义搜索（向量匹配）能在搜索词与 skill description 无关键词重叠但语义相关时返回正确结果，搜索体验满意度显著优于纯关键词模式
- **SC-004**: Embedding 服务不可用时，搜索功能 100% 降级为关键词模式继续工作，用户无感知中断
- **SC-005**: 搜索端点与现有 Agent CRUD 端点（SPEC-001）无冲突，不影响已有功能的正常运行
