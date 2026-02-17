namespace CoreSRE.Domain.Enums;

/// <summary>
/// Team Agent 编排模式枚举。
/// 定义 6 种多 Agent 协作模式，前 4 种由 agent-framework 内置支持，
/// 后 2 种（Selector、MagneticOne）为自定义 GroupChatManager 扩展。
/// </summary>
public enum TeamMode
{
    /// <summary>顺序管道：A→B→C，每个 Agent 依次处理</summary>
    Sequential,

    /// <summary>并发聚合：A∥B∥C → merge，所有 Agent 同时处理后合并结果</summary>
    Concurrent,

    /// <summary>轮询 GroupChat：按固定顺序循环发言</summary>
    RoundRobin,

    /// <summary>交接/Swarm：Agent 自主路由到下一个 Agent</summary>
    Handoffs,

    /// <summary>LLM 动态选择：由 LLM 决定下一个发言者（需 SPEC-102 执行引擎）</summary>
    Selector,

    /// <summary>双循环账本编排：MagneticOne 模式（需 SPEC-103 执行引擎）</summary>
    MagneticOne,
}
