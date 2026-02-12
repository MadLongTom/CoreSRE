using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.AI;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 提供沙盒工具（命令行执行、文件读写、代码运行等）的 AIFunction 实例。
/// 每个 Agent 会话在 Kubernetes Pod 中拥有独立的隔离容器环境。
/// </summary>
public interface ISandboxToolProvider
{
    /// <summary>
    /// 为指定 Agent + 会话创建沙盒工具集合。
    /// 容器的镜像、CPU、内存等参数从 Agent 的 LlmConfig 中读取。
    /// </summary>
    /// <param name="agentId">Agent 注册 ID</param>
    /// <param name="conversationId">会话 ID，用于隔离 VM 实例</param>
    /// <param name="llmConfig">Agent 的 LLM 配置（包含沙盒类型、镜像、资源配置）</param>
    /// <returns>可注入 ChatOptions.Tools 的 AIFunction 列表</returns>
    IReadOnlyList<AIFunction> CreateSandboxTools(Guid agentId, string conversationId, LlmConfigVO llmConfig);
}
