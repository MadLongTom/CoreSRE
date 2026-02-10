namespace CoreSRE.Application.Common.Interfaces;

/// <summary>
/// 模型发现服务接口。通过 OpenAI 兼容的 GET /models 端点发现可用模型。
/// </summary>
public interface IModelDiscoveryService
{
    /// <summary>
    /// 调用 {baseUrl}/models 发现可用模型列表
    /// </summary>
    /// <param name="baseUrl">Provider 的 Base URL</param>
    /// <param name="apiKey">API Key（Bearer token）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模型 ID 列表</returns>
    Task<List<string>> DiscoverModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
}
