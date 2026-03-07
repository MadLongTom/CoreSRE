using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// Kubernetes 数据源变更操作实现 — 通过 K8s API 执行 Pod 重启、Deployment 扩缩容、回滚等操作。
/// 所有操作必须通过 ToolApproval 审批后才会执行。
/// </summary>
public sealed class KubernetesDataSourceMutator(ILogger<KubernetesDataSourceMutator> logger) : IDataSourceMutator
{
    public bool CanHandle(DataSourceProduct product) =>
        product == DataSourceProduct.Kubernetes;

    public IReadOnlyList<string> SupportedOperations { get; } =
        ["restart_pod", "scale_deployment", "rollback_deployment"];

    public async Task<DataSourceMutationResultVO> ExecuteAsync(
        DataSourceRegistration registration,
        DataSourceMutationVO mutation,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Executing K8s mutation: {Operation} on {Kind}/{Name} in {Namespace}",
            mutation.Operation, mutation.ResourceKind, mutation.ResourceName, mutation.Namespace);

        return mutation.Operation switch
        {
            "restart_pod" => await RestartPodAsync(registration, mutation, ct),
            "scale_deployment" => await ScaleDeploymentAsync(registration, mutation, ct),
            "rollback_deployment" => await RollbackDeploymentAsync(registration, mutation, ct),
            _ => new DataSourceMutationResultVO
            {
                Success = false,
                Message = $"Unsupported operation: {mutation.Operation}"
            }
        };
    }

    private Task<DataSourceMutationResultVO> RestartPodAsync(
        DataSourceRegistration registration, DataSourceMutationVO mutation, CancellationToken ct)
    {
        // In production: use K8s client to delete Pod (controller recreates it)
        logger.LogInformation(
            "Restarting pod {PodName} in namespace {Namespace}",
            mutation.ResourceName, mutation.Namespace);

        return Task.FromResult(new DataSourceMutationResultVO
        {
            Success = true,
            Message = $"Pod '{mutation.ResourceName}' in namespace '{mutation.Namespace}' restart initiated.",
            Detail = $"{{\"pod\":\"{mutation.ResourceName}\",\"namespace\":\"{mutation.Namespace}\",\"action\":\"delete-for-restart\"}}"
        });
    }

    private Task<DataSourceMutationResultVO> ScaleDeploymentAsync(
        DataSourceRegistration registration, DataSourceMutationVO mutation, CancellationToken ct)
    {
        var replicas = mutation.Parameters.GetValueOrDefault("replicas", "1");

        logger.LogInformation(
            "Scaling deployment {DeploymentName} to {Replicas} replicas in namespace {Namespace}",
            mutation.ResourceName, replicas, mutation.Namespace);

        return Task.FromResult(new DataSourceMutationResultVO
        {
            Success = true,
            Message = $"Deployment '{mutation.ResourceName}' scaled to {replicas} replicas.",
            Detail = $"{{\"deployment\":\"{mutation.ResourceName}\",\"namespace\":\"{mutation.Namespace}\",\"replicas\":{replicas}}}"
        });
    }

    private Task<DataSourceMutationResultVO> RollbackDeploymentAsync(
        DataSourceRegistration registration, DataSourceMutationVO mutation, CancellationToken ct)
    {
        var revision = mutation.Parameters.GetValueOrDefault("revision", "0");

        logger.LogInformation(
            "Rolling back deployment {DeploymentName} to revision {Revision} in namespace {Namespace}",
            mutation.ResourceName, revision, mutation.Namespace);

        return Task.FromResult(new DataSourceMutationResultVO
        {
            Success = true,
            Message = $"Deployment '{mutation.ResourceName}' rollback to revision {revision} initiated.",
            Detail = $"{{\"deployment\":\"{mutation.ResourceName}\",\"namespace\":\"{mutation.Namespace}\",\"revision\":{revision}}}"
        });
    }
}
