using System.Text.RegularExpressions;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 技能注册 — Agent 的模块化知识文档，兼容 Agent Skills 规范
/// https://agentskills.io/specification
/// </summary>
public partial class SkillRegistration : BaseEntity
{
    /// <summary>
    /// 技能名称（唯一标识）。
    /// 规范: 1-64 字符, 仅小写字母/数字/连字符, 不以连字符开头结尾, 不含连续连字符。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>技能描述 — LLM 判断何时使用该技能的唯一依据 (≤1024 字符)</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>分类标签 (CoreSRE 扩展字段，不属于 Agent Skills 规范)</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Markdown 指令正文 (建议 &lt; 500 行)</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>许可证 (可选，规范字段)</summary>
    public string? License { get; private set; }

    /// <summary>环境兼容性描述 (可选，≤500 字符，规范字段)</summary>
    public string? Compatibility { get; private set; }

    /// <summary>扩展元数据键值对 (可选，规范字段)</summary>
    public Dictionary<string, string>? Metadata { get; private set; }

    /// <summary>预授权工具 ID 列表 (引用系统注册的 BindableTool，规范实验性字段)</summary>
    public List<Guid> AllowedTools { get; private set; } = [];

    /// <summary>作用域</summary>
    public SkillScope Scope { get; private set; } = SkillScope.User;

    /// <summary>状态</summary>
    public SkillStatus Status { get; private set; } = SkillStatus.Active;

    /// <summary>依赖的工具 ID 列表 (CoreSRE 扩展)</summary>
    public List<Guid> RequiresTools { get; private set; } = [];

    /// <summary>是否有 S3 文件包</summary>
    public bool HasFiles { get; private set; }

    // ── SOP 质量保证字段（Spec 022）──

    /// <summary>版本号（同一 AlertRule 下的 SOP 从 1 递增）</summary>
    public int Version { get; private set; } = 1;

    /// <summary>生成该 SOP 的 Incident ID</summary>
    public Guid? SourceIncidentId { get; private set; }

    /// <summary>来源 AlertRule ID（用于版本关联）</summary>
    public Guid? SourceAlertRuleId { get; private set; }

    /// <summary>审核人</summary>
    public string? ReviewedBy { get; private set; }

    /// <summary>审核意见</summary>
    public string? ReviewComment { get; private set; }

    /// <summary>审核时间</summary>
    public DateTime? ReviewedAt { get; private set; }

    /// <summary>结构化校验结果</summary>
    public SopValidationResultVO? ValidationResult { get; private set; }

    // ── SOP 执行统计（Spec 025）──

    /// <summary>SOP 滚动执行统计</summary>
    public SopExecutionStatsVO? ExecutionStats { get; private set; }

    /// <summary>Agent Skills 规范 name 字段正则</summary>
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex NamePattern();

    private SkillRegistration() { }

    /// <summary>校验 name 是否符合 Agent Skills 规范</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= 64
        && !name.Contains("--")
        && NamePattern().IsMatch(name);

    /// <summary>创建技能</summary>
    public static SkillRegistration Create(
        string name,
        string description,
        string category,
        string content,
        SkillScope scope = SkillScope.User,
        string? license = null,
        string? compatibility = null,
        Dictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SkillRegistration
        {
            Name = name.Trim(),
            Description = description.Trim(),
            Category = (category ?? string.Empty).Trim(),
            Content = content ?? string.Empty,
            License = license?.Trim(),
            Compatibility = compatibility?.Trim(),
            Metadata = metadata,
            Scope = scope,
            Status = SkillStatus.Active,
        };
    }

    /// <summary>更新技能</summary>
    public void Update(
        string name, string description, string category, string content,
        string? license = null, string? compatibility = null,
        Dictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name.Trim();
        Description = description.Trim();
        Category = (category ?? string.Empty).Trim();
        Content = content ?? string.Empty;
        License = license?.Trim();
        Compatibility = compatibility?.Trim();
        Metadata = metadata;
    }

    /// <summary>激活</summary>
    public void Activate() => Status = SkillStatus.Active;

    /// <summary>停用</summary>
    public void Deactivate() => Status = SkillStatus.Inactive;

    /// <summary>标记是否有文件包</summary>
    public void SetHasFiles(bool hasFiles) => HasFiles = hasFiles;

    /// <summary>设置预授权工具</summary>
    public void SetAllowedTools(List<Guid> toolIds) =>
        AllowedTools = toolIds ?? [];

