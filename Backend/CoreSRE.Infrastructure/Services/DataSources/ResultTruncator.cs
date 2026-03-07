using System.Text.Json;

namespace CoreSRE.Infrastructure.Services.DataSources;

/// <summary>
/// 智能截断工具 — 确保数据源查询结果不超过 LLM context window 限制。
/// Token 估算使用 characters / 4 近似。
/// </summary>
public static class ResultTruncator
{
    /// <summary>默认最大 token 数（约 4000 token ≈ 16000 字符）</summary>
    public const int DefaultMaxTokens = 4000;

    /// <summary>Token 估算因子（1 token ≈ 4 字符）</summary>
    private const int CharsPerToken = 4;

    /// <summary>
    /// 截断 JSON 数组字符串。超出 maxTokens 限制时保留最新的 N 条条目。
    /// </summary>
    public static string TruncateJsonArray(string json, int maxTokens = DefaultMaxTokens, string dataType = "entries")
    {
        if (string.IsNullOrEmpty(json))
            return json;

        var maxChars = maxTokens * CharsPerToken;
        if (json.Length <= maxChars)
            return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return TruncatePlainText(json, maxTokens);

            var totalCount = doc.RootElement.GetArrayLength();
            if (totalCount == 0)
                return json;

            // Binary search for the max number of tail entries that fit
            var kept = FindMaxTailEntries(doc.RootElement, maxChars, totalCount);

            if (kept >= totalCount)
                return json;

            // Serialize only the last N entries
            var truncated = doc.RootElement.EnumerateArray()
                .Skip(totalCount - kept)
                .Select(e => e.GetRawText());

            var result = $"[{string.Join(",", truncated)}]";
            return $"{result}\n[truncated: showing latest {kept} of {totalCount} {dataType}]";
        }
        catch (JsonException)
        {
            return TruncatePlainText(json, maxTokens);
        }
    }

    /// <summary>
    /// 截断纯文本。超出限制时保留末尾内容。
    /// </summary>
    public static string TruncatePlainText(string text, int maxTokens = DefaultMaxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var maxChars = maxTokens * CharsPerToken;
        if (text.Length <= maxChars)
            return text;

        // Count total lines for the truncation message
        var totalLines = text.AsSpan().Count('\n') + 1;
        var truncated = text[^maxChars..];

        // Find first complete line start
        var newlineIdx = truncated.IndexOf('\n');
        if (newlineIdx >= 0 && newlineIdx < truncated.Length - 1)
            truncated = truncated[(newlineIdx + 1)..];

        var keptLines = truncated.AsSpan().Count('\n') + 1;
        return $"[truncated: showing latest {keptLines} of {totalLines} lines]\n{truncated}";
    }

    private static int FindMaxTailEntries(JsonElement array, int maxChars, int totalCount)
    {
        // Start from the end, greedily add entries until budget exceeded
        var budget = maxChars - 100; // reserve for brackets + truncation message
        var kept = 0;
        var usedChars = 0;

        var elements = array.EnumerateArray().ToList();
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            var entryLen = elements[i].GetRawText().Length + 1; // +1 for comma
            if (usedChars + entryLen > budget)
                break;
            usedChars += entryLen;
            kept++;
        }

        return Math.Max(kept, 1); // always keep at least 1
    }
}
