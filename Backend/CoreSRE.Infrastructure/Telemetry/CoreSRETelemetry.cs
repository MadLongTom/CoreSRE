using System.Diagnostics;

namespace CoreSRE.Infrastructure.Telemetry;

/// <summary>
/// CoreSRE 全局 OpenTelemetry 遥测定义。
/// 提供统一的 ActivitySource 和语义化 Span 创建方法，
/// 覆盖 Agent 调用、工作流执行、工具调用全链路。
/// </summary>
public static class CoreSRETelemetry
{
    /// <summary>遥测源名称（对应 .AddSource() 注册）</summary>
    public const string SourceName = "CoreSRE";

    /// <summary>全局 ActivitySource（线程安全、单例）</summary>
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    // ======================== Workflow Spans ========================

    /// <summary>
    /// 创建工作流执行根 Span。
    /// </summary>
    public static Activity? StartWorkflowExecution(Guid executionId, Guid workflowDefinitionId)
    {
        var activity = Source.StartActivity("workflow.execute", ActivityKind.Internal);
        activity?.SetTag("workflow.execution_id", executionId.ToString());
        activity?.SetTag("workflow.definition_id", workflowDefinitionId.ToString());
        return activity;
    }

    /// <summary>
    /// 创建节点执行 Span（作为工作流执行的子 Span）。
    /// </summary>
    public static Activity? StartNodeExecution(string nodeId, string nodeType, string? displayName = null)
    {
        var activity = Source.StartActivity($"workflow.node.{nodeType}", ActivityKind.Internal);
        activity?.SetTag("workflow.node.id", nodeId);
        activity?.SetTag("workflow.node.type", nodeType);
        if (displayName is not null)
            activity?.SetTag("workflow.node.display_name", displayName);
        return activity;
    }

    // ======================== Agent Spans ========================

    /// <summary>
    /// 创建 Agent 聊天 Span（AG-UI 端点入口）。
    /// </summary>
    public static Activity? StartAgentChat(Guid agentId, string threadId, string? agentName = null)
    {
        var activity = Source.StartActivity("agent.chat", ActivityKind.Server);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("agent.thread_id", threadId);
        if (agentName is not null)
            activity?.SetTag("agent.name", agentName);
        return activity;
    }

    /// <summary>
    /// 创建 Agent 工作流节点调用 Span。
    /// </summary>
    public static Activity? StartAgentInvoke(Guid agentId, string? agentName = null)
    {
        var activity = Source.StartActivity("agent.invoke", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        if (agentName is not null)
            activity?.SetTag("agent.name", agentName);
        return activity;
    }

    /// <summary>
    /// 创建 LLM 调用 Span（符合 GenAI 语义约定）。
    /// </summary>
    public static Activity? StartLlmCall(string? model = null, string? provider = null)
    {
        var activity = Source.StartActivity("gen_ai.chat.completions", ActivityKind.Client);
        if (model is not null)
            activity?.SetTag("gen_ai.request.model", model);
        if (provider is not null)
            activity?.SetTag("gen_ai.system", provider);
        activity?.SetTag("gen_ai.operation.name", "chat");
        return activity;
    }

    // ======================== Tool Spans ========================

    /// <summary>
    /// 创建工具调用 Span。
    /// </summary>
    public static Activity? StartToolInvoke(string toolName, string toolType, Guid? toolId = null)
    {
        var activity = Source.StartActivity("tool.invoke", ActivityKind.Internal);
        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("tool.type", toolType);
        if (toolId.HasValue)
            activity?.SetTag("tool.id", toolId.Value.ToString());
        return activity;
    }

    // ======================== Error Recording ========================

    /// <summary>
    /// 在 Activity 上记录错误事件。
    /// </summary>
    public static void RecordException(Activity? activity, Exception ex)
    {
        if (activity is null) return;
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = ex.GetType().FullName,
            ["exception.message"] = ex.Message,
            ["exception.stacktrace"] = ex.StackTrace
        }));
    }
}
