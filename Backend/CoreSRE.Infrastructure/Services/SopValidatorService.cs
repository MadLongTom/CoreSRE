using System.Text.RegularExpressions;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// SOP 结构化校验器 — 检查 SOP Markdown 的段落完整性、工具引用合法性、危险操作标记
/// </summary>
public partial class SopValidatorService(ILogger<SopValidatorService> logger) : ISopValidator
{
    // ── 危险操作前缀（匹配工具名中的动作词）──
    private static readonly string[] DangerousPrefixes =
        ["delete", "scale", "restart", "drop", "remove", "kill", "drain", "cordon", "evict", "rollback"];

    // ── 正则：段落匹配 ──
    [GeneratedRegex(@"^##\s+适用条件", RegexOptions.Multiline)]
    private static partial Regex ApplicabilitySection();

    [GeneratedRegex(@"^##\s+处置步骤|^##\s+Steps|^##\s+步骤", RegexOptions.Multiline)]
    private static partial Regex StepsSection();

    [GeneratedRegex(@"^##\s+回退计划|^##\s+Rollback|^##\s+回退", RegexOptions.Multiline)]
    private static partial Regex RollbackSection();

    [GeneratedRegex(@"^###\s+Step\s+\d+|^###\s+步骤\s*\d+", RegexOptions.Multiline)]
    private static partial Regex StepHeading();

    [GeneratedRegex(@"`([a-z][a-z0-9_]*(?:-[a-z0-9]+)*)`", RegexOptions.Compiled)]
    private static partial Regex ToolReference();

    [GeneratedRegex(@"预期结果|Expected|expected_outcome|预期", RegexOptions.Multiline)]
    private static partial Regex ExpectedOutcome();

    [GeneratedRegex(@"超时|Timeout|timeout", RegexOptions.Multiline)]
    private static partial Regex TimeoutDecl();

    [GeneratedRegex(@"^##\s+初始化上下文", RegexOptions.Multiline)]
    private static partial Regex ContextInitSection();

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
        { "Metrics", "Logs", "Tracing", "Alerting", "Deployment", "Git" };

    public SopValidationResultVO Validate(string sopContent, IReadOnlySet<string> registeredToolNames)
    {
        if (string.IsNullOrWhiteSpace(sopContent))
            return SopValidationResultVO.WithErrors(["SOP content is empty."]);

        var errors = new List<string>();
        var warnings = new List<string>();
        var dangerousSteps = new List<int>();

        // 1. 必需段落检查
        if (!ApplicabilitySection().IsMatch(sopContent))
            errors.Add("缺少必需段落: '## 适用条件'");

        if (!StepsSection().IsMatch(sopContent))
            errors.Add("缺少必需段落: '## 处置步骤'");

        if (!RollbackSection().IsMatch(sopContent))
            warnings.Add("缺少推荐段落: '## 回退计划'");

        // 2. 步骤数量检查
        var stepMatches = StepHeading().Matches(sopContent);
        if (stepMatches.Count == 0 && StepsSection().IsMatch(sopContent))
            warnings.Add("'## 处置步骤' 段落下未发现标准格式的步骤（### Step N）");

        // 3. 工具引用检查
        var toolRefs = ToolReference().Matches(sopContent)
            .Select(m => m.Groups[1].Value)
            .Where(name => name.Contains('_') || name.Contains('-'))
            .Distinct()
            .ToList();

        foreach (var toolName in toolRefs)
        {
            if (!registeredToolNames.Contains(toolName))
                warnings.Add($"引用的工具 '{toolName}' 未在系统中注册");
        }

        // 4. 危险操作检测
        for (var i = 0; i < stepMatches.Count; i++)
        {
            var stepStart = stepMatches[i].Index;
            var stepEnd = i + 1 < stepMatches.Count ? stepMatches[i + 1].Index : sopContent.Length;
            var stepText = sopContent[stepStart..stepEnd];

            var stepToolRefs = ToolReference().Matches(stepText)
                .Select(m => m.Groups[1].Value)
                .Distinct();

            foreach (var toolName in stepToolRefs)
            {
                if (DangerousPrefixes.Any(p => toolName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    dangerousSteps.Add(i + 1);
                    break;
                }
            }
        }

        // 5. 步骤质量检查（Warning 级）
        for (var i = 0; i < stepMatches.Count; i++)
        {
            var stepStart = stepMatches[i].Index;
            var stepEnd = i + 1 < stepMatches.Count ? stepMatches[i + 1].Index : sopContent.Length;
            var stepText = sopContent[stepStart..stepEnd];

            if (!ExpectedOutcome().IsMatch(stepText))
                warnings.Add($"Step {i + 1} 缺少 '预期结果' 声明");

            if (!TimeoutDecl().IsMatch(stepText))
                warnings.Add($"Step {i + 1} 缺少 '超时' 声明");
        }

        // 6. 上下文初始化段落检查
        if (!ContextInitSection().IsMatch(sopContent))
            warnings.Add("缺少推荐段落: '## 初始化上下文'（添加后可实现 Agent 启动时自动预查数据源）");

        // 7. 上下文初始化条目校验
        ValidateContextInitItems(sopContent, warnings);

        logger.LogInformation(
            "SOP validation completed: Errors={ErrorCount}, Warnings={WarningCount}, DangerousSteps={DangerousCount}",
            errors.Count, warnings.Count, dangerousSteps.Count);

        return errors.Count > 0
            ? SopValidationResultVO.WithErrors(errors, warnings, dangerousSteps)
            : SopValidationResultVO.Valid(warnings, dangerousSteps);
    }

    /// <summary>
    /// 校验上下文初始化条目中的 category 和 expression 占位符。
    /// </summary>
    private static void ValidateContextInitItems(string sopContent, List<string> warnings)
    {
        // 使用 SopParserService 的格式: - {category}: {expression} | {label}
        var itemRegex = new Regex(@"^-\s+(\w+):\s+(.+?)\s*\|\s*.+$", RegexOptions.Multiline);
        var matches = itemRegex.Matches(sopContent);

        foreach (Match m in matches)
        {
            var category = m.Groups[1].Value;
            var expression = m.Groups[2].Value;

            if (!ValidCategories.Contains(category))
                warnings.Add($"初始化上下文条目使用无效 category: '{category}'。有效值: {string.Join(", ", ValidCategories)}");

            if (!expression.Contains("${"))
                warnings.Add($"初始化上下文条目 '{category}' 的表达式未包含模板变量 (${{key}})，建议使用 ${{namespace}} 等占位符提高通用性");
        }
    }
}
