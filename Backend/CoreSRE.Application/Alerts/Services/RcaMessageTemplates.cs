namespace CoreSRE.Application.Alerts.Services;

/// <summary>
/// 根因分析（RCA）首条消息模板构建器。
/// 注入告警上下文 + RCA Team 指令。
/// </summary>
public static class RcaMessageTemplates
{
    /// <summary>
    /// 构建 RCA Team 对话的首条用户消息。
    /// </summary>
    public static string BuildRootCauseAnalysisMessage(
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations)
    {
        var labelsText = string.Join("\n", alertLabels.Select(kv => $"  - {kv.Key}: {kv.Value}"));
        var annotationsText = alertAnnotations.Count > 0
            ? string.Join("\n", alertAnnotations.Select(kv => $"  - {kv.Key}: {kv.Value}"))
            : "  (无)";

        return $"""
            ## 🔍 根因分析任务

            **告警名称**: {alertName}
            **告警标签**:
            {labelsText}

            **告警注解**:
            {annotationsText}

            ---

            请团队协作分析此告警的根因。

            要求：
            1. 查询相关指标（Prometheus/Loki/Jaeger）确认告警详情
            2. 检查 Kubernetes 相关资源状态（Pod/Node/Service）
            3. 分析时间线：问题何时开始？有无关联的变更/部署？
            4. 关联分析：是否有其他告警同时触发？
            5. 诊断根因并提出修复建议
            6. 输出格式：

            ```
            根因: <一句话描述根因>
            影响范围: <受影响的服务/组件>
            修复建议:
              1. <具体操作步骤>
              2. <具体操作步骤>
            预防建议:
              1. <长期改进建议>
            ```

            请开始分析。
            """;
    }
}