    /// <summary>设置依赖工具</summary>
    public void SetRequiresTools(List<Guid> toolIds) =>
        RequiresTools = toolIds ?? [];

    // ── SOP 生命周期方法（Spec 022）──

    /// <summary>创建自动生成的 SOP（状态为 Draft）</summary>
    public static SkillRegistration CreateSop(
        string name,
        string description,
        string content,
        Guid sourceIncidentId,
        Guid sourceAlertRuleId,
        int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SkillRegistration
        {
            Name = name.Trim(),
            Description = description.Trim(),
            Category = "sop",
            Content = content ?? string.Empty,
            Scope = SkillScope.User,
            Status = SkillStatus.Draft,
            Version = version,
            SourceIncidentId = sourceIncidentId,
            SourceAlertRuleId = sourceAlertRuleId,
        };
    }

    /// <summary>设置校验结果</summary>
    public void SetValidationResult(SopValidationResultVO result)
    {
        ValidationResult = result ?? throw new ArgumentNullException(nameof(result));
        if (!result.IsValid)
            Status = SkillStatus.Invalid;
    }

    /// <summary>审核通过</summary>
    public void Approve(string reviewedBy, string? comment = null)
    {
        if (Status is not SkillStatus.Draft and not SkillStatus.Invalid)
            throw new InvalidOperationException($"Cannot approve a Skill in '{Status}' status. Must be Draft or Invalid.");

        if (ValidationResult is { IsValid: false })
            throw new InvalidOperationException("Cannot approve a Skill with validation errors.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);
        Status = SkillStatus.Reviewed;
        ReviewedBy = reviewedBy;
        ReviewComment = comment;
        ReviewedAt = DateTime.UtcNow;
    }

    /// <summary>驳回</summary>
    public void Reject(string reviewedBy, string reason)
    {
        if (Status is not SkillStatus.Draft and not SkillStatus.Invalid)
            throw new InvalidOperationException($"Cannot reject a Skill in '{Status}' status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = SkillStatus.Rejected;
        ReviewedBy = reviewedBy;
        ReviewComment = reason;
        ReviewedAt = DateTime.UtcNow;
    }

    /// <summary>发布（从 Reviewed 变为 Active）</summary>
    public void Publish()
    {
        if (Status != SkillStatus.Reviewed)
            throw new InvalidOperationException($"Cannot publish a Skill in '{Status}' status. Must be Reviewed.");

        Status = SkillStatus.Active;
    }

    /// <summary>归档</summary>
    public void Archive()
    {
        if (Status == SkillStatus.Archived)
            return;
        Status = SkillStatus.Archived;
    }

    /// <summary>标记为已被新版本取代</summary>
    public void MarkSuperseded() => Status = SkillStatus.Superseded;

    /// <summary>设置来源 Incident</summary>
    public void SetSourceIncident(Guid incidentId) => SourceIncidentId = incidentId;

    /// <summary>设置来源 AlertRule</summary>
    public void SetSourceAlertRule(Guid alertRuleId) => SourceAlertRuleId = alertRuleId;

    /// <summary>设置版本号</summary>
    public void SetVersion(int version) => Version = version > 0 ? version : 1;

    // ── SOP 执行统计方法（Spec 025）──

    /// <summary>记录一次 SOP 执行结果</summary>
    public void RecordExecution(bool success, bool timeout, long mttrMs)
    {
        ExecutionStats = (ExecutionStats ?? SopExecutionStatsVO.Empty())
            .RecordExecution(success, timeout, mttrMs);
    }

    /// <summary>标记为效能降级</summary>
    public void MarkDegraded() => Status = SkillStatus.Degraded;

    // ── 上下文初始化条目（Spec 027）──

    private const string ContextInitMetadataKey = "contextInitItems";

    /// <summary>从 Metadata 中读取上下文初始化条目（SOP 用）</summary>
    public List<ContextInitItemVO>? GetContextInitItems()
    {
        if (Metadata is null || !Metadata.TryGetValue(ContextInitMetadataKey, out var json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<ContextInitItemVO>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>将上下文初始化条目存入 Metadata（由 SopParserService 提取后调用）</summary>
    public void SetContextInitItems(List<ContextInitItemVO> items)
    {
        Metadata ??= new();
        if (items is { Count: > 0 })
            Metadata[ContextInitMetadataKey] = System.Text.Json.JsonSerializer.Serialize(items);
        else
            Metadata.Remove(ContextInitMetadataKey);
    }
}
