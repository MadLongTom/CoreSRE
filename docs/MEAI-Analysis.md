# Microsoft.Extensions.AI (M.E.AI) — 综合源码分析报告

> 基于 `dotnet/extensions` 仓库源码分析  
> 分析日期: 2026-02-09

---

## 1. 包概览 (Package Overview)

M.E.AI 由三个核心 NuGet 包组成：

| 包名 | 定位 | 依赖关系 |
|------|------|---------|
| **Microsoft.Extensions.AI.Abstractions** | 定义所有核心接口、数据类型和基类 | 无 AI 相关依赖 |
| **Microsoft.Extensions.AI** | 提供中间件实现（日志、缓存、遥测、函数调用等） | 依赖 Abstractions |
| **Microsoft.Extensions.AI.OpenAI** | OpenAI / Azure OpenAI 的具体 `IChatClient` 实现 | 依赖 Abstractions + OpenAI SDK |

**核心设计理念**：提供统一的 AI 服务抽象层，类似于 `ILogger` 之于日志系统。应用程序面向 `IChatClient` / `IEmbeddingGenerator<>` 编程，底层可替换为任意 AI 提供商（OpenAI、Azure、Ollama 等），且可通过 **装饰器/中间件管线** 叠加日志、缓存、遥测、自动函数调用等横切关注点。

---

## 2. 核心接口 (Key Interfaces)

### 2.1 `IChatClient` — 聊天客户端接口

```csharp
public interface IChatClient : IDisposable
{
    // 非流式：发送消息，返回完整响应
    Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    // 流式：发送消息，返回流式更新
    IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    // 服务定位：获取底层服务对象（元数据、底层实现等）
    object? GetService(Type serviceType, object? serviceKey = null);
}
```

**设计要点**：
- 线程安全，支持并发请求
- `GetService` 提供类似 `IServiceProvider` 的能力，可获取 `ChatClientMetadata`、底层 SDK 客户端实例等
- 所有调用约定：调用者需防范 prompt 注入、数据大小等安全风险

### 2.2 `IEmbeddingGenerator<TInput, TEmbedding>` — 嵌入生成器接口

