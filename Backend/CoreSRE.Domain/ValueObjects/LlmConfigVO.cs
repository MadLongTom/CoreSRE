namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// ChatClient Agent 的 LLM 配置。存储为 PostgreSQL JSONB 列。
/// 字段与 Microsoft.Extensions.AI.ChatOptions 的可配置项一一对应。
/// </summary>
public sealed record LlmConfigVO
{
    /// <summary>关联的 LLM Provider ID（nullable，向后兼容）</summary>
    public Guid? ProviderId { get; init; }

    /// <summary>LLM 模型标识符</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>系统指令</summary>
    public string? Instructions { get; init; }

    /// <summary>工具引用列表（M2 模块 ID，可为空）</summary>
    public List<Guid> ToolRefs { get; init; } = [];

    /// <summary>数据源引用列表 — 控制到函数级别的细粒度绑定</summary>
    public List<DataSourceRefVO> DataSourceRefs { get; init; } = [];

    // ── ChatOptions 扩展配置 ──────────────────────────────────────────────

    /// <summary>生成温度（0‒2），越低越确定</summary>
    public float? Temperature { get; init; }

    /// <summary>最大输出 token 数</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>核采样概率（0‒1）</summary>
    public float? TopP { get; init; }

    /// <summary>Top-K 采样，限制候选 token 数</summary>
    public int? TopK { get; init; }

    /// <summary>重复频率惩罚</summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>存在性惩罚</summary>
    public float? PresencePenalty { get; init; }

    /// <summary>随机种子，用于可复现生成</summary>
    public long? Seed { get; init; }

    /// <summary>停止序列列表</summary>
    public List<string>? StopSequences { get; init; }

    /// <summary>响应格式：Text / Json</summary>
    public string? ResponseFormat { get; init; }

    /// <summary>当 ResponseFormat 为 Json 时的 JSON Schema（可选）</summary>
    public string? ResponseFormatSchema { get; init; }

    /// <summary>工具模式：Auto / Required / None</summary>
    public string? ToolMode { get; init; }

    /// <summary>是否允许单次响应包含多个工具调用</summary>
    public bool? AllowMultipleToolCalls { get; init; }

    // ── History & Memory 配置 ────────────────────────────────────────────

    /// <summary>启用服务端聊天历史管理（框架管理模式）。null 视为 true。</summary>
    public bool? EnableChatHistory { get; init; }

    /// <summary>上下文窗口保留的最大消息数。null 使用平台默认值（50）。</summary>
    public int? MaxHistoryMessages { get; init; }

    /// <summary>启用跨会话语义记忆检索。null 视为 false。</summary>
    public bool? EnableSemanticMemory { get; init; }

    /// <summary>Embedding Provider ID — 用于语义记忆的向量化模型所在 Provider。null 时沿用 ProviderId。</summary>
    public Guid? EmbeddingProviderId { get; init; }

    /// <summary>Embedding 模型标识符 — Provider 中的 embedding model ID。</summary>
    public string? EmbeddingModelId { get; init; }

    /// <summary>Embedding 向量维度。null 使用默认值（1536）。</summary>
    public int? EmbeddingDimensions { get; init; }

    /// <summary>语义记忆搜索模式："BeforeAIInvoke" 或 "OnDemandFunctionCalling"。</summary>
    public string? MemorySearchMode { get; init; }

    /// <summary>每次查询返回的最大记忆片段数。null 使用 SDK 默认值（3）。</summary>
    public int? MemoryMaxResults { get; init; }

    /// <summary>语义记忆最低相关性分数阈值（0~1，越高越严格）。低于此分数的记忆不注入。null 或 0 表示不过滤。</summary>
    public double? MemoryMinRelevanceScore { get; init; }

    // ── Sandbox 配置（Kubernetes Pod 容器隔离）────────────────────────

    /// <summary>启用沙盒工具（命令行、文件读写、代码执行）。null 视为 false。</summary>
    public bool? EnableSandbox { get; init; }

    /// <summary>
    /// 沙盒类型：SimpleBox / CodeBox / InteractiveBox / BrowserBox / ComputerBox。
    /// null 时默认为 CodeBox。
    /// </summary>
    public string? SandboxType { get; init; }

    /// <summary>
    /// OCI 镜像名称。为空时根据 SandboxType 自动选择：
    /// SimpleBox→alpine:latest, CodeBox/InteractiveBox→python:3.12-slim,
    /// BrowserBox→mcr.microsoft.com/playwright:v1.52.0-jammy, ComputerBox→python:3.12-slim。
    /// </summary>
    public string? SandboxImage { get; init; }

    /// <summary>容器分配的 CPU 核数。0 或 null = 默认 1 核。</summary>
    public int? SandboxCpus { get; init; }

    /// <summary>容器分配的内存（MiB）。0 或 null = 默认 512MiB。</summary>
    public int? SandboxMemoryMib { get; init; }

    /// <summary>
    /// K8s 命名空间。为空时使用默认值 "coresre-sandbox"。
    /// Docker Desktop 内置 K8s 默认使用 default 命名空间。
    /// </summary>
    public string? SandboxK8sNamespace { get; init; }

    /// <summary>
    /// 沙箱模式：None / Ephemeral / Persistent。
    /// Ephemeral = 每对话临时 Pod（向后兼容 EnableSandbox=true）；
    /// Persistent = 持久化沙箱实例（由 SandboxInstanceId 指定）。
    /// null 时根据 EnableSandbox 推断：true → Ephemeral, false/null → None。
    /// </summary>
    public string? SandboxMode { get; init; }

    /// <summary>
    /// 持久化沙箱实例 ID — 仅当 SandboxMode=Persistent 时有效。
    /// 引用 SandboxInstance 表的 ID。
    /// </summary>
    public Guid? SandboxInstanceId { get; init; }

    // ── Skills 配置 ──────────────────────────────────────────────────

    /// <summary>
    /// 技能引用列表 — 绑定到该 Agent 的 SkillRegistration ID 列表。
    /// Agent 对话时按此列表注入 Skill 摘要到 SystemPrompt。
    /// </summary>
    public List<Guid> SkillRefs { get; init; } = [];
}
