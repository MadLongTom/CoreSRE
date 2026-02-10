using CoreSRE.Domain.Entities;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// Conversation 专用仓储接口，扩展通用仓储以支持按最近活跃排序查询。
/// </summary>
public interface IConversationRepository : IRepository<Conversation>
{
    /// <summary>按 UpdatedAt 降序查询所有对话</summary>
    Task<IEnumerable<Conversation>> GetAllOrderedByUpdatedAtAsync(CancellationToken cancellationToken = default);
}
