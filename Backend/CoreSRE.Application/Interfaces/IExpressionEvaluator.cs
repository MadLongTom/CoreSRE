using System.Text.Json;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// 表达式引擎接口。在工作流执行时对 {{ ... }} 模板表达式求值，
/// 支持引用上游节点输出、当前输入、执行上下文等。
/// </summary>
public interface IExpressionEvaluator : IDisposable
{
    /// <summary>
    /// 解析字符串中的 {{ ... }} 表达式并替换为运行时值。
    /// </summary>
    /// <param name="template">包含 {{ expr }} 占位符的字符串</param>
    /// <param name="context">表达式求值上下文（节点输出、当前输入等）</param>
    /// <returns>所有表达式被替换后的字符串</returns>
    string Evaluate(string template, ExpressionContext context);

    /// <summary>
    /// 对单个 JavaScript 表达式求值并返回结果的 JSON 字符串。
    /// </summary>
    /// <param name="expression">JavaScript 表达式（不带 {{ }}）</param>
    /// <param name="context">表达式求值上下文</param>
    /// <returns>求值结果的 JSON 字符串</returns>
    string EvaluateExpression(string expression, ExpressionContext context);

    /// <summary>
    /// 对条件表达式求值，返回布尔值。
    /// 可用于替代 IConditionEvaluator，支持完整的 JS 条件逻辑。
    /// </summary>
    /// <param name="condition">JavaScript 条件表达式</param>
    /// <param name="context">表达式求值上下文</param>
    /// <returns>条件是否为真</returns>
    bool EvaluateCondition(string condition, ExpressionContext context);
}

/// <summary>
/// 表达式求值上下文 — 提供 $input、$node、$execution 等内置变量的数据源。
/// </summary>
public sealed class ExpressionContext
{
    /// <summary>
    /// 各节点的输出数据（JSON 字符串），按 nodeId 索引。
    /// 每个节点可执行多次（循环场景），所以值是 List。
    /// 当前阶段简化为只取最后一次运行的输出。
    /// </summary>
    public Dictionary<string, List<string?>> NodeOutputs { get; init; } = new();

    /// <summary>
    /// 当前节点的输入数据（JSON 字符串）。
    /// </summary>
    public string? CurrentInput { get; init; }

    /// <summary>
    /// 当前工作流执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 当前工作流定义 ID。
    /// </summary>
    public Guid WorkflowId { get; init; }

    /// <summary>
    /// 全局变量（可选，可在 WorkflowDefinition 中定义）。
    /// </summary>
    public Dictionary<string, string>? Variables { get; init; }
}
