using CoreSRE.Application.Agents.DTOs;
using CoreSRE.Application.Agents.Queries.ResolveAgentCard;
using CoreSRE.Application.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Agents.Queries.ResolveAgentCard;

public class ResolveAgentCardQueryHandlerTests
{
    private readonly Mock<IAgentCardResolver> _resolverMock = new();
    private readonly Mock<ILogger<ResolveAgentCardQueryHandler>> _loggerMock = new();
    private readonly ResolveAgentCardQueryHandler _handler;

    public ResolveAgentCardQueryHandlerTests()
    {
        _handler = new ResolveAgentCardQueryHandler(_resolverMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_Success_ReturnsOkResult()
    {
        // Arrange
        var dto = new ResolvedAgentCardDto
        {
            Name = "Test Agent",
            Description = "Desc",
            Url = "https://example.com",
            Version = "1.0.0",
            Skills = [new AgentSkillDto { Name = "Skill1", Description = "D" }]
        };

        _resolverMock
            .Setup(r => r.ResolveAsync("https://example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new ResolveAgentCardQuery("https://example.com");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Test Agent");
    }

    [Fact]
    public async Task Handle_HttpRequestException_Returns502()
    {
        // Arrange
        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var query = new ResolveAgentCardQuery("https://unreachable.com");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(502);
        result.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task Handle_Timeout_Returns504()
    {
        // Arrange
        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to timeout."));

        var query = new ResolveAgentCardQuery("https://slow-agent.com");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(504);
        result.Message.Should().Contain("超时");
    }

    [Fact]
    public async Task Handle_AgentCardParseError_Returns422()
    {
        // Arrange — Service converts A2AException to InvalidOperationException with marker
        var ex = new InvalidOperationException("Failed to parse agent card");
        ex.Data["AgentCardParseError"] = true;

        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        var query = new ResolveAgentCardQuery("https://bad-json.com");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_UriFormatException_ReturnsFail()
    {
        // Arrange
        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UriFormatException("Invalid URI format"));

        var query = new ResolveAgentCardQuery("not://valid");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("URL 格式无效");
    }
}
