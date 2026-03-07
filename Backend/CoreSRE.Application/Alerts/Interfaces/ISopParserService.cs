using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// SOP 解析结果。
/// </summary>
public record SopParseResult
{
    /// <summary>SOP 名称（kebab-case）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>SOP 描述（从"适用条件"段提取）</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>SOP 全文 Markdown</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>SOP 中引用的工具/函数名列表</summary>
    public List<string> ReferencedToolNames { get; init; } = [];

    /// <summary>SOP 中声明的上下文初始化条目（从 ## 初始化上下文 段提取）</summary>
    public List<ContextInitItemVO> ContextInitItems { get; init; } = [];
}

/// <summary>
/// 解析 LLM 输出的 SOP Markdown → 结构化结果。
/// </summary>
public interface ISopParserService
{
    SopParseResult Parse(string sopMarkdown, string alertName);
}
