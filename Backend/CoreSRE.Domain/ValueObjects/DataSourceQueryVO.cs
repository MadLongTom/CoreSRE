namespace CoreSRE.Domain.ValueObjects;

/// <summary>数据源统一查询模型</summary>
public sealed record DataSourceQueryVO
{
    /// <summary>时间范围</summary>
    public TimeRangeVO? TimeRange { get; init; }

    /// <summary>标签过滤条件</summary>
    public List<LabelFilterVO>? Filters { get; init; }

    /// <summary>查询表达式（PromQL / LogQL / TraceID / KQL 等）</summary>
    public string? Expression { get; init; }

    /// <summary>分页控制</summary>
    public PaginationVO? Pagination { get; init; }

    /// <summary>产品特有参数透传</summary>
    public Dictionary<string, string>? AdditionalParams { get; init; }
}

/// <summary>时间范围</summary>
public sealed record TimeRangeVO
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>Metrics 步长，如 "15s", "1m", "5m"</summary>
    public string? Step { get; init; }
}

/// <summary>标签过滤条件</summary>
public sealed record LabelFilterVO
{
    public string Key { get; init; } = string.Empty;
    public LabelOperator Operator { get; init; } = LabelOperator.Eq;
    public string Value { get; init; } = string.Empty;
}

/// <summary>标签过滤操作符</summary>
public enum LabelOperator
{
    Eq,
    Neq,
    Regex,
    NotRegex
}

/// <summary>分页</summary>
public sealed record PaginationVO
{
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
}
