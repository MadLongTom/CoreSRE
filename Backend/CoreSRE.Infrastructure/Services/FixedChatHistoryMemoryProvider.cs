using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// A drop-in replacement for <see cref="ChatHistoryMemoryProvider"/> that fixes the
/// Expression.AndAlso lambda-parameter-mismatch bug in SearchChatHistoryAsync.
///
/// <para>
/// <b>Root cause</b>: <c>ChatHistoryMemoryProvider.SearchChatHistoryAsync</c> builds
/// separate filter lambdas for each scope property (ApplicationId, AgentId, …), then
/// combines them via <c>Expression.Lambda(Expression.AndAlso(f1.Body, f2.Body), f1.Parameters)</c>.
/// Because <c>f2.Body</c> still references its own lambda parameter, the PgVector
/// <c>SqlFilterTranslator.TranslateUnary</c> cannot bind the second filter's
/// <c>(string?)x["…"]</c> Convert expression and throws
/// <c>NotSupportedException: Unsupported unary expression node type: Convert</c>.
/// </para>
///
/// <para>
/// <b>Fix</b>: This class uses an <see cref="ExpressionVisitor"/> to rebind all
/// filter lambda parameters to the same <see cref="ParameterExpression"/> before
/// combining with <c>AndAlso</c>, so <c>SqlFilterTranslator.TryBindProperty</c>
/// always recognises the record parameter.
/// </para>
/// </summary>
public sealed class FixedChatHistoryMemoryProvider : AIContextProvider, IDisposable
{
    // ── Constants ────────────────────────────────────────────────────────
    private const string DefaultContextPrompt =
        "## Reference Context (from past conversations)\n" +
        "Below are relevant excerpts retrieved from previous conversations via semantic search.\n" +
        "INSTRUCTIONS:\n" +
        "- Use this context ONLY if it directly helps answer the user's CURRENT question.\n" +
        "- Do NOT repeat, summarize, or recite this context unless explicitly asked to.\n" +
        "- Always prioritize the user's actual request — call tools, take actions, or answer directly.\n" +
        "- If the context is irrelevant to the current question, ignore it entirely.";
    private const int DefaultMaxResults = 3;
    private const string DefaultFunctionToolName = "Search";
    private const string DefaultFunctionToolDescription =
        "Allows searching for related previous chat history to help answer the user question.";

    // ── Fields ───────────────────────────────────────────────────────────
    private readonly VectorStore _vectorStore;
    private readonly VectorStoreCollection<object, Dictionary<string, object?>> _collection;
    private readonly int _maxResults;
    private readonly string _contextPrompt;
    private readonly bool _enableSensitiveTelemetryData;
    private readonly ChatHistoryMemoryProviderOptions.SearchBehavior _searchTime;
    private readonly AITool[] _tools;
    private readonly ILogger? _logger;
    private readonly double _minRelevanceScore;

    private readonly ChatHistoryMemoryProviderScope _storageScope;
    private readonly ChatHistoryMemoryProviderScope _searchScope;

    private bool _collectionInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    // ── Constructors ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new instance from explicit storage/search scopes.
    /// </summary>
    public FixedChatHistoryMemoryProvider(
        VectorStore vectorStore,
        string collectionName,
        int vectorDimensions,
        ChatHistoryMemoryProviderScope storageScope,
        ChatHistoryMemoryProviderScope? searchScope = null,
        ChatHistoryMemoryProviderOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        double minRelevanceScore = 0.0)
        : this(
            vectorStore,
            collectionName,
            vectorDimensions,
            new ProviderState
            {
                StorageScope = new(storageScope ?? throw new ArgumentNullException(nameof(storageScope))),
                SearchScope = searchScope ?? new(storageScope),
            },
            options,
            loggerFactory,
            minRelevanceScore)
    { }



    /// <summary>Internal canonical constructor.</summary>
    private FixedChatHistoryMemoryProvider(
        VectorStore vectorStore,
        string collectionName,
        int vectorDimensions,
        ProviderState? state,
        ChatHistoryMemoryProviderOptions? options,
        ILoggerFactory? loggerFactory,
        double minRelevanceScore)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        options ??= new ChatHistoryMemoryProviderOptions();

        _maxResults = options.MaxResults is > 0 ? options.MaxResults.Value : DefaultMaxResults;
        _contextPrompt = options.ContextPrompt ?? DefaultContextPrompt;
        _enableSensitiveTelemetryData = options.EnableSensitiveTelemetryData;
        _searchTime = options.SearchTime;
        _logger = loggerFactory?.CreateLogger<FixedChatHistoryMemoryProvider>();
        _minRelevanceScore = minRelevanceScore;

        if (state?.StorageScope is null || state.SearchScope is null)
            throw new InvalidOperationException(
                $"The {nameof(FixedChatHistoryMemoryProvider)} state did not contain the required scope properties.");

        _storageScope = state.StorageScope;
        _searchScope = state.SearchScope;