```csharp
// 非泛型基接口
public interface IEmbeddingGenerator : IDisposable
{
    object? GetService(Type serviceType, object? serviceKey = null);
}

// 泛型接口
public interface IEmbeddingGenerator<in TInput, TEmbedding> : IEmbeddingGenerator
    where TEmbedding : Embedding
{
    Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(
        IEnumerable<TInput> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

常用具体化类型: `IEmbeddingGenerator<string, Embedding<float>>`

### 2.3 `IImageGenerator` — 图像生成器接口 (Experimental)

```csharp
[Experimental]
public interface IImageGenerator : IDisposable
{
    Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default);

    object? GetService(Type serviceType, object? serviceKey = null);
}
```

### 2.4 `ISpeechToTextClient` — 语音转文本客户端接口 (Experimental)

```csharp
[Experimental]
public interface ISpeechToTextClient : IDisposable
{
    Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default);

    object? GetService(Type serviceType, object? serviceKey = null);
}
```

### 2.5 `IChatReducer` — 聊天消息缩减器 (Experimental)

```csharp
[Experimental]
public interface IChatReducer
{
    Task<IEnumerable<ChatMessage>> ReduceAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken);
}
```

用于在消息列表过长时缩减/汇总消息，管理上下文窗口。

### 2.6 `IToolReductionStrategy` — 工具缩减策略 (Experimental)

```csharp
[Experimental]
public interface IToolReductionStrategy
{
    Task<IEnumerable<AITool>> SelectToolsForRequestAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken = default);
}
```

在发送请求前筛选/缩减工具列表，以适应提供商限制或提升工具选择质量。

---

## 3. 核心数据类型 (Key Data Types)

### 3.1 消息与内容模型

| 类型 | 说明 |
|------|------|
| **`ChatMessage`** | 聊天消息，包含 `Role`、`Contents` (IList\<AIContent\>)、`AuthorName`、`MessageId` 等 |
| **`ChatRole`** | 结构体，预定义 `System`、`User`、`Assistant`、`Tool` |
| **`AIContent`** | 内容基类，JSON 多态。所有消息内容项的抽象基类 |
| **`TextContent`** | 文本内容 |
| **`DataContent`** | 二进制数据（图片等），支持 data URI |
| **`UriContent`** | URI 引用 |
| **`FunctionCallContent`** | 函数调用请求（含 `CallId`、`Name`、`Arguments`） |
| **`FunctionResultContent`** | 函数调用结果（含 `CallId`、`Result`、`Exception`） |
| **`TextReasoningContent`** | 推理/思考过程文本 |
| **`ErrorContent`** | 错误内容 |
| **`UsageContent`** | Token 使用量内容 |
| **`HostedFileContent`** | 服务端托管的文件引用 |
| **`HostedVectorStoreContent`** | 服务端托管的向量存储引用 |
| **`FunctionApprovalRequestContent`** | 函数审批请求 (Experimental) |
| **`FunctionApprovalResponseContent`** | 函数审批响应 (Experimental) |
| **`McpServerToolCallContent`** | MCP 服务器工具调用 (Experimental) |
| **`McpServerToolResultContent`** | MCP 服务器工具结果 (Experimental) |
| **`CodeInterpreterToolCallContent`** | 代码解释器工具调用 (Experimental) |
| **`CodeInterpreterToolResultContent`** | 代码解释器工具结果 (Experimental) |
| **`UserInputRequestContent`** | 用户输入请求 |
| **`UserInputResponseContent`** | 用户输入响应 |
| **`ImageGenerationToolCallContent`** | 图像生成工具调用 |
| **`ImageGenerationToolResultContent`** | 图像生成工具结果 |

### 3.2 响应模型

| 类型 | 说明 |
|------|------|
| **`ChatResponse`** | 完整的聊天响应，包含 `Messages`、`ResponseId`、`ModelId`、`FinishReason`、`Usage`、`ConversationId` |
| **`ChatResponseUpdate`** | 流式响应块，用于 streaming 场景 |
| **`ChatFinishReason`** | 完成原因：`Stop`、`Length`、`ToolCalls`、`ContentFilter` |
| **`UsageDetails`** | Token 用量：`InputTokenCount`、`OutputTokenCount`、`TotalTokenCount`、`CachedInputTokenCount`、`ReasoningTokenCount` |

### 3.3 选项/配置模型

| 类型 | 说明 |
|------|------|
| **`ChatOptions`** | 请求选项：`Temperature`、`MaxOutputTokens`、`TopP`、`TopK`、`FrequencyPenalty`、`PresencePenalty`、`Seed`、`ModelId`、`StopSequences`、`Tools`、`ToolMode`、`ResponseFormat`、`Instructions`、`Reasoning`、`ConversationId`、`AllowMultipleToolCalls`、`AllowBackgroundResponses` |
| **`ChatResponseFormat`** | 响应格式：`Text` / `Json` / `ForJsonSchema<T>()` |
| **`ChatToolMode`** | 工具模式：`Auto` / `None` / `RequireAny` / `RequireSpecific(name)` |
| **`ReasoningOptions`** | 推理选项（控制思考深度等） |
| **`EmbeddingGenerationOptions`** | 嵌入选项：`Dimensions`、`ModelId` |

### 3.4 元数据类型

| 类型 | 说明 |
|------|------|
| **`ChatClientMetadata`** | `ProviderName`、`ProviderUri`、`DefaultModelId` |
| **`EmbeddingGeneratorMetadata`** | 嵌入生成器元数据 |
| **`ImageGeneratorMetadata`** | 图像生成器元数据 |
| **`SpeechToTextClientMetadata`** | 语音转文本元数据 |

### 3.5 嵌入类型

| 类型 | 说明 |
|------|------|
| **`Embedding`** | 嵌入基类，含 `CreatedAt`、`Dimensions`、`ModelId` |
| **`Embedding<T>`** | 泛型嵌入向量 (`ReadOnlyMemory<T>`)，常用 `Embedding<float>` |
| **`BinaryEmbedding`** | 二进制嵌入 |
| **`GeneratedEmbeddings<TEmbedding>`** | 嵌入生成结果集合，实现 `IList<TEmbedding>` |

---

## 4. 中间件/管线 (Middleware Pipeline)

### 4.1 `DelegatingChatClient` — 装饰器基类

```csharp
public class DelegatingChatClient : IChatClient
{
    protected IChatClient InnerClient { get; }

    // 默认实现全部委托给 InnerClient
    public virtual Task<ChatResponse> GetResponseAsync(...) =>
        InnerClient.GetResponseAsync(...);

