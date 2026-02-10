using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// Agent 注册聚合根。代表一个已注册的 Agent，通过 AgentType 鉴别器区分三种类型。
/// </summary>
public class AgentRegistration : BaseEntity
{
    /// <summary>Agent 名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Agent 描述（可选）</summary>
    public string? Description { get; private set; }

    /// <summary>Agent 类型，注册后不可变更</summary>
    public AgentType AgentType { get; private set; }

    /// <summary>Agent 状态</summary>
    public AgentStatus Status { get; private set; }

    /// <summary>A2A Agent 的远程端点 URL（仅 A2A 类型）</summary>
    public string? Endpoint { get; private set; }

    /// <summary>A2A Agent 的协议描述卡片（仅 A2A 类型，JSONB）</summary>
    public AgentCardVO? AgentCard { get; private set; }

    /// <summary>ChatClient Agent 的 LLM 配置（仅 ChatClient 类型，JSONB）</summary>
    public LlmConfigVO? LlmConfig { get; private set; }

    /// <summary>Workflow Agent 引用的 WorkflowDefinition ID（仅 Workflow 类型）</summary>
    public Guid? WorkflowRef { get; private set; }

    /// <summary>Agent 健康检查状态（JSONB）</summary>
    public HealthCheckVO HealthCheck { get; private set; } = HealthCheckVO.Default();

    // EF Core requires parameterless constructor
    private AgentRegistration() { }

    /// <summary>创建 A2A 类型 Agent</summary>
    public static AgentRegistration CreateA2A(
        string name,
        string? description,
        string endpoint,
        AgentCardVO agentCard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(agentCard, nameof(agentCard));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (endpoint.Length > 2048)
            throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(endpoint));

        return new AgentRegistration
        {
            Name = name,
            Description = description,
            AgentType = AgentType.A2A,
            Status = AgentStatus.Registered,
            Endpoint = endpoint,
            AgentCard = agentCard,
            HealthCheck = HealthCheckVO.Default()
        };
    }

    /// <summary>创建 ChatClient 类型 Agent</summary>
    public static AgentRegistration CreateChatClient(
        string name,
        string? description,
        LlmConfigVO llmConfig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(llmConfig, nameof(llmConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(llmConfig.ModelId, nameof(llmConfig.ModelId));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        return new AgentRegistration
        {
            Name = name,
            Description = description,
            AgentType = AgentType.ChatClient,
            Status = AgentStatus.Registered,
            LlmConfig = llmConfig,
            HealthCheck = HealthCheckVO.Default()
        };
    }

    /// <summary>创建 Workflow 类型 Agent</summary>
    public static AgentRegistration CreateWorkflow(
        string name,
        string? description,
        Guid workflowRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        if (workflowRef == Guid.Empty)
            throw new ArgumentException("WorkflowRef must not be empty.", nameof(workflowRef));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        return new AgentRegistration
        {
            Name = name,
            Description = description,
            AgentType = AgentType.Workflow,
            Status = AgentStatus.Registered,
            WorkflowRef = workflowRef,
            HealthCheck = HealthCheckVO.Default()
        };
    }

    /// <summary>
    /// 更新 Agent 配置。agentType 不可变更，按当前类型校验更新数据的合法性。
    /// </summary>
    public void Update(
        string name,
        string? description,
        string? endpoint,
        AgentCardVO? agentCard,
        LlmConfigVO? llmConfig,
        Guid? workflowRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        // Type-specific invariant validation
        switch (AgentType)
        {
            case AgentType.A2A:
                ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));
                ArgumentNullException.ThrowIfNull(agentCard, nameof(agentCard));
                if (endpoint!.Length > 2048)
                    throw new ArgumentException("Endpoint must not exceed 2048 characters.", nameof(endpoint));
                break;

            case AgentType.ChatClient:
                ArgumentNullException.ThrowIfNull(llmConfig, nameof(llmConfig));
                ArgumentException.ThrowIfNullOrWhiteSpace(llmConfig!.ModelId, nameof(llmConfig.ModelId));
                break;

            case AgentType.Workflow:
                if (workflowRef is null || workflowRef == Guid.Empty)
                    throw new ArgumentException("WorkflowRef must not be empty.", nameof(workflowRef));
                break;
        }

        Name = name;
        Description = description;
        Endpoint = endpoint;
        AgentCard = agentCard;
        LlmConfig = llmConfig;
        WorkflowRef = workflowRef;
    }
}
