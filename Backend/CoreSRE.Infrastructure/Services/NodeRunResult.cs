using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 节点执行结果 — 记录单次节点执行的输出、时间和状态。
/// </summary>
internal sealed record NodeRunResult
{
    /// <summary>节点的结构化输出数据（失败时为 null）</summary>
    public NodeOutputData? OutputData { get; init; }

    /// <summary>执行开始时间</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>执行完成时间（失败或超时时可能为 null）</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>是否执行成功</summary>
    public bool IsSuccess { get; init; }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; init; }
}
