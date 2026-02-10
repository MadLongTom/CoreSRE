using CoreSRE.Domain.Entities;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// LlmProvider 专用仓储接口
/// </summary>
public interface ILlmProviderRepository : IRepository<LlmProvider>
{
    /// <summary>按名称查找 Provider</summary>
    Task<LlmProvider?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>检查名称是否已存在（排除指定 ID）</summary>
    Task<bool> ExistsWithNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
