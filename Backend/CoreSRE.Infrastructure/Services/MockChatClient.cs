using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Mock IChatClient 实现 — 返回模拟响应，供无 LLM Provider 时的工作流开发与测试使用。
/// 响应包含 mock 标记、agent 名称、输入摘要和时间戳字段。
/// </summary>
public class MockChatClient : IChatClient
{
    private readonly string _agentName;

    /// <summary>输入摘要最大长度</summary>
    private const int MaxInputSummaryLength = 200;

    public MockChatClient(string agentName)
    {
        _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var userMessage = chatMessages
            .LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        var inputSummary = userMessage.Length > MaxInputSummaryLength
            ? userMessage[..MaxInputSummaryLength]
            : userMessage;

        var responseJson = JsonSerializer.Serialize(new
        {
            mock = true,
            agentName = _agentName,
            inputSummary,
            timestamp = DateTime.UtcNow.ToString("o")
        });

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseJson));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MockChatClient does not support streaming.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IChatClient))
            return this;
        return null;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
