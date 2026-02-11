using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class ToolFunctionFactoryTests
{
    private static readonly AuthConfigVO s_noAuth = new() { AuthType = AuthType.None };

    private readonly Mock<IToolRegistrationRepository> _toolRepoMock = new();
    private readonly Mock<IMcpToolItemRepository> _mcpRepoMock = new();
    private readonly Mock<IToolInvokerFactory> _invokerFactoryMock = new();
    private readonly Mock<ILogger<ToolFunctionFactory>> _loggerMock = new();

    private ToolFunctionFactory CreateFactory()
    {
        return new ToolFunctionFactory(
            _toolRepoMock.Object,
            _mcpRepoMock.Object,
            _invokerFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CreateFunctionsAsync_EmptyToolRefs_ReturnsEmptyList()
    {
        // Arrange
        var factory = CreateFactory();
        var toolRefs = Array.Empty<Guid>().ToList().AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<ToolRegistration>());
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<McpToolItem>());

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateFunctionsAsync_RestApiToolRef_ResolvesToAIFunctionWithCorrectProperties()
    {
        // Arrange
        var toolId = Guid.NewGuid();
        var inputSchema = """{"type":"object","properties":{"location":{"type":"string"}}}""";
        var tool = ToolRegistration.CreateRestApi(
            name: "check-weather",
            description: "Check the weather",
            endpoint: "https://api.weather.com/check",
            authConfig: s_noAuth,
            httpMethod: "POST");
        // Set Id via reflection (domain entity uses private setter)
        typeof(BaseEntity).GetProperty("Id")!.SetValue(tool, toolId);
        tool.SetToolSchema(new ToolSchemaVO { InputSchema = inputSchema });

        var toolRefs = new List<Guid> { toolId }.AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { tool });
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<McpToolItem>());

        var invokerMock = new Mock<IToolInvoker>();
        _invokerFactoryMock.Setup(f => f.GetInvoker(ToolType.RestApi)).Returns(invokerMock.Object);

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().HaveCount(1);
        var fn = result[0];
        fn.Name.Should().Be("check-weather");
        fn.Description.Should().Be("Check the weather");
        fn.JsonSchema.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        fn.JsonSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public async Task CreateFunctionsAsync_McpToolRef_ResolvesToAIFunctionWithCorrectProperties()
    {
        // Arrange
        var mcpItemId = Guid.NewGuid();
        var parentToolId = Guid.NewGuid();
        var schemaJson = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""").RootElement;

        var parentTool = ToolRegistration.CreateMcpServer(
            name: "Analytics MCP Server",
            description: "MCP analytics",
            endpoint: "https://mcp.example.com",
            transportType: TransportType.Sse);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(parentTool, parentToolId);

        var mcpItem = McpToolItem.Create(
            toolRegistrationId: parentToolId,
            toolName: "query_database",
            description: "Run a SQL query",
            inputSchema: schemaJson);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(mcpItem, mcpItemId);
        // Set the navigation property via reflection
        typeof(McpToolItem).GetProperty("ToolRegistration")!.SetValue(mcpItem, parentTool);

        var toolRefs = new List<Guid> { mcpItemId }.AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<ToolRegistration>());
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpItem });

        var invokerMock = new Mock<IToolInvoker>();
        _invokerFactoryMock.Setup(f => f.GetInvoker(ToolType.McpServer)).Returns(invokerMock.Object);

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().HaveCount(1);
        var fn = result[0];
        fn.Name.Should().Be("query_database");
        fn.Description.Should().Be("Run a SQL query");
        fn.JsonSchema.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        fn.JsonSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public async Task CreateFunctionsAsync_MixedRefs_ReturnsCombinedList()
    {
        // Arrange
        var restToolId = Guid.NewGuid();
        var mcpItemId = Guid.NewGuid();
        var parentToolId = Guid.NewGuid();

        var restTool = ToolRegistration.CreateRestApi("rest-tool", "REST tool", "https://api.example.com", s_noAuth, "GET");
        typeof(BaseEntity).GetProperty("Id")!.SetValue(restTool, restToolId);
        restTool.SetToolSchema(new ToolSchemaVO { InputSchema = """{"type":"object"}""" });

        var parentTool = ToolRegistration.CreateMcpServer("mcp-server", "MCP", "https://mcp.example.com", TransportType.Sse);
        typeof(BaseEntity).GetProperty("Id")!.SetValue(parentTool, parentToolId);

        var mcpItem = McpToolItem.Create(parentToolId, "mcp-tool", "MCP tool");
        typeof(BaseEntity).GetProperty("Id")!.SetValue(mcpItem, mcpItemId);
        typeof(McpToolItem).GetProperty("ToolRegistration")!.SetValue(mcpItem, parentTool);

        var toolRefs = new List<Guid> { restToolId, mcpItemId }.AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { restTool });
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpItem });

        var invokerMock = new Mock<IToolInvoker>();
        _invokerFactoryMock.Setup(f => f.GetInvoker(It.IsAny<ToolType>())).Returns(invokerMock.Object);

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().HaveCount(2);
        result.Select(f => f.Name).Should().Contain("rest-tool");
        result.Select(f => f.Name).Should().Contain("mcp-tool");
    }

    [Fact]
    public async Task CreateFunctionsAsync_DeletedUnknownRefIds_SkippedNoException()
    {
        // Arrange
        var unknownId = Guid.NewGuid();
        var toolRefs = new List<Guid> { unknownId }.AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<ToolRegistration>());
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<McpToolItem>());

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().BeEmpty();
        // No exception thrown — deleted/unknown refs are silently skipped
    }

    [Fact]
    public async Task CreateFunctionsAsync_NullToolSchemaInputSchema_ProducesAIFunctionWithNullJsonSchema()
    {
        // Arrange
        var toolId = Guid.NewGuid();
        var tool = ToolRegistration.CreateRestApi("no-schema-tool", "Tool without schema", "https://api.example.com", s_noAuth, "GET");
        typeof(BaseEntity).GetProperty("Id")!.SetValue(tool, toolId);
        // Do NOT call SetToolSchema — ToolSchema will be null or InputSchema will be null

        var toolRefs = new List<Guid> { toolId }.AsReadOnly();

        _toolRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { tool });
        _mcpRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<McpToolItem>());

        var invokerMock = new Mock<IToolInvoker>();
        _invokerFactoryMock.Setup(f => f.GetInvoker(ToolType.RestApi)).Returns(invokerMock.Object);

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateFunctionsAsync(toolRefs);

        // Assert
        result.Should().HaveCount(1);
        result[0].JsonSchema.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
