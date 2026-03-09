using System.Net.Security;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// Kubernetes 数据源变更操作实现 — 通过 Kubernetes API 执行 Pod 重启、Deployment 扩缩容、回滚等操作。
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

        using var client = CreateClient(registration);

        return mutation.Operation switch
        {
            "restart_pod" => await RestartPodAsync(client, mutation, ct),
            "scale_deployment" => await ScaleDeploymentAsync(client, mutation, ct),
            "rollback_deployment" => await RollbackDeploymentAsync(client, mutation, ct),
            _ => new DataSourceMutationResultVO
            {
                Success = false,
                Message = $"Unsupported operation: {mutation.Operation}"
            }
        };
    }

    private async Task<DataSourceMutationResultVO> RestartPodAsync(
        k8s.Kubernetes client, DataSourceMutationVO mutation, CancellationToken ct)
    {
        try
        {
            await client.CoreV1.DeleteNamespacedPodAsync(
                mutation.ResourceName, mutation.Namespace!, cancellationToken: ct);

            logger.LogInformation("Pod '{Pod}' deleted in namespace '{Namespace}'",
                mutation.ResourceName, mutation.Namespace);

            return new DataSourceMutationResultVO
            {
                Success = true,
                Message = $"Pod '{mutation.ResourceName}' in namespace '{mutation.Namespace}' restart initiated.",
                Detail = $"Pod '{mutation.ResourceName}' deleted; controller will recreate it."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete pod {Pod} in {Namespace}", mutation.ResourceName, mutation.Namespace);
            return new DataSourceMutationResultVO
            {
                Success = false,
                Message = $"Failed to restart pod: {ex.Message}"
            };
        }
    }

    private async Task<DataSourceMutationResultVO> ScaleDeploymentAsync(
        k8s.Kubernetes client, DataSourceMutationVO mutation, CancellationToken ct)
    {
        var replicas = int.Parse(mutation.Parameters.GetValueOrDefault("replicas", "1"));

        try
        {
            var patch = new V1Patch(
                $"{{\"spec\":{{\"replicas\":{replicas}}}}}",
                V1Patch.PatchType.MergePatch);

            await client.AppsV1.PatchNamespacedDeploymentScaleAsync(
                patch, mutation.ResourceName, mutation.Namespace!, cancellationToken: ct);

            logger.LogInformation("Deployment '{Deployment}' scaled to {Replicas} in namespace '{Namespace}'",
                mutation.ResourceName, replicas, mutation.Namespace);

            return new DataSourceMutationResultVO
            {
                Success = true,
                Message = $"Deployment '{mutation.ResourceName}' scaled to {replicas} replicas in namespace '{mutation.Namespace}'.",
                Detail = $"Replicas set to {replicas}."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scale deployment {Deployment} in {Namespace}",
                mutation.ResourceName, mutation.Namespace);
            return new DataSourceMutationResultVO
            {
                Success = false,
                Message = $"Failed to scale deployment: {ex.Message}"
            };
        }
    }

    private async Task<DataSourceMutationResultVO> RollbackDeploymentAsync(
        k8s.Kubernetes client, DataSourceMutationVO mutation, CancellationToken ct)
    {
        var revision = mutation.Parameters.GetValueOrDefault("revision", "0");

        try
        {
            // Read current deployment to get the revision history
            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(
                mutation.ResourceName, mutation.Namespace!, cancellationToken: ct);

            // Get ReplicaSets owned by this deployment
            var labelSelector = string.Join(",",
                deployment.Spec.Selector.MatchLabels.Select(kv => $"{kv.Key}={kv.Value}"));
            var rsList = await client.AppsV1.ListNamespacedReplicaSetAsync(
                mutation.Namespace!, labelSelector: labelSelector, cancellationToken: ct);

            // Filter to ReplicaSets owned by this deployment
            var ownedRs = rsList.Items
                .Where(rs => rs.Metadata.OwnerReferences?.Any(
                    o => o.Kind == "Deployment" && o.Name == mutation.ResourceName) == true)
                .OrderByDescending(rs =>
                {
                    var ann = rs.Metadata.Annotations;
                    if (ann != null && ann.TryGetValue("deployment.kubernetes.io/revision", out var rev)
                        && long.TryParse(rev, out var r))
                        return r;
                    return 0L;
                })
                .ToList();

            if (ownedRs.Count < 2)
            {
                return new DataSourceMutationResultVO
                {
                    Success = false,
                    Message = "No previous revision available for rollback."
                };
            }

            // Pick the target ReplicaSet
            V1ReplicaSet targetRs;
            if (revision != "0" && long.TryParse(revision, out var targetRev))
            {
                targetRs = ownedRs.FirstOrDefault(rs =>
                    rs.Metadata.Annotations?.TryGetValue("deployment.kubernetes.io/revision", out var rev) == true
                    && rev == revision)!;
                if (targetRs is null)
                {
                    return new DataSourceMutationResultVO
                    {
                        Success = false,
                        Message = $"Revision {revision} not found."
                    };
                }
            }
            else
            {
                // revision=0 means previous revision (second in the ordered list)
                targetRs = ownedRs[1];
            }

            // Apply the target RS's pod template to the deployment
            deployment.Spec.Template = targetRs.Spec.Template;

            await client.AppsV1.ReplaceNamespacedDeploymentAsync(
                deployment, mutation.ResourceName, mutation.Namespace!, cancellationToken: ct);

            var targetRevStr = targetRs.Metadata.Annotations is not null
                && targetRs.Metadata.Annotations.TryGetValue("deployment.kubernetes.io/revision", out var revStr)
                ? revStr : "previous";

            logger.LogInformation("Deployment '{Deployment}' rolled back to revision {Revision} in namespace '{Namespace}'",
                mutation.ResourceName, targetRevStr, mutation.Namespace);

            return new DataSourceMutationResultVO
            {
                Success = true,
                Message = $"Deployment '{mutation.ResourceName}' rollback to revision {targetRevStr} initiated in namespace '{mutation.Namespace}'.",
                Detail = $"Pod template restored from ReplicaSet '{targetRs.Metadata.Name}'."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rollback deployment {Deployment} in {Namespace}",
                mutation.ResourceName, mutation.Namespace);
            return new DataSourceMutationResultVO
            {
                Success = false,
                Message = $"Failed to rollback deployment: {ex.Message}"
            };
        }
    }

    private static k8s.Kubernetes CreateClient(DataSourceRegistration registration)
    {
        KubernetesClientConfiguration config;

        if (!string.IsNullOrEmpty(registration.ConnectionConfig.KubeConfig))
        {
            var kubeConfigBytes = Convert.FromBase64String(registration.ConnectionConfig.KubeConfig);
            using var stream = new MemoryStream(kubeConfigBytes);
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
        }
        else if (!string.IsNullOrEmpty(registration.ConnectionConfig.BaseUrl)
                 && registration.ConnectionConfig.BaseUrl != "https://kubernetes.default.svc")
        {
            if (!string.IsNullOrEmpty(registration.ConnectionConfig.EncryptedCredential))
            {
                config = new KubernetesClientConfiguration
                {
                    Host = registration.ConnectionConfig.BaseUrl,
                    AccessToken = registration.ConnectionConfig.EncryptedCredential,
                };
            }
            else
            {
                config = KubernetesClientConfiguration.BuildDefaultConfig();
                config.Host = registration.ConnectionConfig.BaseUrl;
            }
        }
        else
        {
            config = KubernetesClientConfiguration.BuildDefaultConfig();
        }

        config.SkipTlsVerify = registration.ConnectionConfig.TlsSkipVerify;

        if (registration.ConnectionConfig.TlsSkipVerify)
        {
            config.FirstMessageHandlerSetup = handler =>
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            };
        }

        return new k8s.Kubernetes(config);
    }
}
