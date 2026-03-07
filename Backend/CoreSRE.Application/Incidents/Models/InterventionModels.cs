using System.Text.Json.Serialization;

namespace CoreSRE.Application.Incidents.Models;

// ═══════════════════════════════════════════════════════════════════
// Intervention Request / Response Models (Structured HITL — Feature B)
// ═══════════════════════════════════════════════════════════════════

/// <summary>请求类型：工具审批、自由文本输入、选择项</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InterventionRequestType
{
    /// <summary>工具调用审批 — approve/reject</summary>
    ToolApproval,

    /// <summary>Agent 请求人工输入自由文本</summary>
    FreeTextInput,

    /// <summary>Agent 请求人工从选项中选择</summary>
    Choice,
}

/// <summary>
/// Agent → 人工 的结构化请求。
/// 每个请求有唯一 RequestId，前端根据 Type 渲染不同 UI。
/// </summary>
public record InterventionRequest(
    string RequestId,
    Guid IncidentId,
    InterventionRequestType Type,
    string Prompt,
    DateTime CreatedAt,
    /// <summary>ToolApproval 类型时的工具信息</summary>
    ToolApprovalData? ToolApproval = null,
    /// <summary>Choice 类型时的选项列表</summary>
    List<string>? Choices = null);

/// <summary>工具审批请求的附加数据</summary>
public record ToolApprovalData(
    string ToolName,
    string CallId,
    Dictionary<string, object?>? Arguments);

/// <summary>响应类型</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InterventionResponseType
{
    Approved,
    Rejected,
    TextInput,
    ChoiceSelected,
}

/// <summary>
/// 人工 → Agent 的结构化响应。
/// 通过 RequestId 与请求配对。
/// </summary>
public record InterventionResponse(
    string RequestId,
    InterventionResponseType Type,
    /// <summary>FreeText / Choice 的文本内容</summary>
    string? Content = null,
    /// <summary>ToolApproval 的审批结果</summary>
    bool? Approved = null,
    string? OperatorName = null,
    DateTime? Timestamp = null);

/// <summary>Info about an actively processing incident.</summary>
public record ActiveIncidentInfo(Guid AgentId, string ConversationId, DateTime StartedAt);

/// <summary>A spontaneous (proactive) human message injected into an agent conversation.</summary>
public record ProactiveHumanMessage(string Content, string? OperatorName, DateTime Timestamp);
