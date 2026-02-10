namespace CoreSRE.Domain.Enums;

/// <summary>
/// Agent 类型枚举
/// </summary>
public enum AgentType
{
    /// <summary>远程 A2A 协议 Agent，通过 AgentCard 描述能力</summary>
    A2A,

    /// <summary>本地 LLM Agent，通过 LlmConfig 配置模型与工具</summary>
    ChatClient,

    /// <summary>工作流 Agent，引用 WorkflowDefinition</summary>
    Workflow
}
