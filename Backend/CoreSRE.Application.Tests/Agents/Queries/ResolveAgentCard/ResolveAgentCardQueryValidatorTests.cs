using CoreSRE.Application.Agents.Queries.ResolveAgentCard;
using FluentAssertions;
using Xunit;

namespace CoreSRE.Application.Tests.Agents.Queries.ResolveAgentCard;

public class ResolveAgentCardQueryValidatorTests
{
    private readonly ResolveAgentCardQueryValidator _validator = new();

    [Fact]
    public void Should_Fail_When_Url_Is_Empty()
    {
        var query = new ResolveAgentCardQuery(string.Empty);
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Url");
    }

    [Fact]
    public void Should_Fail_When_Url_Has_Invalid_Scheme()
    {
        var query = new ResolveAgentCardQuery("ftp://example.com/agent");
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("http://") || e.ErrorMessage.Contains("https://"));
    }

    [Fact]
    public void Should_Fail_When_Url_Is_Not_Valid_Uri()
    {
        var query = new ResolveAgentCardQuery("not-a-valid-url");
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_Url_Exceeds_Max_Length()
    {
        var longUrl = "https://example.com/" + new string('a', 2048);
        var query = new ResolveAgentCardQuery(longUrl);
        var result = _validator.Validate(query);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("2048"));
    }

    [Theory]
    [InlineData("https://example.com/agent")]
    [InlineData("http://localhost:5100")]
    [InlineData("https://my-agent.example.com:8080/a2a")]
    public void Should_Pass_For_Valid_Http_Urls(string url)
    {
        var query = new ResolveAgentCardQuery(url);
        var result = _validator.Validate(query);
        result.IsValid.Should().BeTrue();
    }
}
