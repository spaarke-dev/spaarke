using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class CorsAndAuthTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    public CorsAndAuthTests(CustomWebAppFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Cors_Preflight_AllowsConfiguredOrigin()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/containers");
        req.Headers.Add("Origin", "https://localhost:5173");
        req.Headers.Add("Access-Control-Request-Method", "GET");
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        res.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
    }

    [Fact]
    public async Task Obo_Endpoints_RequireBearer()
    {
        var res = await _client.GetAsync("/api/obo/containers/cont-id/children");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}