    public virtual IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(...) =>
        InnerClient.GetStreamingResponseAsync(...);
}
```

所有中间件均继承此类，在调用 `InnerClient` 前后添加自定义逻辑。

### 4.2 `ChatClientBuilder` — 管线构建器

```csharp
var pipeline = new ChatClientBuilder(innerClient)
    .Use(inner => new LoggingChatClient(inner, logger))
    .Use(inner => new OpenTelemetryChatClient(inner))
    .Use(inner => new FunctionInvokingChatClient(inner))
    .Build();
```

**构建规则**：工厂按反序应用，第一个 `.Use()` 成为最外层。

支持三种 `Use` 重载：
1. `Use(Func<IChatClient, IChatClient>)` — 简单工厂
2. `Use(Func<IChatClient, IServiceProvider, IChatClient>)` — 可使用 DI
3. `Use(sharedFunc)` — 匿名委托（自动创建 `AnonymousDelegatingChatClient`）

### 4.3 `DelegatingEmbeddingGenerator<TInput, TEmbedding>` — 嵌入生成器装饰器基类

与 `DelegatingChatClient` 对称的设计，用于构建嵌入生成器管线。

### 4.4 内置中间件一览

**Microsoft.Extensions.AI 包提供的中间件**：

| 中间件 | 继承自 | 功能 |
|--------|--------|------|
| **`LoggingChatClient`** | DelegatingChatClient | ILogger 日志记录（Debug/Trace 级别） |
| **`OpenTelemetryChatClient`** | DelegatingChatClient | OpenTelemetry 遥测（活动跟踪 + 指标） |
| **`FunctionInvokingChatClient`** | DelegatingChatClient | 自动函数调用循环 |
| **`CachingChatClient`** | DelegatingChatClient | 响应缓存（抽象基类） |
| **`DistributedCachingChatClient`** | CachingChatClient | 基于 IDistributedCache 的缓存 |
| **`ConfigureOptionsChatClient`** | DelegatingChatClient | 请求选项配置/变换 |
| **`ReducingChatClient`** | DelegatingChatClient | 消息缩减（上下文窗口管理） |
| **`ImageGeneratingChatClient`** | DelegatingChatClient | 图像生成集成 |
| **`ToolReducingChatClient`** | DelegatingChatClient | 工具列表缩减 |
| **`AnonymousDelegatingChatClient`** | (内部) | 匿名委托中间件 |

**嵌入生成器中间件**（对称设计）：

| 中间件 | 功能 |
|--------|------|
| `LoggingEmbeddingGenerator` | 日志 |
| `OpenTelemetryEmbeddingGenerator` | 遥测 |
| `CachingEmbeddingGenerator` | 缓存 |
| `DistributedCachingEmbeddingGenerator` | 分布式缓存 |
| `ConfigureOptionsEmbeddingGenerator` | 选项配置 |

**图像生成器和语音转文本也有对应的中间件**（Logging、OpenTelemetry、ConfigureOptions 等）。

### 4.5 `ChatReducer` 实现

| 实现 | 说明 |
|------|------|
| `MessageCountingChatReducer` | 基于消息数量裁剪 |
| `SummarizingChatReducer` | 使用 AI 模型摘要旧消息 |

### 4.6 `IToolReductionStrategy` 实现

| 实现 | 说明 |
|------|------|
| `EmbeddingToolReductionStrategy` | 基于嵌入相似度选择最相关的工具 |

---

## 5. 函数调用 / 工具使用 (Function Calling / Tool Use)

### 5.1 工具类型层次

```
AITool (abstract)                    ← 所有工具的基类
├── AIFunctionDeclaration (abstract) ← 可描述的函数（含 JSON Schema）
│   └── AIFunction (abstract)        ← 可调用的函数（含 InvokeAsync）
│       ├── ApprovalRequiredAIFunction ← 需审批的函数
│       └── DelegatingAIFunction       ← 委托给另一个 AIFunction
├── HostedCodeInterpreterTool        ← 服务端代码解释器
├── HostedFileSearchTool             ← 服务端文件搜索
├── HostedWebSearchTool              ← 服务端网络搜索
├── HostedImageGenerationTool        ← 服务端图像生成
└── HostedMcpServerTool              ← 服务端 MCP 服务器 (Experimental)
```

### 5.2 `AITool` 基类

```csharp
public abstract class AITool
{
    public virtual string Name { get; }
    public virtual string Description { get; }
    public virtual IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
    public virtual object? GetService(Type serviceType, object? serviceKey = null);
}
```

### 5.3 `AIFunctionDeclaration` — 函数声明

```csharp
public abstract class AIFunctionDeclaration : AITool
{
    public virtual JsonElement JsonSchema { get; }       // 输入参数的 JSON Schema
    public virtual JsonElement? ReturnJsonSchema { get; } // 返回值的 JSON Schema
}
```

### 5.4 `AIFunction` — 可调用函数

```csharp
public abstract class AIFunction : AIFunctionDeclaration
{
    public virtual MethodInfo? UnderlyingMethod { get; }
    public virtual JsonSerializerOptions JsonSerializerOptions { get; }

