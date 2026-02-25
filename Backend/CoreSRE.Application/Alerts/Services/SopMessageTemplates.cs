namespace CoreSRE.Application.Alerts.Services;

/// <summary>
/// SOP 执行首条消息模板构建器。
/// 注入告警上下文 + SOP 步骤执行指令。
/// </summary>
public static class SopMessageTemplates
{
    /// <summary>
    /// 构建 SOP 自动执行的首条用户消息。
    /// </summary>
    public static string BuildSopExecutionMessage(
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations,
        string? sopName = null)
    {
        var labelsText = string.Join("\n", alertLabels.Select(kv => $"  - {kv.Key}: {kv.Value}"));
        var annotationsText = alertAnnotations.Count > 0
            ? string.Join("\n", alertAnnotations.Select(kv => $"  - {kv.Key}: {kv.Value}"))
            : "  (无)";

        var sopRef = sopName is not null ? $" \"{sopName}\"" : "";

        return $"""
            ## 🚨 告警自动处置

            **告警名称**: {alertName}
            **告警标签**:
            {labelsText}

            **告警注解**:
            {annotationsText}

            ---

            请按照当前绑定的 SOP{sopRef} 步骤，逐步执行故障处置。

            要求：
            1. 严格按照 SOP 步骤顺序执行
            2. 每个步骤执行后记录结果
            3. 如果某步骤执行失败，记录失败原因并继续下一步
            4. 执行完毕后给出总结：问题根因、执行结果、是否已恢复

            请开始执行。
            """;
    }
}
