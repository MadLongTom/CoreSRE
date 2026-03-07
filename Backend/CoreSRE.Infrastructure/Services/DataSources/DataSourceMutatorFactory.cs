using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// DataSourceMutator 工厂 — 根据 DataSourceProduct 路由到对应的 Mutator 实现。
/// </summary>
public sealed class DataSourceMutatorFactory(IEnumerable<IDataSourceMutator> mutators) : IDataSourceMutatorFactory
{
    public IDataSourceMutator? GetMutator(DataSourceProduct product) =>
        mutators.FirstOrDefault(m => m.CanHandle(product));
}
