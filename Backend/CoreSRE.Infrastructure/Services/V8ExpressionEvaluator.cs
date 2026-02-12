using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoreSRE.Application.Interfaces;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 基于 V8 引擎 (ClearScript) 的表达式求值器。
/// 
/// 支持 n8n 风格的模板表达式 {{ expr }}，在内嵌 V8 沙箱中执行 JavaScript 表达式。
/// 提供以下内置变量：
/// <list type="bullet">
///   <item><c>$input</c> — 当前节点的输入数据（JSON 对象）</item>
///   <item><c>$node["nodeId"]</c> — 指定节点的输出数据</item>
///   <item><c>$execution</c> — 工作流执行元数据（id, workflowId）</item>
///   <item><c>$vars</c> — 全局用户变量</item>
///   <item><c>$json</c> — $input 的快捷别名</item>
/// </list>
/// 
/// 线程安全说明：V8ScriptEngine 不是线程安全的。
/// 本实现为每次求值创建独立引擎实例，保证隔离性和线程安全。
/// </summary>
public sealed partial class V8ExpressionEvaluator : IExpressionEvaluator
{
    private readonly ILogger<V8ExpressionEvaluator> _logger;

    /// <summary>
    /// 匹配 {{ ... }} 模板占位符。支持嵌套大括号（如 {{ {a: 1} }}）。
    /// 使用惰性匹配，以 }} 为结束标记。
    /// </summary>
    [GeneratedRegex(@"\{\{(.+?)\}\}", RegexOptions.Singleline)]
    private static partial Regex TemplatePattern();

    /// <summary>
    /// V8 引擎约束：限制内存，防止恶意表达式。
    /// </summary>
    private static readonly V8RuntimeConstraints RuntimeConstraints = new()
    {
        MaxOldSpaceSize = 16, // MB - 表达式求值不需要大内存
    };

    /// <summary>
    /// 单次表达式求值超时。
    /// </summary>
    private static readonly TimeSpan EvalTimeout = TimeSpan.FromSeconds(5);

