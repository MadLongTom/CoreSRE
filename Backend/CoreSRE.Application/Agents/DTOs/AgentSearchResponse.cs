namespace CoreSRE.Application.Agents.DTOs;

/// <summary>
/// Agent 技能搜索 API 响应信封，包含搜索结果列表和元数据
/// </summary>
public class AgentSearchResponse
{
    /// <summary>匹配 Agent 列表，按相关性排序</summary>
    public List<AgentSearchResultDto> Results { get; set; } = [];

    /// <summary>搜索模式：keyword / semantic / keyword-fallback</summary>
    public string SearchMode { get; set; } = "keyword";

    /// <summary>原始搜索文本</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>匹配 Agent 总数</summary>
    public int TotalCount { get; set; }
}
