using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests;

[Trait("status", "repaired")]
public class CorsAndAuthTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebAppFactory _factory;
    public CorsAndAuthTests(CustomWebAppFactory f)
    {
        _factory = f;
        _client = f.CreateClient();
    }

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

    // Repointed TWICE, and the reason is the same both times: this test checks BEARER ENFORCEMENT, so
    // it must probe a route that EXISTS. A deleted route answers 404, which says nothing about auth.
    //
    //   2026-08-26 (task 071): from `GET /api/obo/containers/{id}/children` — deleted as a
    //     container-keyed route with no per-document decision — to
    //     `PUT /api/obo/containers/{id}/files/{*path}`.
    //   2026-09-03 (task 076): that route was deleted too, for the same class of reason (it wrote
    //     bytes to a CALLER-NAMED container). Now probes `PUT /api/obo/me/files/{*path}`, one of the
    //     three replacements, none of which takes a container parameter.
    //
    // Route ABSENCE for all five retired routes is asserted by
    // tests/integration/regression/OboDriveKeyedRouteRetirementTests.cs.
    [Fact]
    public async Task Obo_Endpoints_RequireBearer()
    {
        using var body = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var res = await _client.PutAsync("/api/obo/me/files/probe.txt", body);
        // A bare 401 challenge (no problem+json body): the auth layer rejects the request before the
        // handler runs, so no Graph client is needed. The FallbackPolicy answers 401 for an unmapped
        // route too, so the endpoint table is what proves this route still exists.
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        EndpointTable.AssertMapped(_factory, "PUT", "/api/obo/me/files/probe.txt");
    }
}
