using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// Agent Skills 规范 SKILL.md 解析与导出服务接口
/// </summary>
public interface ISkillMdService
{
    /// <summary>解析 SKILL.md 内容 → 结构化数据</summary>
    SkillMdParseResult Parse(string skillMdContent);

    /// <summary>导出 Skill 为 SKILL.md 格式的字符串</summary>
    /// <param name="skill">技能实体</param>
    /// <param name="resolvedAllowedToolNames">AllowedTools(Guid) → 工具名的映射，用于 YAML 导出</param>
    string Export(SkillRegistration skill, IReadOnlyDictionary<Guid, string>? resolvedAllowedToolNames = null);

    /// <summary>导出 Skill 为 ZIP（含 SKILL.md + 文件包）</summary>
    Task<byte[]> ExportZipAsync(SkillRegistration skill,
        IReadOnlyDictionary<Guid, string>? resolvedAllowedToolNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>从 ZIP 导入 — 解析 SKILL.md 并上传文件包到 S3</summary>
    Task<SkillMdImportResult> ImportZipAsync(Stream zipStream, Guid skillId, CancellationToken cancellationToken = default);
}

/// <summary>SKILL.md 解析结果</summary>
public class SkillMdParseResult
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? License { get; set; }
    public string? Compatibility { get; set; }
    public string? AllowedTools { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public List<string> Errors { get; set; } = [];
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>ZIP 导入结果</summary>
public class SkillMdImportResult
{
    public SkillMdParseResult? ParseResult { get; set; }
    public List<string> UploadedFiles { get; set; } = [];
    public bool HasFiles { get; set; }
    public List<string> Errors { get; set; } = [];
    public bool HasErrors => Errors.Count > 0;
}
