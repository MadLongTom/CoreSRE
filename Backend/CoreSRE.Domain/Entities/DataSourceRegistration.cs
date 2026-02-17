using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Domain.Entities;

/// <summary>
/// 数据源注册聚合根。代表一个已注册的 SRE 数据源（Prometheus / Loki / Jaeger / K8s 等）。
/// 通过 Category 表达语义（指标/日志/追踪/告警/部署/Git），通过 Product 区分实现。
/// </summary>
public class DataSourceRegistration : BaseEntity
{
    /// <summary>数据源名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>数据源描述（可选）</summary>
    public string? Description { get; private set; }

    /// <summary>数据源语义类别</summary>
    public DataSourceCategory Category { get; private set; }

    /// <summary>数据源具体产品</summary>
    public DataSourceProduct Product { get; private set; }

    /// <summary>连接状态</summary>
    public DataSourceStatus Status { get; private set; }

    /// <summary>连接配置 (JSONB)</summary>
    public DataSourceConnectionVO ConnectionConfig { get; private set; } = new();

    /// <summary>默认查询配置 (JSONB)</summary>
    public QueryConfigVO DefaultQueryConfig { get; private set; } = new();

    /// <summary>健康检查状态 (JSONB)</summary>
    public DataSourceHealthVO HealthCheck { get; private set; } = DataSourceHealthVO.Default();

    /// <summary>元数据缓存 (JSONB)</summary>
    public DataSourceMetadataVO Metadata { get; private set; } = new();

    // EF Core requires parameterless constructor
    private DataSourceRegistration() { }

    // ── Category ↔ Product 合法映射 ──────────────────────────────

    private static readonly Dictionary<DataSourceCategory, HashSet<DataSourceProduct>> ValidProductMap = new()
    {
        [DataSourceCategory.Metrics] = [DataSourceProduct.Prometheus, DataSourceProduct.VictoriaMetrics, DataSourceProduct.Mimir],
        [DataSourceCategory.Logs] = [DataSourceProduct.Loki, DataSourceProduct.Elasticsearch],
        [DataSourceCategory.Tracing] = [DataSourceProduct.Jaeger, DataSourceProduct.Tempo],
        [DataSourceCategory.Alerting] = [DataSourceProduct.Alertmanager, DataSourceProduct.PagerDuty],
        [DataSourceCategory.Deployment] = [DataSourceProduct.Kubernetes, DataSourceProduct.ArgoCD],
        [DataSourceCategory.Git] = [DataSourceProduct.GitHub, DataSourceProduct.GitLab],
    };

    /// <summary>验证 Category 与 Product 的组合是否合法</summary>
    public static bool IsValidCategoryProduct(DataSourceCategory category, DataSourceProduct product)
        => ValidProductMap.TryGetValue(category, out var products) && products.Contains(product);

    /// <summary>获取某 Category 下所有合法 Product</summary>
    public static IReadOnlyCollection<DataSourceProduct> GetProductsForCategory(DataSourceCategory category)
        => ValidProductMap.TryGetValue(category, out var products) ? products : [];

    // ── 工厂方法 ──────────────────────────────────────────────────

    /// <summary>创建 Metrics 类数据源</summary>
    public static DataSourceRegistration CreateMetrics(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Metrics, product, connectionConfig);

    /// <summary>创建 Logs 类数据源</summary>
    public static DataSourceRegistration CreateLogs(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Logs, product, connectionConfig);

    /// <summary>创建 Tracing 类数据源</summary>
    public static DataSourceRegistration CreateTracing(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Tracing, product, connectionConfig);

    /// <summary>创建 Alerting 类数据源</summary>
    public static DataSourceRegistration CreateAlerting(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Alerting, product, connectionConfig);

    /// <summary>创建 Deployment 类数据源</summary>
    public static DataSourceRegistration CreateDeployment(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Deployment, product, connectionConfig);

