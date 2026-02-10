# Research: Agent 能力语义搜索

**Feature**: 003-agent-semantic-search  
**Date**: 2026-02-10  
**Purpose**: Resolve all technical questions for SPEC-003 implementation

---

## R1: PostgreSQL JSONB Text Search within Array Elements

**Question**: How to query PostgreSQL JSONB array elements for case-insensitive substring matching on `Skills[*].Name` and `Skills[*].Description`?

### Actual JSONB Structure in Database

Based on the EF Core migration snapshot, the `agent_card` column stores:
```json
{
  "Skills": [
    { "Name": "answer-customer-questions", "Description": "Answer questions about products" },
    { "Name": "code-review", "Description": "Review code for bugs" }
  ],
  "Interfaces": [...],
  "SecuritySchemes": [...]
}
```

### Approach 1: `EXISTS` + `jsonb_array_elements()` + `ILIKE` (Recommended)

```sql
SELECT ar.*
FROM agent_registrations ar
WHERE ar.agent_type = 'A2A'
  AND ar.agent_card IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
    WHERE skill ->> 'Name' ILIKE '%' || @query || '%'
       OR skill ->> 'Description' ILIKE '%' || @query || '%'
  );
```

**How it works**:
- `agent_card -> 'Skills'` extracts the JSON array as `jsonb`
- `jsonb_array_elements(...)` unnests the array into rows (one per skill)
- `skill ->> 'Name'` extracts the text value of the `Name` key
- `ILIKE '%...%'` performs case-insensitive substring matching
- `EXISTS` short-circuits after the first match — efficient for "does any skill match?"

### Approach 2: `@>` Containment Operator

```sql
-- Only works for EXACT match, NOT substring/ILIKE
SELECT * FROM agent_registrations
WHERE agent_card @> '{"Skills": [{"Name": "exact-name"}]}';
```

**Verdict**: ❌ Not suitable. The `@>` operator only does exact-value containment checks. It cannot do case-insensitive substring matching (`ILIKE`). Ruled out for P1 keyword search.

### Approach 3: Full-text Search on JSONB

```sql
-- Convert all text in skills array to tsvector, but loses field-level granularity
SELECT * FROM agent_registrations
WHERE to_tsvector('english', agent_card -> 'Skills') @@ plainto_tsquery('customer');
```

**Verdict**: ❌ Overkill for our use case. Requires GIN index setup, loses field-level granularity, and `to_tsvector` on a JSONB array isn't straightforward. Better suited for large text corpus.

### Performance for Small Datasets

For the project's scale (< 100 rows, < 20 skills per row):
- **Full table scan + `jsonb_array_elements` is perfectly acceptable**. At this scale, sequential scan with JSONB unnesting completes in single-digit milliseconds.
- No GIN index or specialized indexing needed.
- The `WHERE agent_type = 'A2A'` filter (on an indexed column) pre-narrows the result set significantly.

### Decision: Use Approach 1

`EXISTS (SELECT 1 FROM jsonb_array_elements(...) WHERE ... ILIKE ...)` is the correct, performant approach. It supports case-insensitive substring matching, is parameterizable (preventing SQL injection), and is straightforward to express in raw SQL.

### Counting Matched Skills (for sorting by relevance)

To sort results by number of matching skills:
```sql
SELECT ar.*,
       (SELECT COUNT(*)
        FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
        WHERE skill ->> 'Name' ILIKE '%' || @query || '%'
           OR skill ->> 'Description' ILIKE '%' || @query || '%'
       ) AS matched_skill_count
FROM agent_registrations ar
WHERE ar.agent_type = 'A2A'
  AND ar.agent_card IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
    WHERE skill ->> 'Name' ILIKE '%' || @query || '%'
       OR skill ->> 'Description' ILIKE '%' || @query || '%'
  )
ORDER BY matched_skill_count DESC;
```

### Extracting Matched Skills (for response DTO)

