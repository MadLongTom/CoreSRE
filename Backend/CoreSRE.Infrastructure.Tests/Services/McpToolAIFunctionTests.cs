using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Entities;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class McpToolAIFunctionTests
{
    private static (McpToolItem McpItem, ToolRegistration ParentTool) CreateMcpToolWithParent(
        string toolName,
        string? description = null,
        JsonElement? inputSchema = null)
    {
        var parentToolId = Guid.NewGuid();
        var parentTool = ToolRegistration.CreateMcpServer(
            name: "Test MCP Server",
            description: "A test MCP server",
            endpoint: "https://mcp.example.com",
            transportType: TransportType.Sse);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(parentTool, parentToolId);

        var mcpItem = McpToolItem.Create(
            toolRegistrationId: parentToolId,
            toolName: toolName,
            description: description,
            inputSchema: inputSchema);
        typeof(McpToolItem).GetProperty("ToolRegistration")!.SetValue(mcpItem, parentTool);

        return (mcpItem, parentTool);
    }

    [Fact]
    public void Name_ReturnsMcpToolItemToolName()
    {
        // Arrange
        var (mcpItem, parentTool) = CreateMcpToolWithParent("query_metrics");
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Assert
        fn.Name.Should().Be("query_metrics");
    }

    [Fact]
    public void Description_ReturnsMcpToolItemDescription()
    {
        // Arrange
        var (mcpItem, parentTool) = CreateMcpToolWithParent("query_metrics", description: "Query system metrics");
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Assert
        fn.Description.Should().Be("Query system metrics");
    }

    [Fact]
    public void JsonSchema_ReturnsMcpToolItemInputSchemaDirectly()
    {
        // Arrange
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""").RootElement;
        var (mcpItem, parentTool) = CreateMcpToolWithParent("query_db", inputSchema: schema);
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Assert
        fn.JsonSchema.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        fn.JsonSchema.GetProperty("type").GetString().Should().Be("object");
        fn.JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void JsonSchema_NullInputSchema_ReturnsNull()
    {
        // Arrange
        var (mcpItem, parentTool) = CreateMcpToolWithParent("no_schema_tool");
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Assert
        fn.JsonSchema.ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task InvokeCoreAsync_DelegatesToToolInvokerWithParentToolAndMcpToolName()
    {
        // Arrange
        var schema = JsonDocument.Parse("""{"type":"object"}""").RootElement;
        var (mcpItem, parentTool) = CreateMcpToolWithParent("run_query", "Run a query", schema);
        var invokerMock = new Mock<IToolInvoker>();

        var resultData = JsonDocument.Parse("""{"rows":42}""").RootElement;
        invokerMock.Setup(i => i.InvokeAsync(
                parentTool,
                "run_query",
                It.IsAny<IDictionary<string, object?>>(),
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolInvocationResultDto
            {
                Success = true,
                Data = resultData,
                DurationMs = 150,
                ToolRegistrationId = parentTool.Id,
                InvokedAt = DateTime.UtcNow
            });

        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Act
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["sql"] = "SELECT COUNT(*) FROM users" });
        var result = await fn.InvokeAsync(args);

        // Assert
        result.Should().NotBeNull();
        result!.ToString().Should().Contain("42");

        // Verify it passed parentTool and mcpToolName correctly
        invokerMock.Verify(i => i.InvokeAsync(
            parentTool,
            "run_query",
            It.Is<IDictionary<string, object?>>(d => d.ContainsKey("sql")),
            null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeCoreAsync_PassesCorrectParametersDictionary()
    {
        // Arrange
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"number"}}}""").RootElement;
        var (mcpItem, parentTool) = CreateMcpToolWithParent("multi_param_tool", inputSchema: schema);
        var invokerMock = new Mock<IToolInvoker>();

        IDictionary<string, object?>? capturedParams = null;
        invokerMock.Setup(i => i.InvokeAsync(
                It.IsAny<ToolRegistration>(),
                It.IsAny<string?>(),
                It.IsAny<IDictionary<string, object?>>(),
                null, null,
                It.IsAny<CancellationToken>()))
            .Callback<ToolRegistration, string?, IDictionary<string, object?>, IDictionary<string, string>?, IDictionary<string, string>?, CancellationToken>(
                (_, _, p, _, _, _) => capturedParams = p)
            .ReturnsAsync(new ToolInvocationResultDto
            {
                Success = true,
                Data = JsonDocument.Parse("{}").RootElement,
                DurationMs = 10,
                ToolRegistrationId = parentTool.Id,
                InvokedAt = DateTime.UtcNow
            });

        var fn = new McpToolAIFunction(mcpItem, parentTool, invokerMock.Object);

        // Act
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["a"] = "hello", ["b"] = 42 });
        await fn.InvokeAsync(args);

        // Assert
        capturedParams.Should().NotBeNull();
        capturedParams!.Should().ContainKey("a").WhoseValue.Should().Be("hello");
        capturedParams.Should().ContainKey("b").WhoseValue.Should().Be(42);
    }
}
