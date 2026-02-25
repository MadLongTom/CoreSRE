using System.Text.RegularExpressions;
using CoreSRE.Domain.Enums;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 告警标签匹配条件值对象（对齐 Alertmanager route.matchers 语法）。
/// 存储为 JSONB 数组元素。
/// </summary>
public sealed record AlertMatcherVO
{
    /// <summary>标签名，如 "alertname", "service", "severity"</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>匹配操作符</summary>
    public MatchOp Operator { get; init; } = MatchOp.Eq;

    /// <summary>匹配值，如 "HighErrorRate", "order-service"</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// 判断给定的标签值是否匹配当前匹配器。
    /// </summary>
    public bool IsMatch(string? actualValue)
    {
        return Operator switch
        {
            MatchOp.Eq => string.Equals(actualValue, Value, StringComparison.Ordinal),
            MatchOp.Neq => !string.Equals(actualValue, Value, StringComparison.Ordinal),
            MatchOp.Regex => actualValue is not null && Regex.IsMatch(actualValue, Value),
            MatchOp.NotRegex => actualValue is null || !Regex.IsMatch(actualValue, Value),
            _ => false
        };
    }
}
