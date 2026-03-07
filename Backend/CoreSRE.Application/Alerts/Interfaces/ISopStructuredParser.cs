using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// SOP Markdown 结构化解析器接口
/// </summary>
public interface ISopStructuredParser
{
    /// <summary>
    /// 将 SOP Markdown 解析为结构化步骤列表
    /// </summary>
    List<SopStepDefinition> Parse(string sopContent, IReadOnlySet<string> dangerousToolPrefixes);
}
