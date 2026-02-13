using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows.DataFlow;

/// <summary>
/// NodeInputData 和 NodeOutputData 单元测试
/// </summary>
public class NodeDataTests
{
    [Fact]
    public void NodeInputData_FromSinglePort_AccessPort0()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"msg\":\"hello\"}").RootElement)
        };
        var portData = new PortDataVO(items);

        var input = NodeInputData.FromSinglePort(portData);

        input.GetPort(0).Should().NotBeNull();
        input.GetPort(0)!.Items.Should().HaveCount(1);
        input.GetPort(0)!.Items[0].Json.GetProperty("msg").GetString().Should().Be("hello");
    }

    [Fact]
    public void NodeInputData_FromPorts_AccessSpecificPort()
    {
        var port0 = new PortDataVO(new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"port\":0}").RootElement)
        });
        var port1 = new PortDataVO(new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"port\":1}").RootElement)
        });

        var input = NodeInputData.FromPorts([port0, port1]);

        input.GetPort(0)!.Items[0].Json.GetProperty("port").GetInt32().Should().Be(0);
        input.GetPort(1)!.Items[0].Json.GetProperty("port").GetInt32().Should().Be(1);
    }

    [Fact]
    public void NodeOutputData_FromPorts_MultiPort()
    {
        var port0 = new PortDataVO(new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"branch\":\"true\"}").RootElement)
        });
        PortDataVO? port1 = null; // No data on port 1

        var output = NodeOutputData.FromPorts([port0, port1]);

        output.GetPort(0).Should().NotBeNull();
        output.GetPort(1).Should().BeNull();
    }

    [Fact]
    public void NodeInputData_ToJsonString_MatchesExpectedFormat()
    {
        var source = new ItemSourceVO("agent-1", 0, 0);
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"message\":\"Hello\"}").RootElement, source)
        };
        var portData = new PortDataVO(items);
        var input = NodeInputData.FromSinglePort(portData);

        var json = input.ToJsonString();

        // Parse and verify structure
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("main", out var main).Should().BeTrue();
        main.ValueKind.Should().Be(JsonValueKind.Array);
        main.GetArrayLength().Should().Be(1); // 1 port

        var port0Items = main[0];
        port0Items.ValueKind.Should().Be(JsonValueKind.Array);
        port0Items.GetArrayLength().Should().Be(1); // 1 item

        var item = port0Items[0];
        item.GetProperty("json").GetProperty("message").GetString().Should().Be("Hello");
        item.GetProperty("source").GetProperty("nodeId").GetString().Should().Be("agent-1");
    }

    [Fact]
    public void NodeInputData_FromJsonString_RoundTrips()
    {
        var source = new ItemSourceVO("start", 0, 0);
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"query\":\"test\"}").RootElement, source)
        };
        var original = NodeInputData.FromSinglePort(new PortDataVO(items));

        var json = original.ToJsonString();
        var restored = NodeInputData.FromJsonString(json);

        restored.GetPort(0).Should().NotBeNull();
        restored.GetPort(0)!.Items.Should().HaveCount(1);
        restored.GetPort(0)!.Items[0].Json.GetProperty("query").GetString().Should().Be("test");
        restored.GetPort(0)!.Items[0].Source.Should().NotBeNull();
        restored.GetPort(0)!.Items[0].Source!.NodeId.Should().Be("start");
    }

    [Fact]
    public void NodeInputData_Empty_GetPort_ReturnsNull()
    {
        var input = NodeInputData.Empty;

        input.GetPort(0).Should().BeNull();
        input.GetPort(1).Should().BeNull();
    }

    [Fact]
    public void NodeOutputData_ToJsonString_RoundTrips()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"response\":\"World\"}").RootElement)
        };
        var original = NodeOutputData.FromSinglePort(new PortDataVO(items));

        var json = original.ToJsonString();
        var restored = NodeOutputData.FromJsonString(json);

        restored.GetPort(0).Should().NotBeNull();
        restored.GetPort(0)!.Items[0].Json.GetProperty("response").GetString().Should().Be("World");
    }

    [Fact]
    public void NodeInputData_NullSource_SerializesWithoutSource()
    {
        var items = new List<WorkflowItemVO>
        {
            new(JsonDocument.Parse("{\"data\":\"value\"}").RootElement) // No source
        };
        var input = NodeInputData.FromSinglePort(new PortDataVO(items));

        var json = input.ToJsonString();

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("main")[0][0];
        item.TryGetProperty("source", out _).Should().BeFalse();
    }
}
