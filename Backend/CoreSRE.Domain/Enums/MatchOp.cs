namespace CoreSRE.Domain.Enums;

/// <summary>
/// AlertMatcher 匹配操作符（对齐 Alertmanager route / matchers 语法）
/// </summary>
public enum MatchOp
{
    /// <summary>精确相等 (=)</summary>
    Eq,

    /// <summary>不等 (!=)</summary>
    Neq,

    /// <summary>正则匹配 (=~)</summary>
    Regex,

    /// <summary>正则不匹配 (!~)</summary>
    NotRegex
}
