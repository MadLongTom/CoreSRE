# Research: Agent 注册与 CRUD 管理（多类型）

**Feature**: 002-agent-registry-crud  
**Date**: 2026-02-09  
**Purpose**: Resolve all NEEDS CLARIFICATION items and document technology decisions

---

## R1: EF Core Value Object Storage Strategy

**Question**: AgentRegistration 包含嵌套集合的值对象（AgentCardVO 含 skills/interfaces/securitySchemes 列表），如何在 EF Core + PostgreSQL 中映射？

**Decision**: 使用 EF Core `ToJson()` 将复杂值对象存储为 PostgreSQL JSONB 列

**Rationale**:
- JSONB 天然支持值对象的不可变语义——更新时整体替换 JSON 文档
- 单表设计，AgentCardVO（含 3 个子集合）仅需 1 个 `agent_card` JSONB 列，而非 4+ 张关联表
- 集合规模小（0-20 条 skills/interfaces），无需关系型索引优化
- 主查询过滤条件 `agentType` 是普通列（非 JSON 内字段），不受 JSONB 查询限制
- EF Core 10 + Npgsql 10 对 `ToJson()` 支持成熟（GA since EF Core 7）

**Alternatives Considered**:
- `OwnsOne/OwnsMany`（关系型映射）——为 AgentCardVO 的 3 个子集合生成 3 张 join 表，schema 复杂度过高
- 独立表 + 外键——违背值对象无标识的 DDD 原则，且增加不必要的 JOIN 开销

**Concrete Mapping**:

| Column | PostgreSQL Type | Notes |
|--------|----------------|-------|
| id | uuid PK | From BaseEntity |
| name | varchar(200) | Unique index |
| description | text | Nullable |
| agent_type | varchar(20) | Enum stored as string, index |
| status | varchar(20) | Enum stored as string |
| endpoint | varchar(2048) | Nullable, A2A only |
| agent_card | jsonb | Nullable, A2A only — contains skills/interfaces/securitySchemes |
| llm_config | jsonb | Nullable, ChatClient only — contains modelId/instructions/toolRefs |
| workflow_ref | uuid | Nullable, Workflow only |
| created_at | timestamptz | From BaseEntity |
| updated_at | timestamptz | From BaseEntity, nullable |

---

## R2: Agent Name Uniqueness Enforcement

**Question**: 如何在并发场景下保证 Agent 名称全局唯一？

**Decision**: 数据库层 Unique Index + 应用层 DbUpdateException 捕获

**Rationale**:
- 数据库唯一索引是唯一无竞态条件的方案，应用层 "先查后插" 有 TOCTOU 漏洞
- Npgsql 在唯一约束冲突时抛出 `DbUpdateException`，内层为 `PostgresException`（`SqlState = "23505"`）
- 在 Command Handler 中捕获异常，返回 `Result<T>.Conflict()` 语义化错误

**Alternatives Considered**:
- 全局异常中间件统一处理 23505——粒度太粗，无法生成友好的字段级错误消息
- 分布式锁——过度工程化，单库场景下数据库约束足够

---

## R3: Entity Design — Discriminated Aggregate vs TPH Inheritance

**Question**: 三种 Agent 类型（A2A/ChatClient/Workflow）使用单实体 + 类型鉴别，还是 TPH 继承体系？

**Decision**: 单一 `AgentRegistration` 实体 + `AgentType` 枚举鉴别器 + 可空类型特有值对象

**Rationale**:
- 三种类型共享相同的生命周期行为（Register → Query → Update → Delete），差异仅在数据形状
- 统一 CRUD 端点、统一仓储、统一查询——单实体最自然
- 工厂方法模式（`CreateA2A` / `CreateChatClient` / `CreateWorkflow`）在构造时保证类型一致性
- TPH 在 EF Core 中变更鉴别器列值不受支持，但我们的 spec 也禁止变更类型——不构成优势
- `ToJson()` 映射在单实体上更简单，TPH 派生类上的 JSON 列配置存在边缘问题

**Alternatives Considered**:
- TPH 继承（`A2AAgentRegistration : AgentRegistration`）——增加 3 个子类 + EF 配置复杂度，但生命周期行为完全相同，无多态需求
- 每类型一张表（TPT）——违反 "统一查询接口" 的 spec 需求

---

## R4: Result<T> Error Type Extension

**Question**: 现有 `Result<T>` 仅有 `Success/Fail`，如何区分 400 vs 404 vs 409 等不同错误语义？

**Decision**: 在 `Result<T>` 中增加 `ErrorCode` 属性（int?），用于表达 HTTP 状态码语义

