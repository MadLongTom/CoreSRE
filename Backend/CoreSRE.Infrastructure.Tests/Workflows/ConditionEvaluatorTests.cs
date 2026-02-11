using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

public class ConditionEvaluatorTests
{
    private readonly ConditionEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_SimpleEqualityMatch_ReturnsTrue()
    {
        var result = _evaluator.Evaluate(
            "$.severity == \"high\"",
            "{\"severity\":\"high\"}");

        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NumericMatch_ReturnsTrue()
    {
        var result = _evaluator.Evaluate(
            "$.count == \"5\"",
            "{\"count\":5}");

        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsFalse()
    {
        var result = _evaluator.Evaluate(
            "$.severity == \"high\"",
            "{\"severity\":\"low\"}");

        result.Should().BeFalse();
    }

    [Fact]
    public void TryEvaluate_MalformedExpression_ReturnsFalse()
    {
        var success = _evaluator.TryEvaluate(
            "not a valid expression",
            "{\"severity\":\"high\"}",
            out var result);

        success.Should().BeFalse();
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NestedJsonPath_ReturnsTrue()
    {
        var result = _evaluator.Evaluate(
            "$.data.status == \"ok\"",
            "{\"data\":{\"status\":\"ok\"}}");

        result.Should().BeTrue();
    }
}
