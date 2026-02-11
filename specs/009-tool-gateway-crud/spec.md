# Feature Specification: Tool Gateway — 工具注册、管理与统一调用

**Feature Branch**: `009-tool-gateway-crud`  
**Created**: 2026-02-10  
**Status**: Draft  
**Input**: User description: "模块 M2: Tool Gateway（工具统一接入网关）SPEC-010~013: REST API 工具注册与管理、MCP Server 工具注册与管理、OpenAPI 文档自动导入生成工具节点、工具调用代理（统一调用入口）。支持手动注册外部 REST API 工具和 MCP Server 工具源，支持 OpenAPI 文档自动解析批量生成工具节点，提供统一的工具调用 API 屏蔽底层协议差异。"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 注册 REST API 工具（Priority: P1）🎯 MVP

作为平台管理员，我需要将一个外部 REST API 服务注册为平台可管理的工具，以便 Agent 在执行任务时可以发现并调用该工具。

注册时我提交工具的基本信息（名称、描述）、工具类型（RestApi）、连接配置（端点 URL）以及认证配置（认证方式：None / ApiKey / Bearer / OAuth2，以及对应的凭据信息）。系统校验必填字段后，将工具信息持久化到数据库，凭据信息加密存储。注册成功后工具进入 Active 状态，可被 Agent 通过 Tool Gateway 发现和调用。

**Why this priority**: REST API 是最常见的第三方工具接入方式，也是 Tool Gateway 最基础的场景。实现此类型即可验证核心领域模型（ToolRegistration、ConnectionConfig、AuthConfig）和凭据加密存储的设计。

**Independent Test**: 发送 `POST /api/tools` 请求（toolType: RestApi），验证返回 201 状态码和完整的工具记录。随后通过 `GET /api/tools/{id}` 确认持久化成功，且凭据信息不以明文返回。

**Acceptance Scenarios**:

1. **Given** 系统中无任何工具注册记录，**When** 提交包含名称、描述、端点 URL 和 ApiKey 认证配置的 REST API 工具注册请求，**Then** 系统返回 HTTP 201，响应体包含系统分配的唯一 ID、状态为 Active、连接配置和认证类型正确
2. **Given** 注册请求中认证类型为 Bearer，**When** 提交注册请求包含 Bearer Token 凭据，**Then** 系统注册成功，通过 `GET /api/tools/{id}` 查询时凭据字段返回掩码值（如 `***`）而非明文
3. **Given** 提交 REST API 工具注册请求但缺少端点 URL，**When** 系统处理请求，**Then** 返回 HTTP 400，错误信息明确指出缺少必填字段 endpoint
4. **Given** 系统中已存在同名工具，**When** 提交相同名称的工具注册请求，**Then** 系统返回 HTTP 409 Conflict，提示名称已被占用

---

### User Story 2 — 注册 MCP Server 工具源（Priority: P1）

作为平台管理员，我需要将一个 MCP Server 注册为平台的工具源，以便系统自动发现该 Server 暴露的所有 Tool 并纳入统一管理。

注册时我提交工具源信息（名称、描述）、工具类型（McpServer）以及 MCP 连接配置（端点 URL、Transport 类型：StreamableHttp 或 Stdio）。系统注册完成后，通过 MCP `initialize` 握手连接到 MCP Server，然后调用 `tools/list` 方法自动发现该 Server 暴露的所有 Tool。每个发现的 Tool 的 Schema 信息（name、description、inputSchema、annotations）存储为该工具源的子工具项。

**Why this priority**: MCP 是新一代工具协议标准，支持 MCP Server 接入是 Tool Gateway 区别于简单 API 代理的核心能力。自动发现 Tool 极大减少了管理员手动配置的工作量。

**Independent Test**: 提交 MCP Server 注册请求后，系统自动发现其 Tool 列表。通过 `GET /api/tools/{id}/mcp-tools` 确认子工具项已正确录入。

