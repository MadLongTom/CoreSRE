using System.Text.RegularExpressions;
using CoreSRE.Domain.Enums;

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
}
