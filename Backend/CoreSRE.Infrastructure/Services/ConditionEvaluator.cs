using System.Text.Json.Nodes;
using CoreSRE.Application.Interfaces;
using Json.Path;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 条件表达式求值器。使用 JsonPath.Net 对 JSON 输出评估条件表达式。
/// 表达式格式: &lt;jsonPath&gt; == &lt;expectedValue&gt;
/// 示例: $.severity == "high"
/// </summary>
public class ConditionEvaluator : IConditionEvaluator
{
    private const string EqualityOperator = " == ";

    /// <inheritdoc/>
    public bool Evaluate(string condition, string jsonOutput)
    {
        var parts = condition.Split(EqualityOperator, 2, StringSplitOptions.None);
        if (parts.Length != 2)
            throw new FormatException($"无效的条件表达式: {condition}");

        var jsonPathStr = parts[0].Trim();
        var expectedValue = parts[1].Trim().Trim('"');

        var path = JsonPath.Parse(jsonPathStr);
        var jsonNode = JsonNode.Parse(jsonOutput);
        var result = path.Evaluate(jsonNode);

        if (result.Matches is null || result.Matches.Count == 0)
            return false;

        var matchNode = result.Matches[0].Value;
        var actualValue = matchNode switch
        {
            JsonValue jv when jv.TryGetValue(out string? sv) => sv,
            _ => matchNode?.ToJsonString()?.Trim('"')
        };

        return string.Equals(actualValue, expectedValue, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public bool TryEvaluate(string condition, string jsonOutput, out bool result)
    {
        try
        {
            result = Evaluate(condition, jsonOutput);
            return true;
        }
        catch
        {
            result = false;
            return false;
        }
    }
}
