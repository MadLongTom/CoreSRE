using System.Net;
using System.Text.Json;
using A2A;
using CoreSRE.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Services;

public class A2ACardResolverServiceTests
{
    private readonly Mock<ILogger<A2ACardResolverService>> _loggerMock = new();

    private A2ACardResolverService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("A2ACardResolver")).Returns(httpClient);
        return new A2ACardResolverService(factoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ResolveAsync_Success_ReturnsMappedDto()
    {
        // Arrange
        var agentCard = new AgentCard
        {
            Name = "Test Agent",
            Description = "A test agent",
            Url = "https://example.com/agent",
            Version = "1.0.0",
            Skills =
            [
                new AgentSkill { Id = "s1", Name = "Skill One", Description = "Desc one", Tags = [] }
            ],
            AdditionalInterfaces =
            [
                new AgentInterface { Transport = AgentTransport.JsonRpc, Url = "/jsonrpc" }
            ],
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["apiKey"] = new ApiKeySecurityScheme("X-API-Key", "header")
            }
        };

        var json = JsonSerializer.Serialize(agentCard, A2AJsonUtilities.DefaultOptions);
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        // Act
        var result = await service.ResolveAsync("https://example.com/agent", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Agent");
        result.Description.Should().Be("A test agent");
        result.Url.Should().Be("https://example.com/agent");
        result.Version.Should().Be("1.0.0");
        result.Skills.Should().HaveCount(1);
        result.Skills[0].Name.Should().Be("Skill One");
        result.Skills[0].Description.Should().Be("Desc one");
        result.Interfaces.Should().HaveCount(1);
        result.Interfaces[0].Protocol.Should().Be("JSONRPC");
        result.Interfaces[0].Path.Should().Be("/jsonrpc");
        result.SecuritySchemes.Should().HaveCount(1);
        result.SecuritySchemes[0].Type.Should().Be("apiKey");
        result.SecuritySchemes[0].Parameters.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResolveAsync_HttpError_ThrowsHttpRequestException()
    {
        // Arrange — SDK wraps HttpRequestException into A2AException, service unwraps it
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        // Act & Assert
        var act = () => service.ResolveAsync("https://example.com/agent", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ResolveAsync_Timeout_ThrowsTaskCanceledException()
    {
        // Arrange
        var handler = new DelayHttpHandler(TimeSpan.FromSeconds(30));
        var service = CreateService(handler);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        var act = () => service.ResolveAsync("https://example.com/agent", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange — SDK throws A2AException for parse errors, service wraps to InvalidOperationException
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not valid json", System.Text.Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        // Act & Assert
        var act = () => service.ResolveAsync("https://example.com/agent", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Data["AgentCardParseError"].Should().Be(true);
    }

    [Fact]
    public async Task ResolveAsync_EmptySkillsAndInterfaces_ReturnsEmptyLists()
    {
        // Arrange
        var agentCard = new AgentCard
        {
            Name = "Minimal Agent",
            Description = "Minimal",
            Url = "https://minimal.com",
            Version = "0.1.0",
            Skills = [],
            AdditionalInterfaces = [],
            SecuritySchemes = null
        };

        var json = JsonSerializer.Serialize(agentCard, A2AJsonUtilities.DefaultOptions);
        var handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        // Act
        var result = await service.ResolveAsync("https://minimal.com", CancellationToken.None);

        // Assert
        result.Skills.Should().BeEmpty();
        result.Interfaces.Should().BeEmpty();
        result.SecuritySchemes.Should().BeEmpty();
    }

    /// <summary>Mock HTTP handler that returns a fixed response.</summary>
    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public MockHttpHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    /// <summary>Mock HTTP handler that delays indefinitely to simulate timeout.</summary>
    private class DelayHttpHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public DelayHttpHandler(TimeSpan delay) => _delay = delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
