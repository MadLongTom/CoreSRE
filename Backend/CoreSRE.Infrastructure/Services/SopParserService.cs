using System.Text.RegularExpressions;
using CoreSRE.Application.Alerts.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 解析 LLM 生成的 SOP Markdown → SopParseResult。
/// 提取 SOP 名称、描述、全文、引用的工具函数名。
/// </summary>
public partial class SopParserService(ILogger<SopParserService> logger) : ISopParserService
{
    // 匹配 `function_name` 格式的工具引用（含下划线和横杠）
    [GeneratedRegex(@"`([a-z][a-z0-9_]*(?:-[a-z0-9]+)*)`", RegexOptions.Compiled)]
    private static partial Regex ToolReferenceRegex();

    // 匹配 # SOP: xxx 标题
    [GeneratedRegex(@"^#\s+SOP:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SopTitleRegex();

    // 匹配 ## 适用条件 段
    [GeneratedRegex(@"##\s+适用条件\s*\n([\s\S]*?)(?=\n##\s|\z)", RegexOptions.None)]
    private static partial Regex ApplicabilityRegex();

    public SopParseResult Parse(string sopMarkdown, string alertName)
    {
        if (string.IsNullOrWhiteSpace(sopMarkdown))
        {
            logger.LogWarning("Empty SOP markdown received.");
            return new SopParseResult
            {
                Name = ToKebabCase(alertName),
                Description = $"Auto-generated SOP for {alertName}",
                Content = sopMarkdown
            };
        }

        // 提取标题
        var titleMatch = SopTitleRegex().Match(sopMarkdown);
        var title = titleMatch.Success
            ? titleMatch.Groups[1].Value.Trim()
            : alertName;

        // 提取适用条件段作为描述
        var applicabilityMatch = ApplicabilityRegex().Match(sopMarkdown);
        var description = applicabilityMatch.Success
            ? applicabilityMatch.Groups[1].Value.Trim()
            : $"Auto-generated SOP for {title}";

        if (description.Length > 1024)
            description = description[..1024];

        // 提取工具引用
        var toolRefs = ToolReferenceRegex()
            .Matches(sopMarkdown)
            .Select(m => m.Groups[1].Value)
            .Where(name => name.Contains('_') || name.Contains('-')) // 过滤普通单词
            .Distinct()
            .ToList();

        logger.LogInformation(
            "Parsed SOP '{Title}'. Found {ToolCount} tool references.",
            title, toolRefs.Count);

        return new SopParseResult
        {
            Name = ToKebabCase(title),
            Description = description,
            Content = sopMarkdown,
            ReferencedToolNames = toolRefs
        };
    }

    /// <summary>
    /// 将标题转为 kebab-case（小写+横杠，去除特殊字符）。
    /// </summary>
    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed-sop";

        var result = Regex.Replace(input.Trim(), @"[^a-zA-Z0-9\u4e00-\u9fff]+", "-");
        result = Regex.Replace(result, @"-{2,}", "-");
        result = result.Trim('-').ToLowerInvariant();

        return string.IsNullOrEmpty(result) ? "unnamed-sop" : result;
    }
}
