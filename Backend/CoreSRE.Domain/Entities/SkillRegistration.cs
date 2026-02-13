using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 技能注册 — Agent 的模块化知识文档
/// </summary>
public class SkillRegistration : BaseEntity
{
    /// <summary>技能名称（唯一标识）</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>技能描述 — LLM 判断何时使用该技能的唯一依据</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>分类标签</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Markdown 指令正文</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>作用域</summary>
    public SkillScope Scope { get; private set; } = SkillScope.User;

    /// <summary>状态</summary>
    public SkillStatus Status { get; private set; } = SkillStatus.Active;

    /// <summary>依赖的工具 ID 列表</summary>
    public List<Guid> RequiresTools { get; private set; } = [];

    /// <summary>是否有 S3 文件包</summary>
    public bool HasFiles { get; private set; }

    private SkillRegistration() { }

    /// <summary>创建技能</summary>
    public static SkillRegistration Create(
        string name,
        string description,
        string category,
        string content,
        SkillScope scope = SkillScope.User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SkillRegistration
        {
            Name = name.Trim(),
            Description = description.Trim(),
            Category = (category ?? string.Empty).Trim(),
            Content = content ?? string.Empty,
            Scope = scope,
            Status = SkillStatus.Active,
        };
    }

    /// <summary>更新技能</summary>
    public void Update(string name, string description, string category, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name.Trim();
        Description = description.Trim();
        Category = (category ?? string.Empty).Trim();
        Content = content ?? string.Empty;
    }

    /// <summary>激活</summary>
    public void Activate() => Status = SkillStatus.Active;

    /// <summary>停用</summary>
    public void Deactivate() => Status = SkillStatus.Inactive;

    /// <summary>标记是否有文件包</summary>
    public void SetHasFiles(bool hasFiles) => HasFiles = hasFiles;

    /// <summary>设置依赖工具</summary>
    public void SetRequiresTools(List<Guid> toolIds) =>
        RequiresTools = toolIds ?? [];
}
