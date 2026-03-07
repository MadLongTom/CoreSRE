using System.Text.Json;
using System.Text.RegularExpressions;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// SOP Markdown 结构化解析器 — 将步骤段落解析为 SopStepDefinition 列表
/// </summary>
public partial class SopStructuredParserService(ILogger<SopStructuredParserService> logger) : ISopStructuredParser
{
    private static readonly string[] DangerousPrefixes =
        ["delete", "scale", "restart", "drop", "remove", "kill", "drain", "cordon", "evict", "rollback"];

    [GeneratedRegex(@"^###\s+Step\s+(\d+)|^###\s+步骤\s*(\d+)", RegexOptions.Multiline)]
    private static partial Regex StepHeading();

    [GeneratedRegex(@"`([a-z][a-z0-9_]*(?:-[a-z0-9]+)*)`")]
    private static partial Regex ToolReference();

    [GeneratedRegex(@"预期结果[：:]\s*(.+?)(?=\n|$)", RegexOptions.Multiline)]
    private static partial Regex ExpectedOutcomeExtractor();

    [GeneratedRegex(@"超时[：:]\s*(\d+)\s*(秒|s|seconds?)", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutExtractor();

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex VariablePlaceholder();

    public List<SopStepDefinition> Parse(string sopContent, IReadOnlySet<string> dangerousToolPrefixes)
    {
        var steps = new List<SopStepDefinition>();
        var stepMatches = StepHeading().Matches(sopContent);

        for (var i = 0; i < stepMatches.Count; i++)
        {
            var match = stepMatches[i];
            var stepNumber = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            var stepStart = match.Index + match.Length;
            var stepEnd = i + 1 < stepMatches.Count ? stepMatches[i + 1].Index : sopContent.Length;
            var stepText = sopContent[stepStart..stepEnd].Trim();

            // 提取描述（第一行非空文本）
            var descLines = stepText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var description = descLines.Length > 0 ? descLines[0].Trim().TrimStart('-', ' ') : string.Empty;

            // 提取工具名
            var toolRefs = ToolReference().Matches(stepText)
                .Select(m => m.Groups[1].Value)
                .Where(name => name.Contains('_') || name.Contains('-'))
                .Distinct()
                .ToList();
            var toolName = toolRefs.FirstOrDefault();

            // 判断步骤类型
            var stepType = toolName is not null ? SopStepType.Structured : SopStepType.Freeform;

            // 提取预期结果
            var expectedMatch = ExpectedOutcomeExtractor().Match(stepText);
            var expectedOutcome = expectedMatch.Success ? expectedMatch.Groups[1].Value.Trim() : null;

            // 提取超时
            var timeoutMatch = TimeoutExtractor().Match(stepText);
            var timeoutSeconds = timeoutMatch.Success ? int.Parse(timeoutMatch.Groups[1].Value) : 300;

            // 判断是否需要审批（危险工具）
            var requiresApproval = toolName is not null &&
                DangerousPrefixes.Any(p => toolName.Contains(p, StringComparison.OrdinalIgnoreCase));

            steps.Add(new SopStepDefinition
            {
                StepNumber = stepNumber,
                Description = description,
                StepType = stepType,
                ToolName = toolName,
                ExpectedOutcome = expectedOutcome,
                TimeoutSeconds = timeoutSeconds,
                RequiresApproval = requiresApproval,
            });
        }

        logger.LogInformation("Parsed SOP: {StepCount} steps ({Structured} structured, {Freeform} freeform)",
            steps.Count,
            steps.Count(s => s.StepType == SopStepType.Structured),
            steps.Count(s => s.StepType == SopStepType.Freeform));

        return steps;
    }
}