**Acceptance Scenarios**:

1. **Given** 目标 MCP Server 可达且暴露了 3 个 Tool，**When** 提交 McpServer 类型的工具注册请求（含端点 URL 和 StreamableHttp Transport），**Then** 系统返回 HTTP 201，注册成功后异步完成 MCP 握手和 Tool 发现
2. **Given** MCP Server 已注册成功且 Tool 发现完成，**When** 请求 `GET /api/tools/{id}/mcp-tools`，**Then** 返回该 Server 暴露的 3 个 Tool 的 Schema 信息（name、description、inputSchema）
3. **Given** 目标 MCP Server 不可达（网络错误或端点无效），**When** 提交注册请求，**Then** 系统仍返回 HTTP 201 创建工具源记录（状态标记为 Inactive），但 Tool 发现标记为失败，错误信息记录在工具源详情中
4. **Given** 注册请求缺少 MCP Transport 类型配置，**When** 系统处理请求，**Then** 返回 HTTP 400，错误信息指出 McpServer 类型需要 transportType

---

### User Story 3 — 通过 OpenAPI 文档批量导入工具（Priority: P1）

作为平台管理员，我需要上传一份 OpenAPI/Swagger 文档（JSON 或 YAML 格式），系统自动解析文档中的每个接口（path + method）并批量生成对应的工具节点，以便快速接入一整套 REST API 服务而无需逐个手动注册。

上传后系统自动解析文档内容：从每个 path+method 组合提取工具名（优先使用 operationId，若缺失则拼接 method+path）、描述（summary 或 description）、输入 Schema（从 parameters 和 requestBody 合并）、输出 Schema（从 responses 的 200 响应提取）。批量生成 ToolRegistration 记录。同时可关联认证配置（适用于文档中所有导入的工具）。

**Why this priority**: OpenAPI 是现有 REST API 最广泛的描述格式。自动导入避免了管理员逐个注册数十甚至上百个接口的繁琐操作，是平台易用性的关键功能。

**Independent Test**: 上传一份包含多个接口的 OpenAPI JSON 文件，验证系统批量创建了对应数量的工具记录，每个工具的 Schema 信息与 OpenAPI 定义匹配。

**Acceptance Scenarios**:

1. **Given** 一份包含 5 个接口的 OpenAPI 3.0 JSON 文档（所有接口有 operationId），**When** 通过 `POST /api/tools/import-openapi` 上传该文档，**Then** 系统返回 HTTP 200，响应体包含 5 条新创建工具记录的摘要（id、name、status），每个工具名为对应的 operationId
2. **Given** 一份 OpenAPI 文档中部分接口缺少 operationId，**When** 上传该文档，**Then** 缺少 operationId 的工具名自动生成为 `{method}_{path}`（如 `GET_/api/users`），其余使用 operationId
3. **Given** 上传的文件不是有效的 OpenAPI/Swagger 格式（如普通 JSON），**When** 系统解析文档，**Then** 返回 HTTP 400，错误信息指出文档格式无效
4. **Given** 上传请求中附带了认证配置（如 ApiKey），**When** 导入成功，**Then** 所有批量生成的工具均关联该认证配置
5. **Given** 一份 OpenAPI YAML 文档，**When** 通过 `POST /api/tools/import-openapi` 上传，**Then** 系统能正确解析 YAML 格式并批量创建工具

---

### User Story 4 — 查询工具列表与详情（Priority: P1）

作为平台管理员或 Agent 编排模块，我需要查看已注册的所有工具列表，并能按类型过滤，以便了解平台当前可用的工具资源。同时，我需要获取单个工具的完整详情以查看其配置、Schema 和状态。

列表接口返回工具的摘要信息（ID、名称、类型、状态、创建时间），支持通过 `?type=RestApi` 或 `?type=McpServer` 过滤特定类型。详情接口返回工具的完整注册信息，包括连接配置、认证类型（凭据掩码）、Tool Schema 等。

