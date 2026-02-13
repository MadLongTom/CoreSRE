using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// LLM 工具 — 从 S3 读取 Skill 文件包中的文件内容。
/// 当 Skill 标记 HasFiles=true 时才注入此工具。
/// </summary>
internal sealed class ReadSkillFileAIFunction : AIFunction
{
    private readonly IReadOnlyDictionary<string, SkillRegistration> _skillMap;
    private readonly IFileStorageService _fileStorage;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "skill_name": {
                "type": "string",
                "description": "The name of the skill that owns the file package"
            },
            "file_path": {
                "type": "string",
                "description": "The relative path within the skill's file package (e.g., 'scripts/backup.sh')"
            }
        },
        "required": ["skill_name", "file_path"]
    }
    """).RootElement.Clone();

    public ReadSkillFileAIFunction(
        IReadOnlyDictionary<string, SkillRegistration> skillMap,
        IFileStorageService fileStorage)
    {
        _skillMap = skillMap;
        _fileStorage = fileStorage;
    }

    public override string Name => "read_skill_file";

    public override string Description =>
        "Read a file from a skill's file package (scripts, references, or assets). " +
        "Returns the file content as text.";

    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var skillName = arguments.TryGetValue("skill_name", out var snVal) ? snVal?.ToString() ?? string.Empty : string.Empty;
        var filePath = arguments.TryGetValue("file_path", out var fpVal) ? fpVal?.ToString() ?? string.Empty : string.Empty;

        if (!_skillMap.TryGetValue(skillName, out var skill))
            return $"Error: Skill '{skillName}' not found.";

        if (!skill.HasFiles)
            return $"Error: Skill '{skillName}' has no file package.";

        var key = $"{skill.Id}/{filePath}";
        try
        {
            var exists = await _fileStorage.ExistsAsync("coresre-skills", key, cancellationToken);
            if (!exists)
                return $"Error: File '{filePath}' not found in skill '{skillName}'.";

            using var stream = await _fileStorage.DownloadAsync("coresre-skills", key, cancellationToken);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}
