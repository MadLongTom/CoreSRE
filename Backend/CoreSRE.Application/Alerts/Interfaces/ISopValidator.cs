using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// SOP 结构化校验服务 — 检查自动生成的 SOP Markdown 是否符合标准结构
/// </summary>
public interface ISopValidator
{
    /// <summary>
    /// 校验 SOP Markdown 内容的结构化完整性。
    /// 检查必需段落、工具引用合法性、危险操作标记。
    /// </summary>
    /// <param name="sopContent">SOP Markdown 内容</param>
    /// <param name="registeredToolNames">系统中已注册的工具名称列表（用于工具引用校验）</param>
    SopValidationResultVO Validate(string sopContent, IReadOnlySet<string> registeredToolNames);
}
