namespace CoreSRE.Domain.Interfaces;

/// <summary>
/// 工作单元接口
/// </summary>
public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