    /// <summary>创建 Git 类数据源</summary>
    public static DataSourceRegistration CreateGit(
        string name, string? description, DataSourceProduct product, DataSourceConnectionVO connectionConfig)
        => Create(name, description, DataSourceCategory.Git, product, connectionConfig);

    private static DataSourceRegistration Create(
        string name,
        string? description,
        DataSourceCategory category,
        DataSourceProduct product,
        DataSourceConnectionVO connectionConfig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(connectionConfig, nameof(connectionConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionConfig.BaseUrl, nameof(connectionConfig.BaseUrl));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (connectionConfig.BaseUrl.Length > 2048)
            throw new ArgumentException("BaseUrl must not exceed 2048 characters.", nameof(connectionConfig));

        if (!IsValidCategoryProduct(category, product))
            throw new ArgumentException(
                $"Product '{product}' is not valid for category '{category}'. Valid products: {string.Join(", ", GetProductsForCategory(category))}.",
                nameof(product));

        return new DataSourceRegistration
        {
            Name = name,
            Description = description,
            Category = category,
            Product = product,
            Status = DataSourceStatus.Registered,
            ConnectionConfig = connectionConfig,
            HealthCheck = DataSourceHealthVO.Default()
        };
    }

    // ── 领域行为 ──────────────────────────────────────────────────

    /// <summary>更新数据源基本信息和连接配置</summary>
    public void Update(
        string name,
        string? description,
        DataSourceConnectionVO connectionConfig,
        QueryConfigVO? defaultQueryConfig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(connectionConfig, nameof(connectionConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionConfig.BaseUrl, nameof(connectionConfig.BaseUrl));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        Name = name;
        Description = description;
        ConnectionConfig = connectionConfig;
        DefaultQueryConfig = defaultQueryConfig ?? new();
    }

    /// <summary>更新健康检查结果</summary>
    public void UpdateHealthCheck(DataSourceHealthVO healthCheck)
    {
        ArgumentNullException.ThrowIfNull(healthCheck, nameof(healthCheck));
        HealthCheck = healthCheck;

        // 状态自动转换
        if (healthCheck.IsHealthy)
        {
            Status = DataSourceStatus.Connected;
        }
        else if (Status == DataSourceStatus.Connected)
        {
            Status = DataSourceStatus.Disconnected;
        }
    }

    /// <summary>标记为错误状态（连续失败超过阈值）</summary>
    public void MarkError(string errorMessage)
    {
        Status = DataSourceStatus.Error;
        HealthCheck = HealthCheck with { IsHealthy = false, ErrorMessage = errorMessage, LastCheckAt = DateTime.UtcNow };
    }

    /// <summary>更新元数据缓存</summary>
    public void UpdateMetadata(DataSourceMetadataVO metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata, nameof(metadata));
        Metadata = metadata;
    }

    /// <summary>根据 Category 和 Name 生成该数据源可暴露的标准 AIFunction 名称列表</summary>
    public List<string> GenerateAvailableFunctionNames()
    {
        var safeName = Name.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return Category switch
        {
            DataSourceCategory.Metrics =>
            [
                $"query_metrics_{safeName}",
                $"list_metric_names_{safeName}",
                $"list_metric_labels_{safeName}"
            ],
            DataSourceCategory.Logs =>
            [
                $"query_logs_{safeName}",
                $"list_log_labels_{safeName}"
            ],
            DataSourceCategory.Tracing =>
            [
                $"get_trace_{safeName}",
                $"search_traces_{safeName}",
                $"list_services_{safeName}"
            ],
            DataSourceCategory.Alerting =>
            [
                $"list_alerts_{safeName}",
                $"get_alert_history_{safeName}"
            ],
            DataSourceCategory.Deployment =>
            [
                $"list_resources_{safeName}",
                $"get_resource_{safeName}"
            ],
            DataSourceCategory.Git =>
            [
                $"list_commits_{safeName}",
                $"list_pipelines_{safeName}"
            ],
            _ => []
        };
    }
}