To also return which skills matched:
```sql
SELECT ar.id, ar.name, ar.agent_type, ar.status, ar.created_at,
       skill_match.name AS skill_name,
       skill_match.description AS skill_description
FROM agent_registrations ar,
LATERAL (
    SELECT skill ->> 'Name' AS name,
           skill ->> 'Description' AS description
    FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
    WHERE skill ->> 'Name' ILIKE '%' || @query || '%'
       OR skill ->> 'Description' ILIKE '%' || @query || '%'
) AS skill_match
WHERE ar.agent_type = 'A2A'
  AND ar.agent_card IS NOT NULL;
```

However, this denormalizes results (one row per matched skill). For the in-memory grouping approach described in R2, it's simpler to load full entities and filter skills in C#.

---

## R2: EF Core Raw SQL / Interpolated SQL for JSONB Queries

**Question**: Can EF Core 10 LINQ translate `OwnsMany` JSONB array element text searches? If not, what's the fallback?

### Can LINQ Translate `.Any(s => s.Name.Contains(query))`?

**Testing required, but likely NO for `ILIKE`-style substring search on JSONB arrays.**

EF Core 10 + Npgsql 10 with `ToJson()` DOES support basic LINQ queries into JSON columns — accessing scalar properties, filtering by exact values, and navigating owned entities. The Npgsql EF Core documentation confirms: *"ToJson mapping supports far more querying patterns than both legacy POCO and DOM."*

However, the specific pattern:
```csharp
.Where(a => a.AgentCard!.Skills.Any(s => s.Name.Contains(query)))
```
involves:
1. Navigation into a JSONB column (`AgentCard`)
2. `.Any()` over an `OwnsMany` collection inside that JSON
3. `string.Contains()` (which needs to translate to `ILIKE` or `LIKE`)

**Current EF Core / Npgsql support for LINQ over JSON arrays is limited** — querying into owned collections within JSON via `.Any()` with string functions is an area where translation may fail at runtime with `InvalidOperationException`. The Npgsql documentation does not list `.Any()` on JSON array sub-entities among supported translations.

### Recommended Strategy: Hybrid Approach

**Step 1 — Use raw SQL for filtering (find matching agent IDs):**
```csharp
var matchingIds = await context.Database
    .SqlQuery<Guid>($"""
        SELECT ar.id AS "Value"
        FROM agent_registrations ar
        WHERE ar.agent_type = 'A2A'
          AND ar.agent_card IS NOT NULL
          AND EXISTS (
            SELECT 1
            FROM jsonb_array_elements(ar.agent_card -> 'Skills') AS skill
            WHERE skill ->> 'Name' ILIKE '%' || {query} || '%'
               OR skill ->> 'Description' ILIKE '%' || {query} || '%'
          )
        """)
    .ToListAsync(cancellationToken);
```

**Key point**: `SqlQuery<T>` uses `FormattableString` — `{query}` is automatically parameterized as `@p0`, **preventing SQL injection**. The output column must be named `"Value"` for scalar `SqlQuery<T>`.