**Why this priority**: 查询是所有消费方（前端管理页面、Agent 调用前的工具发现、工作流节点配置）的基础依赖。

**Independent Test**: 注册若干不同类型的工具后，`GET /api/tools` 返回完整列表；`GET /api/tools?type=RestApi` 仅返回 REST 类型；`GET /api/tools/{id}` 返回含完整配置的详情。

**Acceptance Scenarios**:

1. **Given** 系统中已注册 3 个 RestApi 工具和 2 个 McpServer 工具源，**When** 请求 `GET /api/tools`，**Then** 返回 HTTP 200 和包含 5 条记录的列表
2. **Given** 系统中已注册多种类型工具，**When** 请求 `GET /api/tools?type=McpServer`，**Then** 仅返回 toolType 为 McpServer 的工具列表
3. **Given** 系统中已注册一个含 ApiKey 认证的 RestApi 工具，**When** 请求 `GET /api/tools/{id}`，**Then** 返回完整详情，认证凭据字段显示掩码（如 `ak_***`）
4. **Given** 系统中不存在此 ID 的工具，**When** 请求 `GET /api/tools/{不存在的ID}`，**Then** 返回 HTTP 404

---

### User Story 5 — 更新与注销工具（Priority: P1）

作为平台管理员，我需要更新已注册工具的配置信息（如修改端点 URL、更换认证凭据、更新描述等），也需要注销不再使用的工具，以保持 Tool Gateway 中工具注册信息的准确性。

更新操作通过 `PUT /api/tools/{id}` 执行，支持修改名称、描述、连接配置和认证配置，但不允许变更 toolType。注销操作通过 `DELETE /api/tools/{id}` 执行，永久删除工具记录。若注销的是 McpServer 类型工具源，其关联的子工具项也一并删除。

**Why this priority**: 更新和注销是工具生命周期管理的必要组成部分。凭据轮换、端点迁移是日常运维高频操作。

**Independent Test**: 注册一个工具后通过 PUT 修改端点和凭据，确认更新生效。再通过 DELETE 注销，确认返回 404。

**Acceptance Scenarios**:

1. **Given** 系统中已注册一个 RestApi 工具，**When** 提交 `PUT /api/tools/{id}` 更新端点 URL 和认证凭据，**Then** 返回 HTTP 200，新配置已生效，updatedAt 已更新
2. **Given** 系统中已注册一个 RestApi 工具，**When** 提交 `PUT /api/tools/{id}` 试图将 toolType 从 RestApi 改为 McpServer，**Then** 返回 HTTP 400，错误信息指出工具类型不可变更
3. **Given** 系统中已注册一个 McpServer 工具源（含 3 个发现的子工具项），**When** 提交 `DELETE /api/tools/{id}`，**Then** 返回 HTTP 204，工具源和关联的 3 个子工具项均被删除
4. **Given** 系统中不存在此 ID 的工具，**When** 提交 `DELETE /api/tools/{id}`，**Then** 返回 HTTP 404

---

### User Story 6 — 统一调用工具（Priority: P1）

作为 Agent 或 Workflow 执行引擎，我需要通过统一的调用入口发起工具调用，Tool Gateway 自动处理认证注入、协议适配和结果标准化，使我无需了解底层工具的协议差异。

调用通过 `POST /api/tools/{id}/invoke` 发起，请求体包含调用参数（JSON 对象）。Gateway 自动完成以下流程：1) 根据工具 ID 查找注册记录 2) 从凭据存储获取并注入认证信息 3) 根据工具类型选择调用协议（REST API 使用 HTTP 请求、MCP Server 使用 `tools/call` 方法）4) 序列化请求并发起调用 5) 反序列化响应为标准化结构 6) 记录 OTel Span 7) 返回标准化结果。

**Why this priority**: 统一调用入口是 Tool Gateway 的核心价值——Agent 和 Workflow 无需关心底层协议细节，只需调用统一 API。这是 Agent Framework 中 `AIFunction` 抽象的服务端映射。

