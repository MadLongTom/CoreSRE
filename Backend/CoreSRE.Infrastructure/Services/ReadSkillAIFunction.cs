using System.Text.Json;
using CoreSRE.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// LLM 工具 — 按名称读取 Skill 的完整 Markdown Content。
/// 渐进式披露：LLM 先在 SystemPrompt 中看到 Skill 名称+描述摘要，
/// 决定需要时调用此工具加载完整指令。
/// </summary>
internal sealed class ReadSkillAIFunction : AIFunction
{
    private readonly IReadOnlyDictionary<string, SkillRegistration> _skillMap;

    private static readonly JsonElement _schema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "skill_name": {
                "type": "string",
                "description": "The name of the skill to load (as shown in Available Skills)"
            }
        },
        "required": ["skill_name"]
    }
    """).RootElement.Clone();

    public ReadSkillAIFunction(IReadOnlyDictionary<string, SkillRegistration> skillMap)
    {
        _skillMap = skillMap;
    }

    public override string Name => "read_skill";

    public override string Description =>
        "Load the full instructions (Markdown content) of a skill by name. " +
        "Use this before applying skill-specific procedures.";

    public override JsonElement JsonSchema => _schema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var skillName = arguments.TryGetValue("skill_name", out var val) ? val?.ToString() ?? string.Empty : string.Empty;

        if (_skillMap.TryGetValue(skillName, out var skill))
        {
            return ValueTask.FromResult<object?>($"# Skill: {skill.Name}\n\n{skill.Content}");
        }

        return ValueTask.FromResult<object?>($"Error: Skill '{skillName}' not found. Available skills: {string.Join(", ", _skillMap.Keys)}");
    }
}