**Step 2 — Load full entities via LINQ:**
```csharp
var agents = await context.AgentRegistrations
    .Where(a => matchingIds.Contains(a.Id))
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

**Step 3 — Extract matched skills in C# (post-query):**
```csharp
var results = agents.Select(a => new AgentSearchResultDto
{
    Id = a.Id,
    Name = a.Name,
    AgentType = a.AgentType.ToString(),
    Status = a.Status.ToString(),
    CreatedAt = a.CreatedAt,
    MatchedSkills = a.AgentCard!.Skills
        .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                  || (s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
        .Select(s => new MatchedSkillDto { Name = s.Name, Description = s.Description })
        .ToList()
})
.OrderByDescending(r => r.MatchedSkills.Count)
.ToList();
```

### Why Not `FromSql` / `FromSqlInterpolated`?

`FromSql` works on `DbSet<TEntity>` — it returns full entity types. But it requires the SQL to return **all columns** the entity maps to, and EF Core handles owned-entity/JSON deserialization automatically. However, writing a `FromSql` that correctly returns the JSON columns with proper naming for EF Core to materialize them is fragile and tightly coupled to the table schema.

The **two-step approach** (SqlQuery for IDs → LINQ for entities) is cleaner because:
- Step 1: leverages PostgreSQL's JSONB power for filtering
- Step 2: leverages EF Core's full materialization pipeline including JSON deserialization
- Step 3: leverages C# for the matched-skills extraction, avoiding complex SQL

### Alternative: Single FromSql Query

If testing shows `FromSql` works (which it should for simple SELECT * with JSONB):
```csharp
var agents = await context.AgentRegistrations
    .FromSql($"""
        SELECT *
        FROM agent_registrations
        WHERE agent_type = 'A2A'
          AND agent_card IS NOT NULL
          AND EXISTS (
            SELECT 1
            FROM jsonb_array_elements(agent_card -> 'Skills') AS skill
            WHERE skill ->> 'Name' ILIKE '%' || {query} || '%'
               OR skill ->> 'Description' ILIKE '%' || {query} || '%'
          )
        """)
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

This is simpler and worth trying first. Since `FromSql` returns `IQueryable<AgentRegistration>`, EF Core will materialize the full entity including the JSONB `AgentCard` with its owned collections. The only risk is that EF Core may not handle the `SELECT *` from the raw SQL subquery properly with owned entities — but this should work because the SQL returns the exact same column set as a normal `DbSet<AgentRegistration>` query.

### Integration with AutoMapper

The in-memory mapping from `AgentRegistration` → DTO is done **after** the database query, so AutoMapper can be used normally. However, for the search result DTO specifically, manual projection (as in Step 3 above) is cleaner because:
- The matched skills list requires filter logic based on the search query
- AutoMapper is better suited for 1:1 entity-to-DTO mapping without conditional logic

**Recommendation**: Use AutoMapper for the agent summary fields, and manual projection for `MatchedSkills`.

### Decision

1. **Try LINQ first**: Test `Where(a => a.AgentCard!.Skills.Any(s => EF.Functions.ILike(s.Name, $"%{query}%")))`. If it translates, it's the cleanest option.
2. **Fallback to `FromSql`**: Use the single `FromSql` approach with `SELECT *` + EXISTS subquery.
3. **Last resort**: Two-step SqlQuery<Guid> + LINQ Contains approach.

---

## R3: Microsoft.Extensions.AI.Abstractions — IEmbeddingGenerator

### Package Information

| Property | Value |
|----------|-------|
| **Interface** | `IEmbeddingGenerator<TInput, TEmbedding>` |
| **NuGet Package** | `Microsoft.Extensions.AI.Abstractions` |
| **Latest Version** | **10.2.0** (for .NET 10) |
| **Namespace** | `Microsoft.Extensions.AI` |
| **Assembly** | `Microsoft.Extensions.AI.Abstractions.dll` |
| **Source** | [dotnet/extensions repo](https://github.com/dotnet/extensions) |

The higher-level package `Microsoft.Extensions.AI` (also v10.2.0) includes the abstractions plus middleware builders (caching, telemetry, rate limiting).

### Interface Definition

```csharp
public interface IEmbeddingGenerator<in TInput, TEmbedding> : IDisposable, IEmbeddingGenerator
    where TEmbedding : Embedding
{
    Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(
        IEnumerable<TInput> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

For our use case, the concrete type is:
```csharp
IEmbeddingGenerator<string, Embedding<float>>
```

### Extension Methods for Common Use

```csharp
// Generate embedding for a SINGLE string → ReadOnlyMemory<float>
ReadOnlyMemory<float> vector = await generator.GenerateVectorAsync("some text");

// Generate embeddings for MULTIPLE strings
GeneratedEmbeddings<Embedding<float>> embeddings =
    await generator.GenerateAsync(["text1", "text2"]);

// Access the float[] from an embedding
float[] array = embedding.Vector.ToArray();
```

### DI Registration by Provider

**OpenAI / Azure OpenAI** (via `Microsoft.Extensions.AI.OpenAI`):
```csharp
// Program.cs or DI setup
builder.Services.AddEmbeddingGenerator(sp =>
    new OpenAIClient(apiKey)
        .GetEmbeddingClient("text-embedding-3-small")
        .AsIEmbeddingGenerator());

// Azure OpenAI variant
builder.Services.AddEmbeddingGenerator(sp =>
    new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
        .GetEmbeddingClient("text-embedding-3-small")
        .AsIEmbeddingGenerator());
```

**Ollama** (via `OllamaSharp`):
```csharp
builder.Services.AddEmbeddingGenerator(sp =>
    new OllamaApiClient(new Uri("http://localhost:11434/"), "phi3:mini"));
```

**With Middleware Pipeline** (telemetry + caching):
```csharp
builder.Services.AddEmbeddingGenerator(sp =>
    new EmbeddingGeneratorBuilder<string, Embedding<float>>(
        new OllamaApiClient(new Uri("http://localhost:11434/"), "phi3:mini"))
    .UseOpenTelemetry()
    .UseDistributedCache(sp.GetRequiredService<IDistributedCache>())
    .Build());
```

### Usage in Application Service

```csharp
public class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator)
        => _generator = generator;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ReadOnlyMemory<float> vector = await _generator.GenerateVectorAsync(text, cancellationToken: ct);
        return vector.ToArray();
    }
}
```

### Key Observations for SPEC-003

- The interface is **provider-agnostic** — swapping from Ollama to OpenAI requires only changing the DI registration.
- `GenerateVectorAsync` (extension method) returns `ReadOnlyMemory<float>`, which needs `.ToArray()` to convert to `float[]` for pgvector's `Vector` type.
- The interface is thread-safe for concurrent use.
- The abstraction supports middleware chaining for caching and telemetry — useful for P2 where repeated embedding generations may occur.

---

## R4: pgvector for PostgreSQL + Npgsql

### Package Information

| Package | Version | Purpose |
|---------|---------|---------|
| `Pgvector` | **0.3.2** | Core types (`Vector`, `HalfVector`, `SparseVector`) + Npgsql integration |
| `Pgvector.EntityFrameworkCore` | **0.3.0** | EF Core integration (distance functions, model configuration) |

Both packages are MIT-licensed, by `ankane`. The EF Core package supports **EF Core 9 and 10**.

### PostgreSQL Extension Requirement

**Yes**, the `vector` PostgreSQL extension must be installed:
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

In the Aspire AppHost setup, this would need to be ensured either via:
- A migration that runs `CREATE EXTENSION IF NOT EXISTS vector`
- An initialization script for the PostgreSQL container

### EF Core Entity Configuration

**Define the vector column**:
```csharp
using Pgvector;
using System.ComponentModel.DataAnnotations.Schema;

