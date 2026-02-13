using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows.ExecutionStack;

/// <summary>
/// T021: NodeExecutionTask 单元测试 — 验证构造和默认值。
/// </summary>
public class NodeExecutionTaskTests
{
    [Fact]
    public void Construct_WithNodeAndInputData()
    {
        var node = new WorkflowNodeVO { NodeId = "test", NodeType = WorkflowNodeType.Agent, DisplayName = "Test" };
        var inputData = NodeInputData.FromSinglePort(new PortDataVO([
            new WorkflowItemVO(System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement)
        ]));

        var task = new NodeExecutionTask { Node = node, InputData = inputData };

        task.Node.NodeId.Should().Be("test");
        task.InputData.Should().BeSameAs(inputData);
        task.RunIndex.Should().Be(0);
        task.TriggerSource.Should().BeNull();
    }

    [Fact]
    public void RunIndex_DefaultsToZero()
    {
        var node = new WorkflowNodeVO { NodeId = "test", NodeType = WorkflowNodeType.Agent, DisplayName = "Test" };
        var task = new NodeExecutionTask { Node = node, InputData = NodeInputData.Empty };

        task.RunIndex.Should().Be(0);
    }

    [Fact]
    public void Construct_WithExplicitRunIndex()
    {
        var node = new WorkflowNodeVO { NodeId = "test", NodeType = WorkflowNodeType.Agent, DisplayName = "Test" };
        var task = new NodeExecutionTask { Node = node, InputData = NodeInputData.Empty, RunIndex = 3 };

        task.RunIndex.Should().Be(3);
    }
}