**Independent Test**: 分别对 RestApi 工具和 McpServer 工具发起 `POST /api/tools/{id}/invoke` 调用，验证 Gateway 自动完成认证注入和协议适配，返回标准化结果。

**Acceptance Scenarios**:

1. **Given** 系统中已注册一个 RestApi 工具（含 ApiKey 认证），目标端点可达，**When** 提交 `POST /api/tools/{id}/invoke`（含调用参数），**Then** Gateway 自动将 ApiKey 注入请求头，发起 HTTP 调用，返回 HTTP 200 和标准化结果（含 data 和 metadata）
2. **Given** 系统中已注册一个 McpServer 工具源，其中一个子工具的 toolName 为 "query_logs"，**When** 提交 `POST /api/tools/{parentId}/invoke`（指定 toolName 和调用参数），**Then** Gateway 通过 MCP 协议发起 `tools/call` 调用，返回标准化结果
3. **Given** 目标工具的端点不可达（网络超时），**When** 发起调用，**Then** 返回 HTTP 502 Bad Gateway，错误信息包含超时详情
4. **Given** 工具 ID 不存在，**When** 发起调用，**Then** 返回 HTTP 404
5. **Given** 调用参数不符合工具的 InputSchema 定义，**When** 发起调用，**Then** 返回 HTTP 400，错误信息说明参数校验失败

---

### Edge Cases