public class SkillEmbedding
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public string SkillDescription { get; set; } = string.Empty;

    [Column(TypeName = "vector(1536)")]  // dimension depends on embedding model
    public Vector? Embedding { get; set; }

    public DateTime GeneratedAt { get; set; }
}
```

**Model configuration**:
```csharp
// In OnModelCreating or IEntityTypeConfiguration
modelBuilder.HasPostgresExtension("vector");  // ensures CREATE EXTENSION

modelBuilder.Entity<SkillEmbedding>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => new { e.AgentId, e.SkillName }).IsUnique();

    // Optional: HNSW index for fast cosine similarity search
    entity.HasIndex(e => e.Embedding)
        .HasMethod("hnsw")
        .HasOperators("vector_cosine_ops");
});
```

**DbContext configuration**:
```csharp
optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());
```

**Or with `NpgsqlDataSourceBuilder`** (required for Aspire integration):
```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();
```

### Cosine Similarity Search with EF Core

```csharp
using Pgvector;
using Pgvector.EntityFrameworkCore;

var queryVector = new Vector(queryEmbedding);  // float[] → Vector

var results = await context.SkillEmbeddings
    .Where(se => se.Embedding!.CosineDistance(queryVector) < (1 - threshold))
    .OrderBy(se => se.Embedding!.CosineDistance(queryVector))
    .Take(10)
    .Select(se => new
    {
        se.AgentId,
        se.SkillName,
        se.SkillDescription,
        Similarity = 1 - se.Embedding!.CosineDistance(queryVector)  // Convert distance to similarity
    })
    .ToListAsync(cancellationToken);
