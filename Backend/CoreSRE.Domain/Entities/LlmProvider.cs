namespace CoreSRE.Domain.Entities;

/// <summary>
/// LLM Provider 聚合根。代表一个 OpenAI 兼容的 API 服务提供方。
/// </summary>
public class LlmProvider : BaseEntity
{
    /// <summary>Provider 名称，全局唯一，最长 200 字符</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>OpenAI 兼容 API 的 Base URL</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>API Key（明文存储 v1，响应中掩码）</summary>
    public string ApiKey { get; private set; } = string.Empty;

    /// <summary>已发现的模型 ID 列表（JSONB）</summary>
    public List<string> DiscoveredModels { get; private set; } = [];

    /// <summary>模型列表最后刷新时间</summary>
    public DateTime? ModelsRefreshedAt { get; private set; }

    // EF Core requires parameterless constructor
    private LlmProvider() { }

    /// <summary>创建新的 LLM Provider</summary>
    public static LlmProvider Create(string name, string baseUrl, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl, nameof(baseUrl));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (baseUrl.Length > 500)
            throw new ArgumentException("BaseUrl must not exceed 500 characters.", nameof(baseUrl));

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("BaseUrl must start with http:// or https://.", nameof(baseUrl));

        return new LlmProvider
        {
            Name = name,
            BaseUrl = baseUrl,
            ApiKey = apiKey
        };
    }

    /// <summary>更新 Provider 配置。apiKey 为 null 时保留原值。</summary>
    public void Update(string name, string baseUrl, string? apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl, nameof(baseUrl));

        if (name.Length > 200)
            throw new ArgumentException("Name must not exceed 200 characters.", nameof(name));

        if (baseUrl.Length > 500)
            throw new ArgumentException("BaseUrl must not exceed 500 characters.", nameof(baseUrl));

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("BaseUrl must start with http:// or https://.", nameof(baseUrl));

        Name = name;
        BaseUrl = baseUrl;

        if (apiKey is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
            ApiKey = apiKey;
        }
    }

    /// <summary>替换已发现的模型列表，更新刷新时间</summary>
    public void UpdateDiscoveredModels(List<string> modelIds)
    {
        ArgumentNullException.ThrowIfNull(modelIds, nameof(modelIds));
        DiscoveredModels = modelIds;
        ModelsRefreshedAt = DateTime.UtcNow;
    }

    /// <summary>返回掩码后的 API Key（仅显示最后 4 位）</summary>
    public string MaskApiKey()
    {
        if (string.IsNullOrEmpty(ApiKey) || ApiKey.Length <= 4)
            return "****";

        return $"****{ApiKey[^4..]}";
    }
}