    // 调用函数
    public ValueTask<object?> InvokeAsync(
        AIFunctionArguments? arguments = null,
        CancellationToken cancellationToken = default);

    // 子类实现
    protected abstract ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken);

    // 创建仅声明版本（不可调用）
    public AIFunctionDeclaration AsDeclarationOnly();
}
```

### 5.5 `AIFunctionFactory` — 从 .NET 方法创建 AIFunction

```csharp
public static class AIFunctionFactory
{
    // 从委托创建
    public static AIFunction Create(Delegate method, AIFunctionFactoryOptions? options = null);

    // 从 MethodInfo + target 创建
    public static AIFunction Create(MethodInfo method, object? target = null,
        AIFunctionFactoryOptions? options = null);
}
```

**自动处理**：
- `CancellationToken` 参数自动绑定，不暴露在 Schema 中
- `IServiceProvider` 参数自动从 `AIFunctionArguments.Services` 绑定
- `AIFunctionArguments` 参数直接传递
- 其他参数从参数字典中反序列化
- 返回值如果是 `AIContent` 或其派生类型，直接返回不序列化

### 5.6 `AIFunctionArguments` — 函数参数

```csharp
public class AIFunctionArguments : IDictionary<string, object?>
{
    public IServiceProvider? Services { get; set; }           // DI 容器
    public IDictionary<object, object?>? Context { get; set; } // 附加上下文
}
```

### 5.7 `FunctionInvokingChatClient` — 自动函数调用

```csharp
public class FunctionInvokingChatClient : DelegatingChatClient
{
    public bool IncludeDetailedErrors { get; set; }       // 是否在错误中暴露详细信息
    public bool AllowConcurrentInvocation { get; set; }   // 是否并发调用多函数
    public int MaximumIterationsPerRequest { get; set; } = 40;   // 最大循环次数
    public int MaximumConsecutiveErrorsPerRequest { get; set; }  // 最大连续错误
}
```

**调用循环**：
1. 发送消息到内部 `IChatClient`
2. 收到 `FunctionCallContent` → 查找对应 `AIFunction` → 调用 → 生成 `FunctionResultContent`
3. 将结果添加到消息列表 → 重新发送
4. 重复直到无更多函数调用，或达到最大迭代数

**特殊处理**：
- `AIFunctionDeclaration`（不是 `AIFunction`）的函数调用不会自动执行，会透传给调用者
- `ApprovalRequiredAIFunction` 会生成 `FunctionApprovalRequestContent`，等待调用者审批
- 通过 `FunctionInvocationContext` 暴露当前调用上下文（可通过 `AsyncLocal` 访问）

### 5.8 Hosted Tools（服务端工具）

这些工具不在本地执行，而是作为标记传递给 AI 服务，由服务端执行：

| 工具 | Name | 说明 |
|------|------|------|
| `HostedCodeInterpreterTool` | "code_interpreter" | 服务端代码执行 |
| `HostedFileSearchTool` | "file_search" | 服务端文件搜索 |
| `HostedWebSearchTool` | "web_search" | 服务端网络搜索 |
| `HostedImageGenerationTool` | "image_generation" | 服务端图像生成 |
| `HostedMcpServerTool` | (server name) | 服务端 MCP 服务器 (Experimental) |

---

## 6. 依赖注入集成 (Dependency Injection)

### 6.1 注册 `IChatClient`

```csharp
// 基本注册
services.AddChatClient(new OpenAIChatClient(chatClient));