```

**Important**: pgvector uses **cosine distance** (0 = identical, 2 = opposite), not **cosine similarity** (1 = identical, -1 = opposite). The conversion is: `similarity = 1 - cosine_distance`.

Available distance functions in `Pgvector.EntityFrameworkCore`:
| Method | PostgreSQL Operator | Use Case |
|--------|-------------------|----------|
| `L2Distance()` | `<->` | Euclidean distance |
| `MaxInnerProduct()` | `<#>` | Inner product (negative) |
| `CosineDistance()` | `<=>` | Cosine distance |
| `L1Distance()` | `<+>` | Manhattan distance |

### Key Observations for SPEC-003

- The vector dimension (e.g., 1536) must match the embedding model output dimension. This should be configurable.
- For < 100 rows, even without an HNSW/IVFFlat index, exact nearest-neighbor search is fast enough. Indexing becomes valuable at ~1000+ rows.
- The `Pgvector` NuGet `Vector` type constructor accepts `float[]`, which aligns with `IEmbeddingGenerator`'s output.
- pgvector extension installation is a one-time operation, best handled in an EF Core migration.

---

## R5: Search Result DTO Design

### Current DTOs

Existing `AgentSummaryDto` (from SPEC-002):
```csharp
public class AgentSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

### Decision: Create New DTOs, Do NOT Extend AgentSummaryDto

**Rationale**:
1. **Single Responsibility**: `AgentSummaryDto` represents the list view. Search results have fundamentally different data — matched skills, optional similarity scores — that don't belong in a list DTO.
2. **Open/Closed Principle**: Adding optional `MatchedSkills` and `SimilarityScore` to `AgentSummaryDto` would pollute the list API response with null fields.
3. **API Clarity**: Consumers of `GET /api/agents` and `GET /api/agents/search` expect different response shapes. Mixing them creates confusion.

### Proposed DTOs

```csharp
/// <summary>
/// 搜索结果中匹配的单个技能
/// </summary>
public class MatchedSkillDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// 搜索结果中的单个 Agent 匹配项
/// </summary>
public class AgentSearchResultDto
{
    // Agent 摘要信息（与 AgentSummaryDto 字段一致但独立）
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // 搜索特有字段
    public List<MatchedSkillDto> MatchedSkills { get; set; } = [];
    public double? SimilarityScore { get; set; }  // P2: 向量搜索相似度，P1 为 null
}

/// <summary>
/// 搜索端点响应包装
/// </summary>
public class AgentSearchResponse
{
    public List<AgentSearchResultDto> Results { get; set; } = [];
    public string SearchMode { get; set; } = "keyword";  // "keyword" | "semantic"
    public string Query { get; set; } = string.Empty;
    public int TotalCount { get; set; }
}
```

### Why Not Inherit from AgentSummaryDto?

```csharp
// ❌ Avoid this pattern
public class AgentSearchResultDto : AgentSummaryDto
{
    public List<MatchedSkillDto> MatchedSkills { get; set; } = [];
}
```

Reasons to avoid inheritance:
- DTOs are data contracts — inheritance creates tight coupling between API endpoints
- Serialization libraries handle inheritance poorly (polymorphic serialization issues)
- If `AgentSummaryDto` changes (e.g., adds `Description`), the search DTO is unintentionally affected
- Flat, self-contained DTOs are easier to reason about and document in OpenAPI

### Response Shape Example

```json
{
  "results": [
    {
      "id": "a1b2c3...",
      "name": "CustomerSupportAgent",
      "agentType": "A2A",
      "status": "Registered",
      "createdAt": "2026-02-09T10:00:00Z",
      "matchedSkills": [
        { "name": "answer-customer-questions", "description": "Answer questions about products and orders" },
        { "name": "customer-onboarding", "description": "Guide new customers through setup" }
      ],
      "similarityScore": null
    }
  ],
  "searchMode": "keyword",
  "query": "customer",
  "totalCount": 1
}
```

---

## R6: Endpoint Routing — Search vs CRUD Conflict

### Current Route Configuration

From `AgentEndpoints.cs`:
```csharp
var group = app.MapGroup("/api/agents").WithTags("Agents");

