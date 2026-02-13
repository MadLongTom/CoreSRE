using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows.DataFlow;

/// <summary>
/// PortDataVO 单元测试
/// </summary>
public class PortDataTests
{
    [Fact]
    public void PortDataVO_CreateWithItems_ContainsAllItems()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"a\":1}").RootElement),
            new(JsonDocument.Parse("{\"b\":2}").RootElement)
        };

        var port = new PortDataVO(items);

        port.Items.Should().HaveCount(2);
        port.Items[0].Json.GetProperty("a").GetInt32().Should().Be(1);
        port.Items[1].Json.GetProperty("b").GetInt32().Should().Be(2);
    }

    [Fact]
    public void PortDataVO_Empty_HasNoItems()
    {
        var port = PortDataVO.Empty;

        port.Items.Should().BeEmpty();
    }

    [Fact]
    public void PortDataVO_AccessItemsByIndex_Works()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"first\":true}").RootElement),
            new(JsonDocument.Parse("{\"second\":false}").RootElement),
            new(JsonDocument.Parse("{\"third\":null}").RootElement)
        };

        var port = new PortDataVO(items);

        port.Items[0].Json.GetProperty("first").GetBoolean().Should().BeTrue();
        port.Items[1].Json.GetProperty("second").GetBoolean().Should().BeFalse();
        port.Items[2].Json.GetProperty("third").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void PortDataVO_Items_IsReadOnly()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"x\":1}").RootElement)
        };

        var port = new PortDataVO(items);

        // IReadOnlyList doesn't expose Add/Remove — compile-time guarantee
        port.Items.Should().BeAssignableTo<IReadOnlyList<WorkflowItemVO>>();
    }
}
