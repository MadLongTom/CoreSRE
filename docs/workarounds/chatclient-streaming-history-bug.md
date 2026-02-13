# Workaround: ChatClientAgent Streaming 不发送会话历史

- **状态**: 🟡 待升级后移除
- **影响版本**: `Microsoft.Agents.AI.*` `1.0.0-preview.260209.1`
- **修复版本**: 上游已修复 — [PR #3798](https://github.com/microsoft/agent-framework/pull/3798) (commit `6c37ce84`, 2026-02-10)
- **涉及文件**:
  - `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs`
  - `Backend/CoreSRE.Infrastructure/Persistence/Sessions/PostgresChatHistoryProvider.cs`

---

## Bug 描述

`ChatClientAgent.RunCoreStreamingAsync` 中，`PrepareSessionAndMessagesAsync` 构建了两个消息列表：

| 变量 | 内容 | 用途 |
|---|---|---|
| `inputMessagesForChatClient` | 会话历史 + 用户输入 + AI 上下文 | ❌ 仅用于通知 Provider |
| `inputMessagesForProviders` | 用户输入 + AI 上下文（**无历史**）| ❌ 被传给 LLM |

`RunCoreStreamingAsync` 将 `inputMessagesForProviders` 传给 `chatClient.GetStreamingResponseAsync()`，导致 LLM 永远只看到当前这一条消息，多轮对话完全丧失上下文。

非流式路径 (`RunCoreAsync`) 正确使用了 `inputMessagesForChatClient`，仅流式路径有此 bug。

## 正向修改（Workaround）

### 1. `AgentChatEndpoints.cs` — 手动拼接历史到 inputMessages

在 `HandleChatClientWithHistoryAsync` 中，不再只传 `newUserMessage`，而是从 session 的 `ChatHistoryProvider` 中提取全部历史，拼接后传给 `RunStreamingAsync`：

```csharp
// ── WORKAROUND 代码 ──

// 从 session 中获取 ChatHistoryProvider (它同时实现了 IReadOnlyList<ChatMessage>)
var historyProvider = session?.GetService(typeof(ChatHistoryProvider)) as IReadOnlyList<ChatMessage>;

// 构建完整消息列表 = 历史 + 新消息
var allMessages = new List<ChatMessage>();
if (historyProvider is { Count: > 0 })
{
    allMessages.AddRange(historyProvider);
}
allMessages.Add(newUserMessage);

// 传给 RunStreamingAsync（而非只传 newUserMessage）
await foreach (var update in aiAgent.RunStreamingAsync(allMessages, session, cancellationToken: cancellationToken))
```

**原理**: 由于 `inputMessagesForProviders` 从 `inputMessages` 构建，我们把历史塞入 `inputMessages`，`inputMessagesForProviders` 自然包含完整上下文。

### 2. `PostgresChatHistoryProvider.InvokedCoreAsync` — 引用去重

由于 workaround 把历史消息作为 `inputMessages` 传入，框架在 `InvokedCoreAsync` 回调时 `context.RequestMessages` 会包含已经存储的历史消息。如果直接 `AddRange`，历史会被重复追加。

```csharp
// ── WORKAROUND 代码 ──

protected override ValueTask InvokedCoreAsync(
    InvokedContext context, CancellationToken cancellationToken = default)
{
    if (context.InvokeException is not null)
        return ValueTask.CompletedTask;

    // 使用引用相等去重，避免重复追加已在 _messages 中的消息
    var existingSet = new HashSet<ChatMessage>(ReferenceEqualityComparer.Instance);
    foreach (var msg in _messages)
        existingSet.Add(msg);

    foreach (var msg in context.RequestMessages)
    {
        if (!existingSet.Contains(msg))
            _messages.Add(msg);
    }

    if (context.ResponseMessages is not null)
    {
        foreach (var msg in context.ResponseMessages)
        {
            if (!existingSet.Contains(msg))
                _messages.Add(msg);
        }
    }

    return ValueTask.CompletedTask;
}
```

---

## 逆向修改（升级后还原）

当 `Microsoft.Agents.AI` 升级到包含 PR #3798 修复的版本后，执行以下还原：

### 1. `AgentChatEndpoints.cs` — 恢复为只传新消息

```csharp
// ── 还原代码 ──

// 删除 historyProvider 提取和 allMessages 拼接逻辑
// 删除以下代码:
//   var historyProvider = session?.GetService(typeof(ChatHistoryProvider)) as IReadOnlyList<ChatMessage>;
//   var allMessages = new List<ChatMessage>();
//   if (historyProvider is { Count: > 0 }) { allMessages.AddRange(historyProvider); }
//   allMessages.Add(newUserMessage);

// 恢复为只传新消息:
await foreach (var update in aiAgent.RunStreamingAsync(newUserMessage, session, cancellationToken: cancellationToken))
```

同时删除相关的 `historyCount` / `totalMessages` 日志，恢复为原始日志格式。

### 2. `PostgresChatHistoryProvider.InvokedCoreAsync` — 恢复为简单 AddRange

```csharp
// ── 还原代码 ──

protected override ValueTask InvokedCoreAsync(
    InvokedContext context, CancellationToken cancellationToken = default)
{
    if (context.InvokeException is not null)
        return ValueTask.CompletedTask;

    _messages.AddRange(context.RequestMessages);
    if (context.ResponseMessages is not null)
        _messages.AddRange(context.ResponseMessages);

    return ValueTask.CompletedTask;
}
```

### 3. 删除本文件中标记的 WORKAROUND 注释

在两个文件中搜索 `WORKAROUND` 关键字，删除相关注释块。

---

## 验证方法

升级并还原后，进行多轮对话测试：

1. 发送 "我叫 Alice"
2. 发送 "我叫什么名字？"
3. Agent 应正确回答 "Alice"，证明会话历史正常传递给 LLM
