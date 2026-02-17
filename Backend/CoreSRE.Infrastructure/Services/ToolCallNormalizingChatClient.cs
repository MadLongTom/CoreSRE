using Microsoft.Extensions.AI;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// IChatClient 装饰器：规范化对话历史中 FunctionCallContent.Arguments，
/// 确保 Arguments 永远不为 null（至少为空字典 {}）。
/// <para>
/// 根因：当无参函数（如 list_services、list_log_labels 等）被 LLM 调用后，
/// FunctionInvokingChatClient 将 tool call 历史发回 LLM 时，
/// OpenAI SDK 会把 null Arguments 序列化为 JSON 字符串 "null"。
/// Bedrock/Anthropic 代理将 "null" 解析为 JSON null 后传给 Bedrock Claude API，
/// 导致 tool_use.input 不是合法 dictionary，返回 HTTP 400 ValidationException。
/// </para>
/// <para>
/// 此装饰器插入在 OpenAI SDK IChatClient 与 FunctionInvokingChatClient 之间，
/// 在每次请求转发前将所有 FunctionCallContent.Arguments = null 替换为空字典。
/// </para>
/// </summary>
public sealed class ToolCallNormalizingChatClient : DelegatingChatClient
{
    public ToolCallNormalizingChatClient(IChatClient innerClient) : base(innerClient) { }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        NormalizeFunctionCallArguments(messages);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        NormalizeFunctionCallArguments(messages);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    /// <summary>
    /// 遍历所有消息，将 FunctionCallContent.Arguments == null 替换为空字典。
    /// 这确保 OpenAI SDK 序列化时生成 "{}" 而非 "null"。
    /// </summary>
    private static void NormalizeFunctionCallArguments(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fcc && fcc.Arguments is null)
                {
                    fcc.Arguments = new Dictionary<string, object?>();
                }
            }
        }
    }
}