// 使用工厂
services.AddChatClient(sp => CreateMyClient(sp));

// 带管线
services.AddChatClient(sp => CreateMyClient(sp))
    .UseLogging()
    .UseOpenTelemetry()
    .UseFunctionInvocation();

// Keyed 注册
services.AddKeyedChatClient("gpt4", sp => CreateGpt4Client(sp));
```

**生命周期**：默认 `Singleton`，可通过 `lifetime` 参数控制。

返回 `ChatClientBuilder`，可链式调用 `.Use*()` 扩展方法配置管线。

### 6.2 扩展方法 (Builder Extensions)

每个中间件都提供了便捷的 Builder 扩展方法：

```csharp
builder.UseLogging(loggerFactory);           // LoggingChatClient
builder.UseOpenTelemetry(...);               // OpenTelemetryChatClient
builder.UseFunctionInvocation(...);          // FunctionInvokingChatClient
builder.UseDistributedCaching(...);          // DistributedCachingChatClient
builder.UseConfigureOptions(configure);      // ConfigureOptionsChatClient
builder.UseChatReduction(reducer);           // ReducingChatClient
builder.UseToolReduction(strategy);          // ToolReducingChatClient
```

### 6.3 嵌入生成器注册

```csharp
services.AddEmbeddingGenerator(sp => CreateMyGenerator(sp))
    .UseLogging()
    .UseOpenTelemetry();
```

### 6.4 图像生成器、语音转文本注册

同样的模式：
- `services.AddImageGenerator(...)` + `ImageGeneratorBuilder`
- `services.AddSpeechToTextClient(...)` + `SpeechToTextClientBuilder`

---

## 7. OpenTelemetry 集成 (Telemetry)

### 7.1 `OpenTelemetryChatClient`

实现 **OpenTelemetry Semantic Conventions for Generative AI systems v1.39**。

**Activity (分布式追踪)**：
- Source name: `Experimental.Microsoft.Extensions.AI` (或自定义)
- 记录请求/响应消息（当 `EnableSensitiveData = true`）
- 记录模型、提供商等属性

**Metrics (指标)**：
- `gen_ai.client.operation.duration` — 操作耗时直方图 (秒)
- `gen_ai.client.token.usage` — Token 使用量直方图

**敏感数据控制**：
- 默认不记录消息内容
- 可通过 `EnableSensitiveData = true` 或环境变量 `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true` 启用

### 7.2 对应的嵌入/图像/语音遥测

- `OpenTelemetryEmbeddingGenerator` — 嵌入生成遥测
- `OpenTelemetryImageGenerator` — 图像生成遥测
- `OpenTelemetrySpeechToTextClient` — 语音转文本遥测

### 7.3 关键遥测常量

```csharp
// Span/Activity 操作名
"chat", "embeddings", "execute_tool", "invoke_agent", "orchestrate_tools", "generate_content"

// 属性名
"gen_ai.system_instructions"
"error.type"

// Metric bucket boundaries (token usage)
[1, 4, 16, 64, 256, 1024, 4096, 16384, 65536, ...]

// Metric bucket boundaries (duration seconds)  
[0.01, 0.02, 0.04, ..., 81.92]
```

---

## 8. 具体实现 — OpenAI Provider (Microsoft.Extensions.AI.OpenAI)

### 8.1 提供的实现

| 类 | 接口 | 底层 SDK 类型 | 说明 |
|----|------|--------------|------|
| `OpenAIChatClient` (internal) | `IChatClient` | `OpenAI.Chat.ChatClient` | Chat Completions API |
| `OpenAIResponsesChatClient` (internal) | `IChatClient` | `OpenAI.Responses.ResponsesClient` | Responses API |
| `OpenAIAssistantsChatClient` (internal) | `IChatClient` | `OpenAI.Assistants.AssistantClient` | Assistants API |
| `OpenAIEmbeddingGenerator` (internal) | `IEmbeddingGenerator<string, Embedding<float>>` | `OpenAI.Embeddings.EmbeddingClient` | 嵌入生成 |
| `OpenAIImageGenerator` (internal) | `IImageGenerator` | `OpenAI.Images.ImageClient` | 图像生成 |
| `OpenAISpeechToTextClient` (internal) | `ISpeechToTextClient` | `OpenAI.Audio.AudioClient` | 语音转文本 |

### 8.2 入口扩展方法

所有实现类均为 `internal`，通过 `OpenAIClientExtensions` 的扩展方法访问：

```csharp
// Chat Completions
ChatClient chatClient = openAIClient.GetChatClient("gpt-4o");
IChatClient meaiClient = chatClient.AsIChatClient();

