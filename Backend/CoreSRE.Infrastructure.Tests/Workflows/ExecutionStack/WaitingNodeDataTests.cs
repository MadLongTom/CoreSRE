using System.Text.Json;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows.ExecutionStack;

/// <summary>
/// T022: WaitingNodeData 单元测试 — 验证多输入端口等待逻辑。
/// </summary>
public class WaitingNodeDataTests
{
    [Fact]
    public void Create_WithExpectedPortCount()
    {
        var waiting = new WaitingNodeData(3);

        waiting.ExpectedPortCount.Should().Be(3);
        waiting.IsComplete.Should().BeFalse();
        waiting.ReceivedPorts.Should().BeEmpty();
    }

    [Fact]
    public void ReceivePort_OnePort_NotComplete()
    {
        var waiting = new WaitingNodeData(2);
        var portData = new PortDataVO([
            new WorkflowItemVO(JsonDocument.Parse("{\"a\":1}").RootElement)
        ]);

        waiting.ReceivePort(0, portData);

        waiting.IsComplete.Should().BeFalse();
        waiting.ReceivedPorts.Should().HaveCount(1);
    }

    [Fact]
    public void ReceivePort_AllPorts_IsComplete()
    {
        var waiting = new WaitingNodeData(2);
        var portData0 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"a\":1}").RootElement)]);
        var portData1 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"b\":2}").RootElement)]);

        waiting.ReceivePort(0, portData0);
        waiting.ReceivePort(1, portData1);

        waiting.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void BuildInputData_CombinesAllPorts()
    {
        var waiting = new WaitingNodeData(2);
        var portData0 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"from\":\"A\"}").RootElement)]);
        var portData1 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"from\":\"B\"}").RootElement)]);

        waiting.ReceivePort(0, portData0);
        waiting.ReceivePort(1, portData1);

        var inputData = waiting.BuildInputData();

        inputData.GetPort(0).Should().NotBeNull();
        inputData.GetPort(0)!.Items.Should().HaveCount(1);
        inputData.GetPort(0)!.Items[0].Json.GetProperty("from").GetString().Should().Be("A");

        inputData.GetPort(1).Should().NotBeNull();
        inputData.GetPort(1)!.Items.Should().HaveCount(1);
        inputData.GetPort(1)!.Items[0].Json.GetProperty("from").GetString().Should().Be("B");
    }

    [Fact]
    public void DuplicatePortData_OverwritesPrevious()
    {
        var waiting = new WaitingNodeData(1);
        var portData1 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"v\":1}").RootElement)]);
        var portData2 = new PortDataVO([new WorkflowItemVO(JsonDocument.Parse("{\"v\":2}").RootElement)]);

        waiting.ReceivePort(0, portData1);
        waiting.ReceivePort(0, portData2);

        waiting.ReceivedPorts.Should().HaveCount(1);
        var inputData = waiting.BuildInputData();
        inputData.GetPort(0)!.Items[0].Json.GetProperty("v").GetInt32().Should().Be(2);
    }
}
