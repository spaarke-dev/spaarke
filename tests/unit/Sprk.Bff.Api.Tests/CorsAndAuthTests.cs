using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests;

[Trait("status", "repaired")]
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

    // Repointed 2026-08-26 by unified-access-control-r2 task 071. This test previously probed
    // `GET /api/obo/containers/{id}/children`, which task 071 DELETED (drive/container-keyed OBO
    // route with no per-document authorization decision; zero production callers). A deleted route
    // answers 404, which is not a statement about bearer enforcement — the thing this test exists to
    // check. Repointed at `PUT /api/obo/containers/{id}/files/{*path}`, which survives task 071
    // (11 live wizard call sites) and carries the same `RequireAuthorization()`.
    // Route ABSENCE for the four retired routes is asserted by
    // tests/integration/regression/OboDriveKeyedRouteRetirementTests.cs.
    [Fact(Skip = "Requires fully mocked Graph/Dataverse services - OBO endpoint returns 500 without Graph client")]
    public async Task Obo_Endpoints_RequireBearer()
    {
        using var body = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var res = await _client.PutAsync("/api/obo/containers/cont-id/files/probe.txt", body);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
