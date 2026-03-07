namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// SOP 结构化校验结果
/// </summary>
public sealed record SopValidationResultVO
{
    /// <summary>是否通过校验（无 Error 级别问题）</summary>
    public bool IsValid { get; init; }

    /// <summary>Error 级错误列表（阻塞发布）</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>Warning 级警告列表（不阻塞但记录）</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>需审批的步骤序号（危险工具操作）</summary>
    public List<int> DangerousSteps { get; init; } = [];

    public static SopValidationResultVO Valid(List<string>? warnings = null, List<int>? dangerousSteps = null) =>
        new() { IsValid = true, Warnings = warnings ?? [], DangerousSteps = dangerousSteps ?? [] };

    public static SopValidationResultVO WithErrors(List<string> errors, List<string>? warnings = null, List<int>? dangerousSteps = null) =>
        new() { IsValid = false, Errors = errors, Warnings = warnings ?? [], DangerousSteps = dangerousSteps ?? [] };
}
