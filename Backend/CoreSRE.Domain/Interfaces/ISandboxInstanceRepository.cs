using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// SandboxInstance 专用仓储接口
/// </summary>
public interface ISandboxInstanceRepository : IRepository<SandboxInstance>
{
    Task<IEnumerable<SandboxInstance>> GetByStatusAsync(
        SandboxStatus status, CancellationToken cancellationToken = default);

    Task<IEnumerable<SandboxInstance>> GetByAgentIdAsync(
        Guid agentId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SandboxInstance> Items, int TotalCount)> GetPagedAsync(
        SandboxStatus? status, Guid? agentId, string? search,
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>获取所有需要自动停止检查的 Running 沙箱</summary>
    Task<IEnumerable<SandboxInstance>> GetRunningWithAutoStopAsync(
        CancellationToken cancellationToken = default);
}
