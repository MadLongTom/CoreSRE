using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Workflows;

/// <summary>
/// WorkflowDefinition 聚合根单元测试 — 工厂方法、状态守卫、Publish 转换
/// </summary>
public class WorkflowDefinitionTests
{
    private static WorkflowGraphVO CreateValidGraph() => new()
    {
        Nodes =
        [
            new WorkflowNodeVO { NodeId = "n1", NodeType = WorkflowNodeType.Agent, ReferenceId = Guid.NewGuid(), DisplayName = "Agent 1" },
            new WorkflowNodeVO { NodeId = "n2", NodeType = WorkflowNodeType.Tool, ReferenceId = Guid.NewGuid(), DisplayName = "Tool 1" }
        ],
        Edges =
        [
            new WorkflowEdgeVO { EdgeId = "e1", SourceNodeId = "n1", TargetNodeId = "n2", EdgeType = WorkflowEdgeType.Normal }
        ]
    };

    // ========== Create Factory ==========

    [Fact]
    public void Create_ValidInput_ReturnsDraftWorkflow()
    {
        var graph = CreateValidGraph();

        var workflow = WorkflowDefinition.Create("Test Workflow", "A test description", graph);

        workflow.Name.Should().Be("Test Workflow");
        workflow.Description.Should().Be("A test description");
        workflow.Status.Should().Be(WorkflowStatus.Draft);
        workflow.Graph.Should().BeSameAs(graph);
        workflow.Id.Should().NotBeEmpty();
        workflow.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_NullDescription_Accepted()
    {
        var graph = CreateValidGraph();

        var workflow = WorkflowDefinition.Create("Test", null, graph);

        workflow.Description.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var graph = CreateValidGraph();

        var workflow = WorkflowDefinition.Create("  Name  ", "  Desc  ", graph);

        workflow.Name.Should().Be("Name");
        workflow.Description.Should().Be("Desc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrNullName_Throws(string? name)
    {
        var graph = CreateValidGraph();

        var act = () => WorkflowDefinition.Create(name!, null, graph);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        var graph = CreateValidGraph();
        var longName = new string('A', 201);

        var act = () => WorkflowDefinition.Create(longName, null, graph);

        act.Should().Throw<ArgumentException>().WithMessage("*200*");
    }

    [Fact]
    public void Create_DescriptionTooLong_Throws()
    {
        var graph = CreateValidGraph();
        var longDesc = new string('A', 2001);

        var act = () => WorkflowDefinition.Create("Test", longDesc, graph);

        act.Should().Throw<ArgumentException>().WithMessage("*2000*");
    }

    [Fact]
    public void Create_NullGraph_Throws()
    {
        var act = () => WorkflowDefinition.Create("Test", null, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== Update ==========

    [Fact]
    public void Update_DraftStatus_UpdatesFields()
    {
        var workflow = WorkflowDefinition.Create("Original", "Original desc", CreateValidGraph());
        var newGraph = new WorkflowGraphVO
        {
            Nodes = [new WorkflowNodeVO { NodeId = "x1", NodeType = WorkflowNodeType.Condition, DisplayName = "X1" }],
            Edges = []
        };

        workflow.Update("Updated", "Updated desc", newGraph);

        workflow.Name.Should().Be("Updated");
        workflow.Description.Should().Be("Updated desc");
        workflow.Graph.Should().BeSameAs(newGraph);
    }

    [Fact]
    public void Update_PublishedStatus_Throws()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());
        workflow.Publish();

        var act = () => workflow.Update("New", null, CreateValidGraph());

        act.Should().Throw<InvalidOperationException>().WithMessage("*已发布*编辑*");
    }

    [Fact]
    public void Update_EmptyName_Throws()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());

        var act = () => workflow.Update("", null, CreateValidGraph());

        act.Should().Throw<ArgumentException>();
    }

    // ========== Publish ==========

    [Fact]
    public void Publish_DraftStatus_TransitionsToPublished()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());

        workflow.Publish();

        workflow.Status.Should().Be(WorkflowStatus.Published);
    }

    [Fact]
    public void Publish_AlreadyPublished_Throws()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());
        workflow.Publish();

        var act = () => workflow.Publish();

        act.Should().Throw<InvalidOperationException>().WithMessage("*已发布*发布*");
    }

    // ========== GuardCanDelete ==========

    [Fact]
    public void GuardCanDelete_DraftStatus_NoException()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());

        var act = () => workflow.GuardCanDelete();

        act.Should().NotThrow();
    }

    [Fact]
    public void GuardCanDelete_PublishedStatus_Throws()
    {
        var workflow = WorkflowDefinition.Create("Test", null, CreateValidGraph());
        workflow.Publish();

        var act = () => workflow.GuardCanDelete();

        act.Should().Throw<InvalidOperationException>().WithMessage("*已发布*删除*");
    }
}
