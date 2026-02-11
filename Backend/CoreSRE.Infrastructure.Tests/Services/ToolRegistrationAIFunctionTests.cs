using System.Text.Json;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class ToolRegistrationAIFunctionTests
{
    private static ToolRegistration CreateToolWithSchema(string name, string? description, string? inputSchema)
    {
        var tool = ToolRegistration.CreateRestApi(
            name: name,
            description: description,
            endpoint: "https://api.example.com",
            authConfig: new AuthConfigVO { AuthType = AuthType.None },
            httpMethod: "POST");
        if (inputSchema is not null)
        {
            tool.SetToolSchema(new ToolSchemaVO { InputSchema = inputSchema });
        }
        return tool;
    }

    [Fact]
    public void Name_ReturnsToolRegistrationName()
    {
        // Arrange
        var tool = CreateToolWithSchema("get-weather", "Get weather info", """{"type":"object"}""");
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Assert
        fn.Name.Should().Be("get-weather");
    }

    [Fact]
    public void Description_ReturnsToolRegistrationDescription()
    {
        // Arrange
        var tool = CreateToolWithSchema("get-weather", "Get weather for a location", """{"type":"object"}""");
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Assert
        fn.Description.Should().Be("Get weather for a location");
    }

    [Fact]
    public void JsonSchema_ParsesToolSchemaInputSchemaStringToJsonElement()
    {
        // Arrange
        var inputSchema = """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""";
        var tool = CreateToolWithSchema("get-weather", "Weather", inputSchema);
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Assert
        fn.JsonSchema.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var schema = fn.JsonSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").GetProperty("city").GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("required").EnumerateArray().First().GetString().Should().Be("city");
    }

    [Fact]
    public void JsonSchema_NullInputSchema_ReturnsNull()
    {
        // Arrange
        var tool = CreateToolWithSchema("no-schema", "No schema tool", null);
        var invokerMock = new Mock<IToolInvoker>();

        // Act
        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Assert
        fn.JsonSchema.ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task InvokeCoreAsync_DelegatesToToolInvoker_ReturnsSerializedResult()
    {
        // Arrange
        var tool = CreateToolWithSchema("check-health", "Check health", """{"type":"object"}""");
        var invokerMock = new Mock<IToolInvoker>();

        var resultData = JsonDocument.Parse("""{"status":"healthy","uptime":99.5}""").RootElement;
        invokerMock.Setup(i => i.InvokeAsync(
                tool,
                null,
                It.IsAny<IDictionary<string, object?>>(),
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolInvocationResultDto
            {
                Success = true,
                Data = resultData,
                DurationMs = 42,
                ToolRegistrationId = Guid.NewGuid(),
                InvokedAt = DateTime.UtcNow
            });

        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Act — InvokeCoreAsync is protected, call via public InvokeAsync
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["service"] = "api-gateway" });
        var result = await fn.InvokeAsync(args);

        // Assert
        result.Should().NotBeNull();
        var resultStr = result!.ToString()!;
        resultStr.Should().Contain("healthy");

        invokerMock.Verify(i => i.InvokeAsync(
            tool, null,
            It.Is<IDictionary<string, object?>>(d => d.ContainsKey("service")),
            null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeCoreAsync_InvokerReturnsFailure_ReturnsErrorString()
    {
        // Arrange
        var tool = CreateToolWithSchema("failing-tool", "Fails", """{"type":"object"}""");
        var invokerMock = new Mock<IToolInvoker>();

        invokerMock.Setup(i => i.InvokeAsync(
                tool, null,
                It.IsAny<IDictionary<string, object?>>(),
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolInvocationResultDto
            {
                Success = false,
                Error = "Connection refused",
                DurationMs = 100,
                ToolRegistrationId = Guid.NewGuid(),
                InvokedAt = DateTime.UtcNow
            });

        var fn = new ToolRegistrationAIFunction(tool, invokerMock.Object);

        // Act
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        // Assert
        result.Should().NotBeNull();
        result!.ToString().Should().Contain("Connection refused");
    }
}
