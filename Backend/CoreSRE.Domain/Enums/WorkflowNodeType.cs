namespace CoreSRE.Domain.Enums;

/// <summary>工作流节点类型</summary>
public enum WorkflowNodeType
{
    /// <summary>开始节点，工作流入口，透传输入数据</summary>
    Start,
    /// <summary>Agent 节点，引用 AgentRegistration</summary>
    Agent,
    /// <summary>Tool 节点，引用 ToolRegistration</summary>
    Tool,
    /// <summary>条件分支节点</summary>
    Condition,
    /// <summary>并行分发节点</summary>
    FanOut,
    /// <summary>聚合汇总节点</summary>
    FanIn
}
