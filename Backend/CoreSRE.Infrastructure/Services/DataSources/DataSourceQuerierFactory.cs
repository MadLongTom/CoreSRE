using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// 数据源查询器工厂。根据 DataSourceProduct 路由到正确的 IDataSourceQuerier 实现。
/// </summary>
public class DataSourceQuerierFactory : IDataSourceQuerierFactory
{
    private readonly IEnumerable<IDataSourceQuerier> _queriers;

    public DataSourceQuerierFactory(IEnumerable<IDataSourceQuerier> queriers)
    {
        _queriers = queriers;
    }

    public IDataSourceQuerier GetQuerier(DataSourceProduct product)
    {
        var querier = _queriers.FirstOrDefault(q => q.CanHandle(product));
        if (querier is null)
            throw new NotSupportedException(
                $"No querier registered for DataSourceProduct '{product}'. " +
                $"Available queriers handle: {string.Join(", ", _queriers.SelectMany(q => Enum.GetValues<DataSourceProduct>().Where(q.CanHandle)))}");

        return querier;
    }
}
