namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 条件表达式求值接口。评估 JSON Path 条件表达式是否匹配给定 JSON 输出。
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// 评估条件表达式（如 $.severity == "high"）是否对给定 JSON 输出匹配。
    /// </summary>
    /// <param name="condition">条件表达式，格式为 &lt;jsonPath&gt; == &lt;expectedValue&gt;</param>
    /// <param name="jsonOutput">上一节点的 JSON 输出字符串</param>
    /// <returns>匹配返回 true，不匹配返回 false</returns>
    bool Evaluate(string condition, string jsonOutput);

    /// <summary>
    /// 尝试评估条件表达式，失败时返回 false（不抛异常）。
    /// </summary>
    /// <param name="condition">条件表达式</param>
    /// <param name="jsonOutput">JSON 输出字符串</param>
    /// <param name="result">评估结果</param>
    /// <returns>解析和评估是否成功</returns>
    bool TryEvaluate(string condition, string jsonOutput, out bool result);
}
