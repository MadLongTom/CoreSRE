# Data Model: Agent 能力语义搜索

**Feature**: 003-agent-semantic-search  
**Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## Existing Entities (Read-Only in This Feature)

### AgentRegistration (Aggregate Root)

> 已在 SPEC-001 中实现，本 Spec 不修改其结构。搜索操作以只读方式访问。

| Field | Type | Notes |
|-------|------|-------|
| Id | `Guid` | PK |
| Name | `string` | Unique, required |
| AgentType | `AgentType` (enum) | A2A / ChatClient / Workflow |
| Status | `AgentStatus` (enum) | Active / Inactive / Error |
| AgentCard | `AgentCardVO?` | JSONB column `agent_card`, nullable (only A2A) |
| CreatedAt | `DateTime` | UTC |
| UpdatedAt | `DateTime?` | UTC |

### AgentSkillVO (Value Object, nested in AgentCardVO)

> 搜索的最小匹配粒度。嵌套在 `AgentCardVO.Skills` 列表中，存储为 JSONB 数组元素。

| Field | Type | Notes |
|-------|------|-------|
| Name | `string` | Required, skill 标识名 |
| Description | `string?` | Optional, skill 描述文本 |

**JSONB 存储结构** (column `agent_card`):
```json
{
  "Skills": [
    { "Name": "answer-customer-questions", "Description": "Answer questions about products and orders" },
    { "Name": "escalate-ticket", "Description": "Escalate support tickets to senior agents" }
  ],
  "Interfaces": [...],
  "SecuritySchemes": [...]
}
```

## New DTOs (P1: Keyword Search)

### AgentSearchResultDto

> 搜索结果中的单个 Agent 条目。包含 Agent 摘要信息 + 匹配到的 skill 列表。

| Field | Type | Notes |
|-------|------|-------|
| Id | `Guid` | Agent ID |
| Name | `string` | Agent 名称 |
| AgentType | `string` | "A2A" (搜索结果始终为 A2A 类型) |
| Status | `string` | Agent 当前状态 |
| CreatedAt | `DateTime` | Agent 注册时间 |
| MatchedSkills | `List<MatchedSkillDto>` | 与搜索词匹配的 skill 列表 |
| SimilarityScore | `double?` | P2 only: 语义相似度评分（0.0~1.0），关键词模式为 null |

**设计决策**: 不继承/扩展 `AgentSummaryDto`。虽然前 5 个字段与 `AgentSummaryDto` 相同，但 `AgentSearchResultDto` 有独立的语义（搜索结果 vs 列表摘要），且包含额外的 `MatchedSkills` 和 `SimilarityScore` 字段。分离 DTO 遵循 SRP，避免 `AgentSummaryDto` 因搜索功能引入可选字段。

### MatchedSkillDto

> 单个匹配到的 skill 详情。

| Field | Type | Notes |
|-------|------|-------|
| Name | `string` | Skill 名称 |
| Description | `string?` | Skill 描述 |

### AgentSearchResponse

> 搜索 API 的响应信封，包裹搜索结果列表和元数据。

| Field | Type | Notes |
|-------|------|-------|
| Results | `List<AgentSearchResultDto>` | 匹配 Agent 列表，按相关性排序 |
| SearchMode | `string` | `"keyword"` / `"semantic"` / `"keyword-fallback"` |
| Query | `string` | 原始搜索文本，回显给调用方 |
| TotalCount | `int` | 匹配 Agent 总数 |

## New Entity (P2: Semantic Search — Future)

### SkillEmbedding

> P2 实现时新增。存储 skill description 的向量嵌入，用于余弦相似度搜索。

| Field | Type | Notes |
|-------|------|-------|
| Id | `Guid` | PK |
| AgentId | `Guid` | FK → AgentRegistration.Id, CASCADE DELETE |
| SkillName | `string` | 对应 AgentSkillVO.Name |
| SkillDescription | `string` | 生成嵌入时使用的原始文本（快照） |
| Embedding | `Vector(1536)` | pgvector 向量类型，维度取决于 embedding model |
| GeneratedAt | `DateTime` | 嵌入计算时间 (UTC) |

**约束**:
- Composite unique index: `(AgentId, SkillName)` — 每个 Agent 的每个 skill 最多一条嵌入记录
- FK 约束 + CASCADE DELETE — Agent 删除时自动清理其嵌入记录
- 向量维度由 DI 注册的 `IEmbeddingGenerator` 决定，默认 1536（OpenAI text-embedding-3-small 维度）

**EF Core 配置** (P2):
```csharp
builder.HasOne<AgentRegistration>()
    .WithMany()
    .HasForeignKey(e => e.AgentId)
    .OnDelete(DeleteBehavior.Cascade);

builder.HasIndex(e => new { e.AgentId, e.SkillName }).IsUnique();
builder.Property(e => e.Embedding).HasColumnType("vector(1536)");
```

## Repository Interface Extension

### IAgentRegistrationRepository — New Method

```csharp
/// <summary>
/// 按 skill name/description 关键词搜索 A2A Agent
/// </summary>
/// <param name="searchTerm">搜索关键词，大小写不敏感</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>匹配的 Agent 列表（仅 A2A 类型，AgentCard 非 null）</returns>
Task<IReadOnlyList<AgentRegistration>> SearchBySkillAsync(
    string searchTerm,
    CancellationToken cancellationToken = default);
```

**实现策略** (Infrastructure 层):
1. 使用 raw SQL (`FromSqlInterpolated`) 查询含匹配 skill 的 Agent ID 列表
2. 用标准 EF Core 按 ID 列表加载完整 Entity（确保 owned JSON 导航属性正确加载）
3. 在 C# 层面从每个 Agent 的 AgentCard.Skills 中提取匹配的 skill 列表（由 QueryHandler 完成）

## CQRS Query

### SearchAgentsQuery (MediatR IRequest)

```csharp
public record SearchAgentsQuery(string Query) : IRequest<Result<AgentSearchResponse>>;
```

### SearchAgentsQueryValidator (FluentValidation)

| Rule | Validation |
|------|------------|
| Query | NotEmpty, MaximumLength(500), Must not be whitespace-only |

### SearchAgentsQueryHandler Logic

1. 调用 `repository.SearchBySkillAsync(query.Query)` 获取匹配 Agent 列表
2. 对每个 Agent 的 `AgentCard.Skills`，在 C# 中过滤出 Name 或 Description 包含搜索词的 skill → `MatchedSkillDto` 列表
3. 构建 `AgentSearchResultDto`（含 MatchedSkills），按 `MatchedSkills.Count` 降序排列
4. 包装为 `AgentSearchResponse`（SearchMode = "keyword"）返回
