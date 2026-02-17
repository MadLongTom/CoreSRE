namespace CoreSRE.Domain.ValueObjects;

/// <summary>数据源统一响应模型</summary>
public sealed record DataSourceResultVO
{
    /// <summary>响应数据类型</summary>
    public DataSourceResultType ResultType { get; init; }

    /// <summary>指标时间序列（Prometheus / VictoriaMetrics / Mimir）</summary>
    public List<TimeSeriesVO>? TimeSeries { get; init; }

    /// <summary>日志条目（Loki / Elasticsearch）</summary>
    public List<LogEntryVO>? LogEntries { get; init; }

    /// <summary>追踪 Span（Jaeger / Tempo）</summary>
    public List<SpanVO>? Spans { get; init; }

    /// <summary>告警（Alertmanager / PagerDuty）</summary>
    public List<AlertVO>? Alerts { get; init; }

    /// <summary>资源（Kubernetes / ArgoCD / GitHub）</summary>
    public List<ResourceVO>? Resources { get; init; }

    /// <summary>总结果数（分页用）</summary>
    public int? TotalCount { get; init; }

    /// <summary>是否因 MaxResults 截断</summary>
    public bool Truncated { get; init; }
}

/// <summary>结果类型枚举</summary>
public enum DataSourceResultType
{
    TimeSeries,
    LogEntries,
    Spans,
    Alerts,
    Resources
}

/// <summary>时间序列 VO</summary>
public sealed record TimeSeriesVO
{
    public string MetricName { get; init; } = string.Empty;
    public Dictionary<string, string> Labels { get; init; } = new();
    public List<DataPointVO> DataPoints { get; init; } = [];
}

/// <summary>数据点</summary>
public sealed record DataPointVO
{
    public DateTime Timestamp { get; init; }
    public double Value { get; init; }
}

/// <summary>日志条目 VO</summary>
public sealed record LogEntryVO
{
    public DateTime Timestamp { get; init; }
    public string? Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string>? Labels { get; init; }
    public string? Source { get; init; }
    public string? TraceId { get; init; }
}

/// <summary>Span VO</summary>
public sealed record SpanVO
{
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string? ParentSpanId { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public long DurationMicros { get; init; }
    public string? Status { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public DateTime StartTime { get; init; }
}

/// <summary>告警 VO</summary>
public sealed record AlertVO
{
    public string? AlertId { get; init; }
    public string AlertName { get; init; } = string.Empty;
    public string? Severity { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
    public Dictionary<string, string>? Annotations { get; init; }
    public string? Fingerprint { get; init; }
}

/// <summary>资源 VO（Kubernetes / ArgoCD / GitHub）</summary>
public sealed record ResourceVO
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Namespace { get; init; }
    public string? Status { get; init; }
    public Dictionary<string, string>? Labels { get; init; }
    public Dictionary<string, object?>? Properties { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