group.MapPost("/", RegisterAgent);          // POST /api/agents
group.MapGet("/", GetAgents);               // GET  /api/agents
group.MapGet("/{id:guid}", GetAgentById);   // GET  /api/agents/{id}
group.MapPut("/{id:guid}", UpdateAgent);    // PUT  /api/agents/{id}
group.MapDelete("/{id:guid}", DeleteAgent); // DELETE /api/agents/{id}
```

### Will `GET /search?q=` Conflict with `GET /{id:guid}`?

**No conflict.** ASP.NET Core route matching with constraints is deterministic:

1. `/{id:guid}` uses the `:guid` route constraint, which only matches valid GUID format strings (e.g., `a1b2c3d4-e5f6-7890-abcd-ef1234567890`).
2. The string `"search"` is **not** a valid GUID, so `GET /api/agents/search?q=test` will **never** match `/{id:guid}`.
3. ASP.NET Core evaluates route constraints before matching. A literal route segment (`/search`) takes priority over a parameterized segment anyway.

### Verification

Route matching order in ASP.NET Core Minimal APIs:
1. **Literal segments** match first (e.g., `/search`)
2. **Constrained parameters** match next (e.g., `/{id:guid}`)
3. **Unconstrained parameters** match last (e.g., `/{id}`)

Since we have the `:guid` constraint AND `search` is a literal, there is zero ambiguity.

### Best Practice: Adding the Search Route

**Option A — Add to existing MapGroup** (Recommended):
```csharp
var group = app.MapGroup("/api/agents").WithTags("Agents");

// CRUD routes
group.MapPost("/", RegisterAgent);
group.MapGet("/", GetAgents);
group.MapGet("/{id:guid}", GetAgentById);
group.MapPut("/{id:guid}", UpdateAgent);
group.MapDelete("/{id:guid}", DeleteAgent);

// Search route
group.MapGet("/search", SearchAgents);
```

This is clean and keeps all agent-related routes in one group. The `/search` sub-route is a common REST pattern for search operations (e.g., GitHub API uses `/search/repositories`).

**Option B — Separate MapGroup for search** (If search grows complex):
```csharp
var searchGroup = app.MapGroup("/api/agents/search").WithTags("Agent Search");
searchGroup.MapGet("/", SearchAgents);
```

### Decision: Use Option A

For P1, add `group.MapGet("/search", SearchAgents)` to the existing `MapAgentEndpoints`. It's simple, consistent, and there's no routing conflict.

### Route Parameter Design

```csharp
private static async Task<IResult> SearchAgents(
    ISender sender,
    [FromQuery(Name = "q")] string? query)
{
    if (string.IsNullOrWhiteSpace(query))
        return Results.BadRequest(new { success = false, message = "Query parameter 'q' is required." });

    if (query.Length > 500)
        return Results.BadRequest(new { success = false, message = "Query text must not exceed 500 characters." });

    var result = await sender.Send(new SearchAgentsQuery(query.Trim()));
    return Results.Ok(result);
}
```

Using `[FromQuery(Name = "q")]` explicitly binds the `q` query parameter, making the intent clear and compatible with OpenAPI documentation generation.

---

## Summary of Key Decisions

| # | Topic | Decision |
|---|-------|----------|
| R1 | JSONB search SQL | `EXISTS + jsonb_array_elements() + ILIKE` — no indexing needed at current scale |
| R2 | EF Core approach | Try LINQ first → fallback to `FromSql` with `SELECT *` → last resort two-step ID + entity load |
| R3 | Embedding abstraction | `Microsoft.Extensions.AI.Abstractions` v10.2.0, `IEmbeddingGenerator<string, Embedding<float>>` |
| R4 | pgvector packages | `Pgvector` 0.3.2 + `Pgvector.EntityFrameworkCore` 0.3.0, requires PG `vector` extension |
| R5 | Search DTO | New `AgentSearchResultDto` with `MatchedSkills` list — do NOT extend `AgentSummaryDto` |
| R6 | Route design | `group.MapGet("/search", SearchAgents)` — no conflict with `/{id:guid}` |
