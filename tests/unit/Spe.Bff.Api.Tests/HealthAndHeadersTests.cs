using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class HealthAndHeadersTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    public HealthAndHeadersTests(CustomWebAppFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Healthz_Returns200()
    {
        var res = await _client.GetAsync("/healthz");
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_ReturnsTraceId_And_Json()
    {
        var res = await _client.GetAsync("/ping");
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("traceId");
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SecurityHeaders_Present()
    {
        var res = await _client.GetAsync("/ping");
        var headers = res.Headers;
        headers.Contains("X-Content-Type-Options").Should().BeTrue();
        headers.Contains("Strict-Transport-Security").Should().BeTrue();
        headers.Contains("Referrer-Policy").Should().BeTrue();
        headers.Contains("X-Frame-Options").Should().BeTrue();
        headers.Contains("X-XSS-Protection").Should().BeTrue();
        headers.Contains("Content-Security-Policy").Should().BeTrue();
    }
}