// Responses API
ResponsesClient responsesClient = openAIClient.GetResponsesClient();
IChatClient responsesAsChat = responsesClient.AsIChatClient();

// Assistants API
AssistantClient assistantClient = openAIClient.GetAssistantClient();
IChatClient assistantAsChat = assistantClient.AsIChatClient(assistantId: "asst_xxx");

// Embeddings
EmbeddingClient embeddingClient = openAIClient.GetEmbeddingClient("text-embedding-3-small");
IEmbeddingGenerator<string, Embedding<float>> generator = embeddingClient.AsIEmbeddingGenerator();

// Image Generation (Experimental)
ImageClient imageClient = openAIClient.GetImageClient("dall-e-3");
IImageGenerator imageGenerator = imageClient.AsIImageGenerator();

// Speech-to-Text (Experimental)
AudioClient audioClient = openAIClient.GetAudioClient("whisper-1");
ISpeechToTextClient sttClient = audioClient.AsISpeechToTextClient();
```

### 8.3 Provider 元数据

所有 OpenAI 实现在 `ChatClientMetadata` 中将 `ProviderName` 设为 `"openai"`。

### 8.4 JSON Schema Strict Mode

`OpenAIClientExtensions` 内置了 `StrictSchemaTransformCache`，自动将 AI 函数的 JSON Schema 转换为 OpenAI 的 strict 格式：
- 禁止 `additionalProperties`
- 将布尔 schema 转为对象 schema
- 移动 `default` 到 `description`
- 要求所有属性为 `required`
- 移除不支持的关键字（`minLength`、`maxLength`、`pattern` 等）到 `description`

---

## 9. 架构总结

```
┌─────────────────────────────────────────────────────────┐
│                    Application Code                      │
│              var response = await chatClient              │
│                .GetResponseAsync(messages, options);      │
└──────────────────────┬──────────────────────────────────┘
                       │
        ┌──────────────▼──────────────┐
        │    LoggingChatClient         │  ← ILogger
        │    (DelegatingChatClient)    │
        └──────────────┬──────────────┘
                       │
        ┌──────────────▼──────────────┐
        │  OpenTelemetryChatClient     │  ← ActivitySource + Meter
        │    (DelegatingChatClient)    │
        └──────────────┬──────────────┘
                       │
        ┌──────────────▼──────────────┐
        │ FunctionInvokingChatClient   │  ← Auto tool loop
        │    (DelegatingChatClient)    │
        └──────────────┬──────────────┘
                       │
        ┌──────────────▼──────────────┐
        │   ConfigureOptionsChatClient │  ← Options mutation
        │    (DelegatingChatClient)    │
        └──────────────┬──────────────┘
                       │
        ┌──────────────▼──────────────┐
        │     OpenAIChatClient         │  ← OpenAI SDK
        │        (IChatClient)         │
        └──────────────┬──────────────┘
                       │
                       ▼
              OpenAI REST API
```

### 设计模式要点

1. **装饰器模式 (Decorator)**：通过 `DelegatingChatClient` 层层包装
2. **构建器模式 (Builder)**：`ChatClientBuilder` 链式构建管线
3. **策略模式 (Strategy)**：`IChatReducer`、`IToolReductionStrategy` 可替换策略
4. **服务定位 (Service Locator)**：`GetService()` 方法提供管线穿透能力
5. **统一抽象 (Uniform Abstraction)**：所有 AI 能力（聊天、嵌入、图像、语音）采用一致的接口模式
6. **关注点分离**：Abstractions 包零实现依赖，中间件和实现分属不同包

### 与其他 AI 提供商的集成

`IChatClient` 和 `IEmbeddingGenerator<>` 是开放接口，任何提供商都可以实现：
- **OpenAI / Azure OpenAI**：由 `Microsoft.Extensions.AI.OpenAI` 包提供
- **Ollama**：社区或第三方包实现
- **其他提供商**：只需实现 `IChatClient` 接口即可插入管线

---

*本报告基于 dotnet/extensions 仓库中 `src/Libraries/Microsoft.Extensions.AI*` 目录的源码分析生成。*