    public V8ExpressionEvaluator(ILogger<V8ExpressionEvaluator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Evaluate(string template, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        var matches = TemplatePattern().Matches(template);
        if (matches.Count == 0)
            return template; // 无表达式，原样返回

        // 如果整个字符串就是一个表达式 {{ expr }}，直接求值并保留原始类型
        if (matches.Count == 1
            && matches[0].Index == 0
            && matches[0].Length == template.Length)
        {
            return EvaluateExpression(matches[0].Groups[1].Value.Trim(), context);
        }

        // 多个表达式或混合文本 — 逐个替换为字符串
        var sb = new StringBuilder(template);
        // 反向替换以保持偏移量正确
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var expr = match.Groups[1].Value.Trim();
            var result = EvaluateExpression(expr, context);
            sb.Remove(match.Index, match.Length);
            sb.Insert(match.Index, result);
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public string EvaluateExpression(string expression, ExpressionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);

        using var engine = CreateSandboxedEngine();
        InjectContext(engine, context);

        try
        {
            var result = engine.Evaluate(expression);

            // V8 对象/数组：使用 JSON.stringify 在引擎内序列化
            if (result is ScriptObject)
            {
                var json = engine.Evaluate($"JSON.stringify({expression})");
                return json?.ToString() ?? "null";
            }

            return SerializeResult(result);
        }
        catch (ScriptEngineException ex)
        {
            _logger.LogWarning(ex, "V8 表达式求值失败: {Expression}", expression);
            throw new ExpressionEvaluationException(expression, ex);
        }
    }

    /// <inheritdoc/>
    public bool EvaluateCondition(string condition, ExpressionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(context);

        using var engine = CreateSandboxedEngine();
        InjectContext(engine, context);

        try
        {
            var result = engine.Evaluate(condition);
            return IsTruthy(result);
        }
        catch (ScriptEngineException ex)
        {
            _logger.LogWarning(ex, "V8 条件求值失败: {Condition}", condition);
            throw new ExpressionEvaluationException(condition, ex);
        }
    }

    /// <summary>
    /// 创建受限的 V8 引擎实例。禁用宿主对象访问、require 等危险功能。
    /// </summary>
    private static V8ScriptEngine CreateSandboxedEngine()
    {
        var engine = new V8ScriptEngine(
            RuntimeConstraints,
            V8ScriptEngineFlags.DisableGlobalMembers
            | V8ScriptEngineFlags.EnableDateTimeConversion);

        // 注入安全的辅助函数
        engine.Execute(@"
            // JSON 辅助
            function $parseJson(s) {
                try { return JSON.parse(s); }
                catch { return s; }
            }
            function $toJson(o) {
                return JSON.stringify(o);
            }
            // 字符串模板拼接辅助
            function $concat() {
                return Array.from(arguments).join('');
            }
            // 安全取值（类似 lodash _.get）
            function $get(obj, path, defaultValue) {
                if (obj == null) return defaultValue;
                var keys = path.replace(/\[(\d+)\]/g, '.$1').split('.');
                var result = obj;
                for (var i = 0; i < keys.length; i++) {
                    if (result == null) return defaultValue;
                    result = result[keys[i]];
                }
                return result === undefined ? defaultValue : result;
            }
        ");

        return engine;
    }

    /// <summary>
    /// 将 ExpressionContext 注入到 V8 引擎作为全局变量。
    /// </summary>
    private static void InjectContext(V8ScriptEngine engine, ExpressionContext context)
    {
        // $input — 当前节点输入
        if (context.CurrentInput is not null)
        {
            engine.Execute($"var $input = $parseJson({JsonEncode(context.CurrentInput)});");
            engine.Execute("var $json = $input;");
        }
        else
        {
            engine.Execute("var $input = {}; var $json = $input;");
        }

        // $node — 各节点输出的字典
        // 构建: $node = { "nodeId": { json: <parsed>, text: <raw> }, ... }
        var nodeBuilder = new StringBuilder("var $node = {};");
        foreach (var (nodeId, outputs) in context.NodeOutputs)
        {
            // 取最后一次输出
            var lastOutput = outputs.Count > 0 ? outputs[^1] : null;
            var escapedId = JsonEncode(nodeId);
            var escapedOutput = lastOutput is not null ? JsonEncode(lastOutput) : "\"{}\"";

            nodeBuilder.Append($"$node[{escapedId}] = {{");
            nodeBuilder.Append($"json: $parseJson({escapedOutput}),");
            nodeBuilder.Append($"text: {escapedOutput}");
            nodeBuilder.Append("};");
        }
        engine.Execute(nodeBuilder.ToString());

        // $execution — 执行元数据
        engine.Execute($@"
            var $execution = {{
                id: {JsonEncode(context.ExecutionId.ToString())},
                workflowId: {JsonEncode(context.WorkflowId.ToString())}
            }};
        ");

        // $vars — 全局变量
        if (context.Variables is { Count: > 0 })
        {
            var varsBuilder = new StringBuilder("var $vars = {};");
            foreach (var (key, value) in context.Variables)
            {
                varsBuilder.Append($"$vars[{JsonEncode(key)}] = {JsonEncode(value)};");
            }
            engine.Execute(varsBuilder.ToString());
        }
        else
        {
            engine.Execute("var $vars = {};");
        }
    }

    /// <summary>
    /// 将 V8 返回值序列化为 JSON 字符串。
    /// </summary>
    private static string SerializeResult(object? result)
    {
        if (result is null or Undefined)
            return "null";

        // 基本类型直接序列化
        if (result is string s)
            return s; // 字符串不再包装引号，因为模板替换需要原始值

        if (result is bool b)
            return b ? "true" : "false";

        if (result is int or long or float or double or decimal)
            return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? "null";

        // V8 对象/数组 — 通过 JSON.stringify 序列化
        if (result is ScriptObject)
        {
            // 已在 engine 内执行过 — 需要直接返回 ToString
            return result.ToString() ?? "null";
        }

        // 其他类型用 .NET JSON 序列化
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// JavaScript 风格的 truthy 判定。
    /// </summary>
    private static bool IsTruthy(object? result)
    {
        return result switch
        {
            null or Undefined => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0 && !double.IsNaN(d),
            float f => f != 0 && !float.IsNaN(f),
            string s => s.Length > 0,
            _ => true
        };
    }

    /// <summary>
    /// 将 .NET 字符串编码为 JSON 字符串字面量（含双引号包裹）。
    /// </summary>
    private static string JsonEncode(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// 释放资源（IExpressionEvaluator : IDisposable 要求）。
    /// 当前实现不持有长期 V8 引擎实例，因此无需释放。
    /// </summary>
    public void Dispose()
    {
        // 每次求值使用独立引擎实例，无需全局释放。
    }
}

/// <summary>
/// 表达式求值异常。
/// </summary>
public class ExpressionEvaluationException : Exception
{
    public string Expression { get; }

    public ExpressionEvaluationException(string expression, Exception innerException)
        : base($"表达式求值失败: {expression}", innerException)
    {
        Expression = expression;
    }
}
