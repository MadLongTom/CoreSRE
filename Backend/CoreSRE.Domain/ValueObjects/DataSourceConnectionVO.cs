namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 数据源连接配置。存储为 PostgreSQL JSONB 列。
/// 合并了端点、认证、TLS 和产品特有配置。
/// </summary>
public sealed record DataSourceConnectionVO
{
    /// <summary>数据源 Base URL，如 http://prometheus:9090</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>认证类型：None / ApiKey / Bearer / BasicAuth</summary>
    public string AuthType { get; init; } = "None";

    /// <summary>加密后的凭据（API Key / Bearer Token / Base64 encoded user:pass）</summary>
    public string? EncryptedCredential { get; init; }

    /// <summary>API Key 自定义 Header 名称（默认 Authorization）</summary>
    public string? AuthHeaderName { get; init; }

    /// <summary>是否跳过 TLS 证书验证（开发环境用）</summary>
    public bool TlsSkipVerify { get; init; }

    /// <summary>请求超时秒数，默认 30</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>自定义请求头</summary>
    public Dictionary<string, string>? CustomHeaders { get; init; }

    /// <summary>K8s 命名空间（仅 Kubernetes 产品使用）</summary>
    public string? Namespace { get; init; }

    /// <summary>GitHub/GitLab Organization 或项目路径</summary>
    public string? Organization { get; init; }

    /// <summary>K8s kubeconfig 内容（Base64 编码，可选，默认使用 in-cluster 或默认 kubeconfig）</summary>
    public string? KubeConfig { get; init; }
}