- 工具名称中包含特殊字符（如 `<script>`、SQL 注入片段）时，系统正常处理并存储（数据库参数化查询防注入），不做内容过滤
- 工具名称长度超过 200 字符时，系统返回 HTTP 400
- 工具描述为空字符串时，系统接受注册（描述为可选字段）
- 并发注册同名工具时，仅第一个成功，后续请求返回 409 Conflict
- 请求体 JSON 格式错误时，系统返回 HTTP 400 并提示解析失败
- OpenAPI 文档大小超过 10MB 时，系统返回 HTTP 413 Payload Too Large
- OpenAPI 文档中包含 0 个有效路径时，系统返回 HTTP 400 并提示文档中无可导入的接口
- MCP Server 握手超时（超过 30 秒无响应）时，工具源记录仍创建但状态为 Inactive，错误信息记录握手失败原因
- 调用 McpServer 子工具时指定了不存在的 toolName，系统返回 HTTP 400 并列出可用的 toolName
- 认证配置中 authType 为 OAuth2 时，系统目前仅存储 Client Credentials 配置（clientId、clientSecret、tokenEndpoint）以支持 M2M 场景
- 统一调用时，若目标工具状态为 Inactive，系统返回 HTTP 503 Service Unavailable 并提示工具不可用
- OpenAPI 导入时，若文档引用了外部 `$ref` 且无法解析，系统跳过该接口并在响应中报告警告

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: 系统必须提供统一的工具注册入口 `POST /api/tools`，接受包含 toolType 字段的注册请求，根据类型值（RestApi / McpServer）分派到对应的校验与存储逻辑
- **FR-002**: 系统必须支持注册 RestApi 类型工具，要求提交 name、toolType、连接配置（endpoint URL）和认证配置（authType + 凭据），其中 name、endpoint 为必填
- **FR-003**: 系统必须支持注册 McpServer 类型工具源，要求提交 name、toolType、连接配置（endpoint URL、transportType: StreamableHttp/Stdio），其中 name、endpoint、transportType 为必填
- **FR-004**: 注册成功后，系统必须为工具分配唯一 ID，RestApi 类型初始状态为 Active，McpServer 类型初始状态取决于握手结果（成功为 Active，失败为 Inactive）
- **FR-005**: 系统必须保证工具名称在全局范围内唯一，重复名称注册返回冲突错误
- **FR-006**: 系统必须对认证凭据进行加密存储，凭据在任何查询接口中不以明文返回，使用掩码替代
- **FR-007**: McpServer 类型工具注册后，系统必须异步执行 MCP `initialize` 握手和 `tools/list` 发现流程，将发现的 Tool 信息（name、description、inputSchema、annotations）存储为子工具项
- **FR-008**: 系统必须提供 `GET /api/tools/{id}/mcp-tools` 端点，返回 McpServer 工具源下所有已发现的子工具项列表
- **FR-009**: 系统必须提供 OpenAPI 文档导入端点 `POST /api/tools/import-openapi`，接受 JSON 或 YAML 格式的 OpenAPI/Swagger 文档（文件上传或 URL）
- **FR-010**: OpenAPI 导入时，系统必须将文档中每个 path+method 组合解析为一个独立的工具节点，提取 operationId（或拼接 method+path）作为工具名、summary 作为描述、parameters+requestBody 合并为 InputSchema、responses 中 200 响应作为 OutputSchema
- **FR-011**: 系统必须提供工具列表查询接口 `GET /api/tools`，返回所有已注册工具的摘要信息，支持通过 `?type=` 查询参数按 toolType 过滤
- **FR-012**: 系统必须提供工具详情查询接口 `GET /api/tools/{id}`，返回工具的完整注册信息，包括连接配置、认证类型（凭据掩码）和 ToolSchema
- **FR-013**: 系统必须提供工具更新接口 `PUT /api/tools/{id}`，支持修改名称、描述、连接配置和认证配置，但不允许变更 toolType
- **FR-014**: 系统必须提供工具注销接口 `DELETE /api/tools/{id}`，永久删除工具记录；若目标为 McpServer 类型，其关联的子工具项也一并删除
- **FR-015**: 系统必须提供统一工具调用接口 `POST /api/tools/{id}/invoke`，接受调用参数（JSON），自动完成认证注入、协议适配（REST HTTP 或 MCP tools/call）、结果标准化
- **FR-016**: 统一调用接口必须根据工具类型自动选择调用协议——RestApi 类型转换为 HTTP 请求（注入认证头），McpServer 类型转换为 MCP `tools/call` 调用
- **FR-017**: 统一调用接口必须返回标准化的调用结果结构，包含 data（响应数据）、metadata（耗时、状态码等元信息）和 isSuccess 标志
- **FR-018**: 统一调用时，系统必须自动生成 OpenTelemetry Trace Span，记录工具名称、调用耗时、状态等属性
- **FR-019**: 所有涉及 ID 查找的操作（详情、更新、注销、调用），当目标工具不存在时返回 Not Found 错误
- **FR-020**: 所有写入操作的请求数据不符合校验规则时，返回结构化的错误信息，包含具体的字段级错误描述

### Key Entities

- **ToolRegistration**（聚合根）: 代表一个已注册的工具或工具源。包含通用属性（ID、名称、描述、类型、状态、时间戳）以及连接配置、认证配置和 Tool Schema。是 Tool Gateway 模块的核心实体，所有工具生命周期操作的入口。
- **ToolType**（枚举）: 区分工具的两种主要类型——RestApi（外部 REST API 工具）、McpServer（MCP Server 工具源）。一旦注册后不可变更。
- **ToolStatus**（枚举）: 表示工具的运行状态——Active（可用）、Inactive（不可用，如 MCP 握手失败）、CircuitOpen（熔断中，由后续 SPEC-014 处理）。
- **ConnectionConfigVO**（值对象）: 工具的连接配置，包含端点 URL（Endpoint）、协议类型（Protocol: Http/Mcp）和传输类型（TransportType: Rest/StreamableHttp/Stdio）。
- **AuthConfigVO**（值对象）: 工具的认证配置，包含认证类型（AuthType: None/ApiKey/Bearer/OAuth2）和加密的凭据引用（CredentialRef）。凭据在存储时加密，查询时返回掩码。
- **ToolSchemaVO**（值对象）: 工具的输入/输出 Schema 描述，包含 InputSchema（JSON Schema）、OutputSchema（JSON Schema）和 Annotations（readOnly、destructive、idempotent 等标注）。对于 MCP 工具，Schema 从 MCP Tool 定义映射；对于 OpenAPI 导入的工具，Schema 从 OpenAPI 文档映射。
- **McpToolItem**（实体）: MCP Server 工具源下发现的单个 Tool。包含 toolName、description、inputSchema、annotations 等属性。关联到父 ToolRegistration（McpServer 类型）。
- **ToolInvocation**（值对象）: 记录一次工具调用的上下文信息，包含调用者标识、目标工具、请求参数、响应结果、耗时和状态。用于构建 OTel Span 和审计数据。

