using System.Text;
using CoreSRE.Domain.Entities;

namespace CoreSRE.Application.Skills;

/// <summary>
/// 将 Skill 摘要列表拼接为 SystemPrompt 格式。
/// 采用渐进式披露：SystemPrompt 仅列出名称+描述摘要，
/// LLM 通过 read_skill 工具按需加载完整内容。
/// </summary>
public static class SkillPromptBuilder
{
    /// <summary>生成 Skill 摘要注入到 SystemPrompt 尾部的文本块</summary>
    public static string BuildSkillSummary(IEnumerable<SkillRegistration> skills)
    {
        var skillList = skills.ToList();
        if (skillList.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Available Skills");
        sb.AppendLine("Use the `read_skill` tool to load full instructions for a skill before using it.");
        sb.AppendLine();

        foreach (var skill in skillList)
        {
            sb.AppendLine($"- **{skill.Name}**: {skill.Description}");
        }

        return sb.ToString();
    }
}
