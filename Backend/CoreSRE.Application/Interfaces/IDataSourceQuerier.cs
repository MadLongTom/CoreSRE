using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 数据源查询策略接口。每种 DataSourceProduct 对应一个实现。
/// </summary>
public interface IDataSourceQuerier
{
    /// <summary>是否能处理该产品类型</summary>
    bool CanHandle(DataSourceProduct product);

    /// <summary>执行统一查询，返回标准化结果</summary>
    Task<DataSourceResultVO> QueryAsync(
        DataSourceRegistration registration,
        DataSourceQueryVO query,
        CancellationToken ct = default);

    /// <summary>健康检查</summary>
    Task<DataSourceHealthVO> HealthCheckAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default);

    /// <summary>发现元数据（标签/服务/索引等）</summary>
    Task<DataSourceMetadataVO> DiscoverMetadataAsync(
        DataSourceRegistration registration,
        CancellationToken ct = default);
}
