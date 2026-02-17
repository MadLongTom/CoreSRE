using CoreSRE.Domain.Enums;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 数据源查询器工厂。根据 DataSourceProduct 返回对应的 IDataSourceQuerier 实现。
/// </summary>
public interface IDataSourceQuerierFactory
{
    /// <summary>获取能够处理指定产品的查询器</summary>
    IDataSourceQuerier GetQuerier(DataSourceProduct product);
}
