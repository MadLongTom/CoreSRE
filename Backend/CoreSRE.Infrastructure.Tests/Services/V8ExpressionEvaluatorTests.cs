using CoreSRE.Application.Interfaces;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class V8ExpressionEvaluatorTests : IDisposable
{
    private readonly V8ExpressionEvaluator _evaluator;

    public V8ExpressionEvaluatorTests()
    {
        var logger = new Mock<ILogger<V8ExpressionEvaluator>>();
        _evaluator = new V8ExpressionEvaluator(logger.Object);
    }

    public void Dispose()
    {
        _evaluator.Dispose();
    }

    private static ExpressionContext EmptyContext() => new()
    {
        ExecutionId = Guid.NewGuid(),
        WorkflowId = Guid.NewGuid(),
    };

    private static ExpressionContext ContextWithInput(string json) => new()
    {
        CurrentInput = json,
        ExecutionId = Guid.NewGuid(),
        WorkflowId = Guid.NewGuid(),
    };

    private static ExpressionContext ContextWithNodes(Dictionary<string, string?> nodeOutputs, string? input = null) => new()
    {
        CurrentInput = input,
        NodeOutputs = nodeOutputs.ToDictionary(kv => kv.Key, kv => new List<string?> { kv.Value }),
        ExecutionId = Guid.NewGuid(),
        WorkflowId = Guid.NewGuid(),
    };

    // ───────────────────────────────────────────────────────────
    // Evaluate — 模板表达式
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NoExpressions_ReturnsOriginalString()
    {
        var result = _evaluator.Evaluate("Hello World", EmptyContext());
        result.Should().Be("Hello World");
    }

    [Fact]
    public void Evaluate_SimpleArithmetic()
    {
        var result = _evaluator.Evaluate("{{ 1 + 2 }}", EmptyContext());
        result.Should().Be("3");
    }

    [Fact]
    public void Evaluate_StringConcatenation()
    {
        var result = _evaluator.Evaluate("{{ 'hello' + ' ' + 'world' }}", EmptyContext());
        result.Should().Be("hello world");
    }

    [Fact]
    public void Evaluate_MixedTextAndExpression()
    {
        var result = _evaluator.Evaluate("The answer is {{ 6 * 7 }} today.", EmptyContext());
        result.Should().Be("The answer is 42 today.");
    }

    [Fact]
    public void Evaluate_MultipleExpressions()
    {
        var result = _evaluator.Evaluate("{{ 1 + 1 }} and {{ 2 + 2 }}", EmptyContext());
        result.Should().Be("2 and 4");
    }

    // ───────────────────────────────────────────────────────────
    // $input / $json — 当前节点输入
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_InputJsonAccess()
    {
        var ctx = ContextWithInput("""{"severity":"high","count":42}""");
        var result = _evaluator.Evaluate("{{ $input.severity }}", ctx);
        result.Should().Be("high");
    }

    [Fact]
    public void Evaluate_JsonAlias()
    {
        var ctx = ContextWithInput("""{"name":"Alice"}""");
        var result = _evaluator.Evaluate("{{ $json.name }}", ctx);
        result.Should().Be("Alice");
    }

    [Fact]
    public void Evaluate_InputNestedAccess()
    {
        var ctx = ContextWithInput("""{"data":{"items":[1,2,3]}}""");
        var result = _evaluator.Evaluate("{{ $input.data.items.length }}", ctx);
        result.Should().Be("3");
    }

    [Fact]
    public void Evaluate_InputArrayIndex()
    {
        var ctx = ContextWithInput("""{"tags":["a","b","c"]}""");
        var result = _evaluator.Evaluate("{{ $input.tags[1] }}", ctx);
        result.Should().Be("b");
    }

    // ───────────────────────────────────────────────────────────
    // $node — 上游节点输出引用
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NodeOutputAccess()
    {
        var nodes = new Dictionary<string, string?>
        {
            ["AgentA"] = """{"analysis":"Critical issue found","score":9.5}"""
        };
        var ctx = ContextWithNodes(nodes);

        var result = _evaluator.Evaluate("{{ $node[\"AgentA\"].json.analysis }}", ctx);
        result.Should().Be("Critical issue found");
    }

    [Fact]
    public void Evaluate_NodeOutputNumeric()
    {
        var nodes = new Dictionary<string, string?>
        {
            ["scorer"] = """{"score":95}"""
        };
        var ctx = ContextWithNodes(nodes);

        var result = _evaluator.Evaluate("{{ $node[\"scorer\"].json.score }}", ctx);
        result.Should().Be("95");
    }

    [Fact]
    public void Evaluate_NodeOutputText()
    {
        var nodes = new Dictionary<string, string?>
        {
            ["node1"] = """{"msg":"hello"}"""
        };
        var ctx = ContextWithNodes(nodes);

        var result = _evaluator.Evaluate("{{ $node[\"node1\"].text }}", ctx);
        // text is the raw JSON string
        result.Should().Contain("msg");
    }

    [Fact]
    public void Evaluate_MultipleNodeReferences()
    {
        var nodes = new Dictionary<string, string?>
        {
            ["A"] = """{"val":10}""",
            ["B"] = """{"val":20}"""
        };
        var ctx = ContextWithNodes(nodes);

        var result = _evaluator.Evaluate("{{ $node[\"A\"].json.val + $node[\"B\"].json.val }}", ctx);
        result.Should().Be("30");
    }

    // ───────────────────────────────────────────────────────────
    // $execution — 执行元数据
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ExecutionId()
    {
        var executionId = Guid.NewGuid();
        var ctx = new ExpressionContext
        {
            ExecutionId = executionId,
            WorkflowId = Guid.NewGuid(),
        };

        var result = _evaluator.Evaluate("{{ $execution.id }}", ctx);
        result.Should().Be(executionId.ToString());
    }

    // ───────────────────────────────────────────────────────────
    // $vars — 全局变量
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_GlobalVariables()
    {
        var ctx = new ExpressionContext
        {
            ExecutionId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            Variables = new Dictionary<string, string> { ["env"] = "production" }
        };

        var result = _evaluator.Evaluate("{{ $vars.env }}", ctx);
        result.Should().Be("production");
    }

    // ───────────────────────────────────────────────────────────
    // $get — 安全取值辅助函数
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_SafeGet_ExistingPath()
    {
        var ctx = ContextWithInput("""{"a":{"b":{"c":42}}}""");
        var result = _evaluator.Evaluate("{{ $get($input, 'a.b.c') }}", ctx);
        result.Should().Be("42");
    }

    [Fact]
    public void Evaluate_SafeGet_MissingPathWithDefault()
    {
        var ctx = ContextWithInput("""{"a":1}""");
        var result = _evaluator.Evaluate("{{ $get($input, 'x.y.z', 'fallback') }}", ctx);
        result.Should().Be("fallback");
    }

    // ───────────────────────────────────────────────────────────
    // EvaluateExpression — 单个表达式
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateExpression_ReturnsJsonForObject()
    {
        var ctx = ContextWithInput("""{"a":1}""");
        var result = _evaluator.EvaluateExpression("$input", ctx);
        result.Should().Contain("\"a\"");
    }

    [Fact]
    public void EvaluateExpression_ReturnsBooleanAsString()
    {
        var result = _evaluator.EvaluateExpression("1 > 0", EmptyContext());
        result.Should().Be("true");
    }

    // ───────────────────────────────────────────────────────────
    // EvaluateCondition — 条件求值
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateCondition_SimpleTrue()
    {
        var result = _evaluator.EvaluateCondition("1 === 1", EmptyContext());
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_SimpleFalse()
    {
        var result = _evaluator.EvaluateCondition("1 === 2", EmptyContext());
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_InputFieldEquality()
    {
        var ctx = ContextWithInput("""{"severity":"high"}""");
        var result = _evaluator.EvaluateCondition("$input.severity === 'high'", ctx);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_InputFieldInequality()
    {
        var ctx = ContextWithInput("""{"severity":"low"}""");
        var result = _evaluator.EvaluateCondition("$input.severity === 'high'", ctx);
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_NumericComparison()
    {
        var ctx = ContextWithInput("""{"score":85}""");
        var result = _evaluator.EvaluateCondition("$input.score >= 80", ctx);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_ComplexLogic()
    {
        var ctx = ContextWithInput("""{"severity":"high","count":5}""");
        var result = _evaluator.EvaluateCondition(
            "$input.severity === 'high' && $input.count > 3", ctx);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_NodeOutputCondition()
    {
        var nodes = new Dictionary<string, string?>
        {
            ["analyzer"] = """{"risk":"critical"}"""
        };
        var ctx = ContextWithNodes(nodes);

        var result = _evaluator.EvaluateCondition(
            "$node[\"analyzer\"].json.risk === 'critical'", ctx);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_StringIncludes()
    {
        var ctx = ContextWithInput("""{"message":"Error: disk full"}""");
        var result = _evaluator.EvaluateCondition(
            "$input.message.includes('Error')", ctx);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_ArrayLength()
    {
        var ctx = ContextWithInput("""{"items":[1,2,3,4,5]}""");
        var result = _evaluator.EvaluateCondition("$input.items.length > 3", ctx);
        result.Should().BeTrue();
    }

    // ───────────────────────────────────────────────────────────
    // 错误处理
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateExpression_InvalidSyntax_Throws()
    {
        var act = () => _evaluator.EvaluateExpression("if (", EmptyContext());
        act.Should().Throw<ExpressionEvaluationException>();
    }

    [Fact]
    public void EvaluateCondition_InvalidSyntax_Throws()
    {
        var act = () => _evaluator.EvaluateCondition("!@#$%", EmptyContext());
        act.Should().Throw<ExpressionEvaluationException>();
    }

    [Fact]
    public void Evaluate_NullInput_DefaultsToEmptyObject()
    {
        var ctx = EmptyContext();
        // $input should be {} when CurrentInput is null
        var result = _evaluator.EvaluateExpression("typeof $input", ctx);
        result.Should().Be("object");
    }

    // ───────────────────────────────────────────────────────────
    // 边界情况
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_EmptyTemplate_ReturnsEmpty()
    {
        var result = _evaluator.Evaluate("", EmptyContext());
        result.Should().Be("");
    }

    [Fact]
    public void Evaluate_UndefinedProperty_ReturnsNull()
    {
        var ctx = ContextWithInput("""{"a":1}""");
        var result = _evaluator.EvaluateExpression("$input.nonexistent", ctx);
        result.Should().Be("null");
    }

    [Fact]
    public void Evaluate_TernaryOperator()
    {
        var ctx = ContextWithInput("""{"level":3}""");
        var result = _evaluator.Evaluate(
            "{{ $input.level > 2 ? 'high' : 'low' }}", ctx);
        result.Should().Be("high");
    }

    [Fact]
    public void Evaluate_TemplateLiteral_InExpression()
    {
        var ctx = ContextWithInput("""{"name":"Bob"}""");
        var result = _evaluator.Evaluate(
            "{{ `Hello ${$input.name}!` }}", ctx);
        result.Should().Be("Hello Bob!");
    }

    [Fact]
    public void Evaluate_JsonStringify()
    {
        var ctx = ContextWithInput("""{"a":1,"b":2}""");
        var result = _evaluator.Evaluate("{{ $toJson($input) }}", ctx);
        result.Should().Contain("\"a\"");
        result.Should().Contain("\"b\"");
    }
}
