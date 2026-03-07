using System.Text.Json;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// SOP 步骤定义值对象。从 SOP Markdown 解析后生成。
/// </summary>
public sealed record SopStepDefinition
{
    /// <summary>步骤编号（1-based）</summary>
    public int StepNumber { get; init; }

    /// <summary>步骤描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>步骤类型</summary>
    public SopStepType StepType { get; init; } = SopStepType.Freeform;

    /// <summary>工具名（结构化步骤必填）</summary>
    public string? ToolName { get; init; }

    /// <summary>工具参数模板（可含 ${variable} 占位符）</summary>
    public JsonElement? ParameterTemplate { get; init; }

    /// <summary>预期结果描述（供 Agent 判断）</summary>
    public string? ExpectedOutcome { get; init; }

    /// <summary>超时时间（秒）</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>是否需要人工审批</summary>
    public bool RequiresApproval { get; init; }
}
