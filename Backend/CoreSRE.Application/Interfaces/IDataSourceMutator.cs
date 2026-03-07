using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 数据源变更操作接口。每种 DataSourceProduct 可有对应实现。
/// 所有变更操作必须通过 ToolApproval 流程审批后才能执行。
/// </summary>
public interface IDataSourceMutator
{
    /// <summary>是否能处理该产品类型</summary>
    bool CanHandle(DataSourceProduct product);

    /// <summary>执行变更操作</summary>
    Task<DataSourceMutationResultVO> ExecuteAsync(
        DataSourceRegistration registration,
        DataSourceMutationVO mutation,
        CancellationToken ct = default);

    /// <summary>返回该类型数据源支持的变更操作名称列表</summary>
    IReadOnlyList<string> SupportedOperations { get; }
}

/// <summary>
/// 数据源变更操作工厂 — 根据 DataSourceProduct 路由到对应的 Mutator 实现。
/// </summary>
public interface IDataSourceMutatorFactory
{
    /// <summary>获取对应产品类型的 Mutator，不存在则返回 null</summary>
    IDataSourceMutator? GetMutator(DataSourceProduct product);
}
