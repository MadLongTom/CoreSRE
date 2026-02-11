using CoreSRE.Domain.Entities;

namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// McpToolItem 专用仓储接口，扩展通用仓储以支持按 ToolRegistration 关联查询和批量删除。
/// </summary>
public interface IMcpToolItemRepository : IRepository<McpToolItem>
{
    /// <summary>
    /// 按 ToolRegistration ID 查询所有关联的 MCP 子工具项。
    /// </summary>
    Task<IReadOnlyList<McpToolItem>> GetByToolRegistrationIdAsync(Guid toolRegistrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ToolRegistration ID 批量删除所有关联的 MCP 子工具项。
    /// </summary>
    Task DeleteByToolRegistrationIdAsync(Guid toolRegistrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ID 列表批量查询（用于 ToolRefs 解析），包含关联的 ToolRegistration 导航属性。
    /// </summary>
    Task<IEnumerable<McpToolItem>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
