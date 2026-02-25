namespace CoreSRE.Application.Alerts.Services;

/// <summary>
/// SOP 生成 Prompt 构建器。
/// </summary>
public static class SopGenerationPromptBuilder
{
    /// <summary>
    /// 构建 SOP 生成的提示词（传给总结 Agent）。
    /// </summary>
    public static string Build(
        string alertName,
        Dictionary<string, string> alertLabels,
        string? rootCause,
        string conversationHistory)
    {
        var labelsText = string.Join(", ", alertLabels.Select(kv => $"{kv.Key}={kv.Value}"));

        return $"""
            ## 任务：根据团队故障分析对话，提炼 SOP

            以下是一次告警应急响应的完整对话记录。请从中提炼一份标准操作流程（SOP），
            使得未来再次发生相同告警时，单个 Agent 可以按此 SOP 独立执行故障处置。

            ### 告警信息
            - 名称: {alertName}
            - 标签: {labelsText}
            - 根因: {rootCause ?? "未确定"}

            ### 团队对话记录
            {conversationHistory}

            ---

            ### 输出要求

            请严格按以下 Markdown 格式输出 SOP：

            # SOP: {alertName} 处置流程

            ## 适用条件
            - 告警标签匹配: (列出关键标签)

            ## 工具依赖
            - `tool_name_1` — 用途说明
            - `tool_name_2` — 用途说明

            ## 操作步骤

            ### Step 1: 确认告警状态
            **操作**: 调用 `function_name` 查询告警状态
            **预期**: 告警仍在 firing
            **如果异常**: 记录并关闭

            ### Step 2: 诊断问题
            (逐步列出诊断步骤)

            ### Step N: 验证修复
            **操作**: 等待后重新查询指标
            **预期**: 指标回落到正常水平
            **如果异常**: 上报人工介入

            ## 回滚方案
            (如果修复操作导致更大问题，描述回滚步骤)
            """;
    }
}