        _tools =
        [
            AIFunctionFactory.Create(
                (Func<string, CancellationToken, Task<string>>)SearchTextAsync,
                name: options.FunctionToolName ?? DefaultFunctionToolName,
                description: options.FunctionToolDescription ?? DefaultFunctionToolDescription)
        ];

        if (vectorDimensions < 1)
            throw new ArgumentOutOfRangeException(nameof(vectorDimensions));

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Key", typeof(Guid)),
                new VectorStoreDataProperty("Role", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("MessageId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AuthorName", typeof(string)),
                new VectorStoreDataProperty("ApplicationId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("AgentId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("UserId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("SessionId", typeof(string)) { IsIndexed = true },
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("CreatedAt", typeof(string)) { IsIndexed = true },
                new VectorStoreVectorProperty("ContentEmbedding", typeof(string), vectorDimensions)
            ]
        };

        _collection = _vectorStore.GetDynamicCollection(
            collectionName ?? throw new ArgumentNullException(nameof(collectionName)),
            definition);
    }

    // ── AIContextProvider overrides ──────────────────────────────────────

    /// <inheritdoc />
    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_searchTime == ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling)
            return new AIContext { Tools = _tools };

        try
        {
            var messages = context.AIContext.Messages ?? [];
            var requestText = string.Join("\n", messages
                .Where(m => m.GetAgentRequestMessageSourceType() == AgentRequestMessageSourceType.External)
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => m.Text));

            if (string.IsNullOrWhiteSpace(requestText))
                return new AIContext();

            var contextText = await SearchTextAsync(requestText, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(contextText))
                return new AIContext();

            var memoryMsg = new ChatMessage(ChatRole.System, contextText);
            memoryMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            memoryMsg.AdditionalProperties["source"] = "memory";

            return new AIContext
            {
                Messages = [memoryMsg]
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "FixedChatHistoryMemoryProvider: Search failed. ApplicationId='{AppId}', AgentId='{AgentId}', SessionId='{SessionId}', UserId='{UserId}'.",
                _searchScope.ApplicationId, _searchScope.AgentId,
                _searchScope.SessionId, Sanitize(_searchScope.UserId));

            return new AIContext();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.InvokeException is not null)
            return;

        try
        {
            var collection = await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);

            var items = context.RequestMessages
                .Where(m => m.GetAgentRequestMessageSourceType() == AgentRequestMessageSourceType.External)
                .Concat(context.ResponseMessages ?? [])
                .Where(msg => !string.IsNullOrWhiteSpace(msg.Text)) // skip tool-calls / empty messages
                .Select(msg => new Dictionary<string, object?>
                {
                    ["Key"] = Guid.NewGuid(),
                    ["Role"] = msg.Role.ToString(),
                    ["MessageId"] = msg.MessageId,
                    ["AuthorName"] = msg.AuthorName,
                    ["ApplicationId"] = _storageScope.ApplicationId,
                    ["AgentId"] = _storageScope.AgentId,
                    ["UserId"] = _storageScope.UserId,
                    ["SessionId"] = _storageScope.SessionId,
                    ["Content"] = msg.Text,
                    ["CreatedAt"] = msg.CreatedAt?.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
                    ["ContentEmbedding"] = msg.Text,
                })
                .ToList();

            if (items.Count > 0)
                await collection.UpsertAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "FixedChatHistoryMemoryProvider: Failed to store messages. ApplicationId='{AppId}', AgentId='{AgentId}', SessionId='{SessionId}', UserId='{UserId}'.",
                _storageScope.ApplicationId, _storageScope.AgentId,
                _storageScope.SessionId, Sanitize(_storageScope.UserId));
        }
    }


    // ── Search implementation (FIXED) ────────────────────────────────────

    internal async Task<string> SearchTextAsync(string userQuestion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
            return string.Empty;

        var results = await SearchChatHistoryAsync(userQuestion, _maxResults, ct).ConfigureAwait(false);
        if (results.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(_contextPrompt);
        sb.AppendLine();

        var entryIndex = 0;
        foreach (var (record, score) in results)
        {
            var content = (string?)record["Content"];
            if (string.IsNullOrWhiteSpace(content)) continue;

            entryIndex++;
            var role = (string?)record["Role"] ?? "unknown";
            var createdAt = (string?)record["CreatedAt"];
            var scoreStr = score.HasValue ? $"{score.Value:F2}" : "N/A";

            sb.AppendLine($"[{entryIndex}] (relevance: {scoreStr}, role: {role}, time: {createdAt ?? "unknown"})");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        if (entryIndex == 0)
            return string.Empty;

        var formatted = sb.ToString().TrimEnd();

        _logger?.LogTrace(
            "FixedChatHistoryMemoryProvider: Search Results\nInput:{Input}\nOutput:{Output}\n AppId='{AppId}', AgentId='{AgentId}', SessionId='{SessionId}', UserId='{UserId}'.",
            Sanitize(userQuestion), Sanitize(formatted),
            _searchScope.ApplicationId, _searchScope.AgentId,
            _searchScope.SessionId, Sanitize(_searchScope.UserId));

        return formatted;
    }

    /// <summary>
    /// Searches for relevant chat history using semantic similarity with properly
    /// combined filter expressions (the bug fix).
    /// </summary>
    private async Task<List<(Dictionary<string, object?> Record, double? Score)>> SearchChatHistoryAsync(
        string queryText, int top, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return [];

        var collection = await EnsureCollectionAsync(ct).ConfigureAwait(false);

        // ── Build a unified filter ──────────────────────────────────
        // FIX: Use CombineFilters helper which rebinds lambda parameters
        // via ExpressionVisitor before AndAlso, preventing the
        // "Unsupported unary expression node type: Convert" error.
        var filter = BuildSearchFilter();

        var searchResults = collection.SearchAsync(
            queryText, top,
            options: new() { Filter = filter },
            cancellationToken: ct);

        var results = new List<(Dictionary<string, object?> Record, double? Score)>();
        await foreach (var result in searchResults.WithCancellation(ct).ConfigureAwait(false))
        {
            // Filter by minimum relevance score (higher = more relevant)
            if (_minRelevanceScore > 0 && result.Score.HasValue && result.Score.Value < _minRelevanceScore)
                continue;

            results.Add((result.Record, result.Score));
        }

        _logger?.LogInformation(
            "FixedChatHistoryMemoryProvider: Retrieved {Count} results (minScore={MinScore:F2}). AppId='{AppId}', AgentId='{AgentId}', SessionId='{SessionId}', UserId='{UserId}'.",
            results.Count, _minRelevanceScore, _searchScope.ApplicationId, _searchScope.AgentId,
            _searchScope.SessionId, Sanitize(_searchScope.UserId));

        return results;
    }

    /// <summary>
    /// Builds a single, correctly-parameterised filter expression from all
    /// non-null search-scope properties.
    /// </summary>
    private Expression<Func<Dictionary<string, object?>, bool>>? BuildSearchFilter()
    {
        Expression<Func<Dictionary<string, object?>, bool>>? filter = null;

        if (_searchScope.ApplicationId is { } appId)
            filter = CombineFilters(filter, x => (string?)x["ApplicationId"] == appId);

        if (_searchScope.AgentId is { } agentId)
            filter = CombineFilters(filter, x => (string?)x["AgentId"] == agentId);

        if (_searchScope.UserId is { } userId)
            filter = CombineFilters(filter, x => (string?)x["UserId"] == userId);

        // SessionId is intentionally NOT used as a search filter —
        // semantic memory is cross-session by design: we want to recall
        // relevant context from ALL past conversations, not just the current one.
        // SessionId is still stored on each record for cascade-delete purposes.

        return filter;
    }

    // ── Expression helpers ───────────────────────────────────────────────

    /// <summary>
    /// Safely combines two filter lambdas with AndAlso by rebinding the
    /// right-hand lambda's parameter to match the left-hand one.
    /// </summary>
    private static Expression<Func<T, bool>> CombineFilters<T>(
        Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>> right)
    {
        if (left is null)
            return right;

        // Rebind right.Body so that it references left's parameter
        var rewriter = new ParameterReplacer(right.Parameters[0], left.Parameters[0]);
        var rewrittenRight = rewriter.Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(left.Body, rewrittenRight),
            left.Parameters);
    }

    /// <summary>
    /// Replaces all occurrences of one <see cref="ParameterExpression"/>
    /// with another in an expression tree.
    /// </summary>
    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

    // ── Collection initialisation ────────────────────────────────────────

    private async Task<VectorStoreCollection<object, Dictionary<string, object?>>> EnsureCollectionAsync(
        CancellationToken ct)
    {
        if (_collectionInitialized)
            return _collection;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_collectionInitialized)
                return _collection;

            await _collection.EnsureCollectionExistsAsync(ct).ConfigureAwait(false);
            _collectionInitialized = true;
            return _collection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Serialization helpers ────────────────────────────────────────────

    private static ProviderState? DeserializeState(
        JsonElement element, JsonSerializerOptions? jso)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        jso ??= JsonSerializerDefaults;
        return element.Deserialize<ProviderState>(jso);
    }

    private string? Sanitize(string? data) =>
        _enableSensitiveTelemetryData ? data : "<redacted>";

    // ── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initLock.Dispose();
        _collection?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Default options matching the framework's web-friendly serialization.</summary>
    private static readonly JsonSerializerOptions JsonSerializerDefaults = new(System.Text.Json.JsonSerializerDefaults.Web);

    // ── State class (matches ChatHistoryMemoryProviderState layout) ──────
    internal sealed class ProviderState
    {
        public ChatHistoryMemoryProviderScope? StorageScope { get; set; }
        public ChatHistoryMemoryProviderScope? SearchScope { get; set; }
    }
}