**Rationale**:
- Minimal API endpoint 需根据不同错误类型返回不同 HTTP 状态码
- `ErrorCode` 保持 Application 层对 HTTP 无感知（仅是整数语义），但 API 层可直接映射
- 新增 `Result<T>.NotFound()` / `Result<T>.Conflict()` 工厂方法提供便捷 API
- 避免字符串匹配（如 `Message.Contains("already exists")`）来判断错误类型

**Alternatives Considered**:
- 枚举 `ErrorType`（Validation/NotFound/Conflict）——同样可行，但 int 更灵活（可表达任意 HTTP 状态码）
- 抛异常 + 全局中间件——在 CQRS 模式中失去了显式返回值的可读性

---

## R5: IAgentRegistrationRepository Extension

**Question**: 现有 `IRepository<T>` 的 `GetAllAsync()` 不支持按条件过滤，Agent 列表需要 `?type=` 过滤，如何处理？

**Decision**: 新建 `IAgentRegistrationRepository` 接口扩展 `IRepository<AgentRegistration>`，添加 `GetByTypeAsync` 方法

**Rationale**:
- 遵循 Constitution §V（接口先于实现）和 §III（仓储接口定义在 Domain 层）
- 避免在泛型仓储上暴露 `IQueryable`（会泄漏持久化细节到 Domain 层）
- 专用方法 `GetByTypeAsync(AgentType? type)` 语义清晰，type 为 null 时返回全部
- 保持泛型仓储的简洁性，领域特有查询通过扩展接口实现

**Alternatives Considered**:
- 在 `IRepository<T>` 上添加 `FindAsync(Expression<Func<T, bool>>)` ——泄漏 LINQ 到 Domain 层
- Specification Pattern——对单一过滤条件过度工程化

---

## R6: Validation Architecture — FluentValidation + Domain Guards

**Question**: 校验逻辑放在 FluentValidation（Application 层）还是 Domain 工厂方法，还是两者都有？

**Decision**: 两层校验——FluentValidation 做请求结构校验，Domain 工厂方法做业务不变量守卫

**Rationale**:
- **FluentValidation**（Application 层）：校验请求数据的结构合法性——字段非空、长度限制、枚举值合法、按类型条件校验必填字段。通过 `ValidationBehavior` pipeline 自动执行，抛出 `ValidationException`
- **Domain 工厂方法**：守卫业务不变量——`ArgumentException.ThrowIfNullOrWhiteSpace(name)`、`ArgumentNullException.ThrowIfNull(agentCard)`。这是最后一道防线，防止跳过 Application 层直接创建非法实体
- 两层互补：FluentValidation 生成用户友好的错误消息（国际化、字段级），Domain guards 保证领域模型永远有效

**Alternatives Considered**:
- 仅 Domain 校验——错误消息不够结构化，无法生成字段级错误列表
- 仅 FluentValidation——Domain 模型可被绕过 Application 层直接实例化

---

## R7: Minimal API Endpoint Organization

**Question**: Agent 端点如何组织？每个端点单独声明还是用 `MapGroup`？

**Decision**: 使用 `MapGroup("/api/agents")` + 静态扩展方法 `MapAgentEndpoints()`

**Rationale**:
- `MapGroup` 是 .NET 8+ 官方推荐的端点组织模式，避免重复前缀
- 扩展方法 `app.MapAgentEndpoints()` 保持 `Program.cs` 整洁，一行注册
- 私有静态方法作为处理函数，仅负责 MediatR 命令分发 + HTTP 结果映射
- 所有端点共享 `.WithTags("Agents")` 和 `.WithOpenApi()` 声明

**Alternatives Considered**:
- Carter 库——引入额外依赖，项目规模不大不需要
- Controller-based——项目已确定使用 Minimal API

---

## R8: Exception Handling Middleware

**Question**: FluentValidation 的 `ValidationException` 如何映射到 HTTP 400？

**Decision**: 自定义 `ExceptionHandlingMiddleware` 全局捕获 `ValidationException`，返回结构化的 `Result<object>`

**Rationale**:
- 当前 `ValidationBehavior` 抛出 `ValidationException`，需要在 HTTP 管道中捕获
- 中间件方式统一处理所有端点的验证异常，避免每个端点手动 try-catch
- 返回 `Result<object>.Fail("Validation failed.", errors)` 格式与正常 Result 响应一致
- 可扩展处理其他异常类型（如 `ArgumentException` → 400）

**Alternatives Considered**:
- `IExceptionHandler`（.NET 8+）——同样可行，但需要 `AddProblemDetails()` 的集成，增加配置复杂度
- endpoint 级 try-catch——重复代码，容易遗漏