## Assumptions

- **凭据加密方案**: 凭据使用对称加密存储。不在本 Spec 中指定具体加密算法或密钥管理基础设施，但凭据必须在存储前加密、查询时返回掩码。
- **MCP 握手异步执行**: McpServer 注册后的 initialize 握手和 tools/list 发现为异步操作（后台任务）。注册 API 立即返回，不等待握手完成。工具源状态和发现结果通过后续查询获取。
- **OpenAPI 版本支持**: 支持 OpenAPI 3.0 和 Swagger 2.0 格式。
- **认证鉴权**: 本 Spec 不涉及 API 端点自身的认证鉴权（无 JWT 保护）。Tool Gateway API 的访问控制由 SPEC-049（身份认证与 RBAC）统一处理。
- **分页**: 工具列表查询初期不实现分页（预期早期工具数量 < 200）。当工具数量增长时，在后续迭代中添加分页支持。
- **软删除 vs 硬删除**: 注销操作执行硬删除（物理删除数据库记录），非软删除。审计需求由 SPEC-015（工具调用审计日志）和 SPEC-052（操作审计日志）满足。
- **OAuth2 范围**: OAuth2 认证当前仅支持 Client Credentials Grant（M2M 场景），不支持 Authorization Code Grant（需用户交互的场景）。
- **MCP Transport**: MCP 连接当前支持 StreamableHttp 和 Stdio 两种 Transport 类型。Stdio 类型需要系统有权限启动对应的命令行进程。
- **配额与熔断**: 本 Spec 不实现调用配额管理和熔断策略，这些由 SPEC-014（工具配额管理与熔断）在后续迭代处理。ToolRegistration 的 QuotaConfig 和 CircuitBreakerConfig 字段暂不填充。
- **审计日志**: 统一调用时记录 OTel Span 用于链路追踪，但不在本 Spec 中实现详细的调用审计日志持久化（由 SPEC-015 处理）。
- **OpenAPI 导入基础 URL**: 通过 OpenAPI 文档导入的工具，其端点 URL 从文档中的 `servers` 字段提取。若文档缺少 `servers` 字段，系统要求在导入请求中手动指定基础 URL。

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 平台可成功注册 RestApi 和 McpServer 两种类型的工具，并通过列表接口查询到全部类型
- **SC-002**: 管理员上传 OpenAPI 文档后，系统在 10 秒内完成解析和批量工具创建（文档包含 ≤ 50 个接口）
- **SC-003**: 凭据信息 100% 加密存储，任何查询接口均不返回明文凭据
- **SC-004**: 通过统一调用接口调用 RestApi 工具和 McpServer 工具，调用方无需感知底层协议差异，均通过相同的请求/响应格式交互
- **SC-005**: 工具注册操作（从提交到收到响应）在 1 秒内完成（不含 MCP 异步握手时间）
- **SC-006**: 工具列表查询在注册数量 ≤ 200 条时，响应时间在 500 毫秒以内
- **SC-007**: 非法请求（缺少必填字段、格式错误）100% 返回结构化错误信息，用户无需查阅文档即可理解错误原因
- **SC-008**: 完整工具生命周期（注册→查询→更新→调用→注销）可在 5 分钟内通过 API 端到端验证完成