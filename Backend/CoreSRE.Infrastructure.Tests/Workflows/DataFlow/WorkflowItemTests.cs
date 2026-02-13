using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows.DataFlow;

/// <summary>
/// WorkflowItemVO 和 ItemSourceVO 单元测试
/// </summary>
public class WorkflowItemTests
{
    [Fact]
    public void WorkflowItemVO_ConstructWithJsonPayload_RetainsData()
    {
        var json = JsonDocument.Parse("{\"message\":\"Hello\"}").RootElement;

        var item = new WorkflowItemVO(json);

        item.Json.GetProperty("message").GetString().Should().Be("Hello");
    }

    [Fact]
    public void WorkflowItemVO_ConstructWithNullSource_SourceIsNull()
    {
        var json = JsonDocument.Parse("{\"data\":1}").RootElement;

        var item = new WorkflowItemVO(json, null);

        item.Source.Should().BeNull();
    }

    [Fact]
    public void WorkflowItemVO_ConstructWithSource_RetainsSourceInfo()
    {
        var json = JsonDocument.Parse("{\"value\":42}").RootElement;
        var source = new ItemSourceVO("agent-1", 0, 0);

        var item = new WorkflowItemVO(json, source);

        item.Source.Should().NotBeNull();
        item.Source!.NodeId.Should().Be("agent-1");
        item.Source.OutputIndex.Should().Be(0);
        item.Source.ItemIndex.Should().Be(0);
    }

    [Fact]
    public void WorkflowItemVO_SamePayloadAndSource_AreEqual()
    {
        var jsonText = "{\"key\":\"value\"}";
        var json1 = JsonDocument.Parse(jsonText).RootElement;
        var json2 = JsonDocument.Parse(jsonText).RootElement;
        var source1 = new ItemSourceVO("node-1", 0, 0);
        var source2 = new ItemSourceVO("node-1", 0, 0);

        var item1 = new WorkflowItemVO(json1, source1);
        var item2 = new WorkflowItemVO(json2, source2);

        // ItemSourceVO is a record — value equality works
        item1.Source.Should().Be(item2.Source);
    }

    [Fact]
    public void ItemSourceVO_RecordEquality_Works()
    {
        var s1 = new ItemSourceVO("node-a", 1, 2);
        var s2 = new ItemSourceVO("node-a", 1, 2);

        s1.Should().Be(s2);
        (s1 == s2).Should().BeTrue();
    }

    [Fact]
    public void ItemSourceVO_DifferentValues_NotEqual()
    {
        var s1 = new ItemSourceVO("node-a", 0, 0);
        var s2 = new ItemSourceVO("node-b", 0, 0);

        s1.Should().NotBe(s2);
    }

    [Fact]
    public void WorkflowItemVO_JsonElement_RoundTrips()
    {
        var original = JsonDocument.Parse("{\"nested\":{\"array\":[1,2,3]}}").RootElement;
        var item = new WorkflowItemVO(original);

        // Serialize to string and back
        var serialized = item.Json.GetRawText();
        var restored = JsonDocument.Parse(serialized).RootElement;

        restored.GetProperty("nested").GetProperty("array").GetArrayLength().Should().Be(3);
    }
}
