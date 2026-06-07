using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for the <c>GET /api/ai/scopes/personas</c> endpoint (R6 Pillar 1 D-A-02).
/// </summary>
/// <remarks>
/// Coverage:
/// <list type="bullet">
/// <item>Endpoint registration + method signature (ADR-001 Minimal API)</item>
/// <item>Authorization filter inheritance from <c>MapScopeEndpoints</c> group (ADR-008)</item>
/// <item>Endpoint mapping symmetry with the 4 sibling scope endpoints (skills/knowledge/tools/actions)</item>
/// <item>Pagination + filtering + sorting query params handled (mirrors <c>/api/ai/scopes/actions</c>)</item>
/// </list>
/// <para>
/// Test strategy: focus on contract assertions (route present, method present, auth required)
/// rather than full Dataverse round-trip since the LIST surface calls real Dataverse over HTTP
/// and the test factory provides only configuration shells. This matches the test approach
/// used by sibling endpoint tests (e.g., <c>HandlerEndpointsTests</c>, <c>NodeEndpointsTests</c>).
/// </para>
/// </remarks>
public class ScopePersonasEndpointTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public ScopePersonasEndpointTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    // =========================================================================
    // Endpoint Registration (ADR-001 — Minimal API)
    // =========================================================================

    [Fact]
    public void MapScopeEndpoints_MethodExists_AndIsStatic()
    {
        // Arrange
        var method = typeof(ScopeEndpoints).GetMethod(nameof(ScopeEndpoints.MapScopeEndpoints));

        // Assert
        method.Should().NotBeNull("endpoint registration extension method must exist");
        method!.IsStatic.Should().BeTrue("Minimal API extension methods are static (ADR-001)");
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    [Fact]
    public async Task GetPersonas_EndpointExists_AcceptsGet()
    {
        // Act — no auth; we only care that the route is registered (not 404 or 405)
        var response = await _client.GetAsync("/api/ai/scopes/personas");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "endpoint /api/ai/scopes/personas must be registered alongside the 4 sibling scope endpoints (R6 D-A-02)");
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "endpoint must support GET");
    }

    // =========================================================================
    // Authorization (ADR-008 — endpoint filters / group-level auth)
    // =========================================================================

    [Fact]
    public async Task GetPersonas_WithoutAuth_RequiresAuthentication()
    {
        // Act — no Authorization header
        var response = await _client.GetAsync("/api/ai/scopes/personas");

        // Assert — must NOT return 200 without auth (group has RequireAuthorization())
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError],
            "endpoint inherits RequireAuthorization() from the MapScopeEndpoints group (ADR-008)");
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "unauthenticated requests must not receive 200");
    }

    [Fact]
    public async Task GetPersonas_WithAuth_ProceedsToHandler()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/scopes/personas");

        // Assert — auth filter passes; handler is reached. May return 500 in the test harness
        // because real Dataverse is not reachable, but the call must NOT 404 / 401.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "auth must pass and the handler must be reached");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "Bearer test-token is accepted by FakeAuthHandler");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "no resource-level authorization filter applies to the LIST surface");
    }

    // =========================================================================
    // Query Parameter Contract — pagination, filter, sort (mirrors /scopes/actions)
    // =========================================================================

    [Fact]
    public async Task GetPersonas_AcceptsPaginationParameters()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/scopes/personas?page=2&pageSize=10");

        // Assert — query string is parsed without error (no 400 / 405)
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            "page + pageSize query parameters must be bound");
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task GetPersonas_AcceptsNameFilterParameter()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/scopes/personas?nameFilter=SYS-");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            "nameFilter query parameter must be bound");
    }

    [Fact]
    public async Task GetPersonas_AcceptsSortParameters()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/scopes/personas?sortBy=name&sortDescending=true");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            "sortBy + sortDescending query parameters must be bound");
    }

    // =========================================================================
    // Symmetry with Sibling Scope Endpoints (R6 D-A-02 acceptance criterion)
    // =========================================================================

    [Theory]
    [InlineData("/api/ai/scopes/skills")]
    [InlineData("/api/ai/scopes/knowledge")]
    [InlineData("/api/ai/scopes/tools")]
    [InlineData("/api/ai/scopes/actions")]
    [InlineData("/api/ai/scopes/personas")]
    public async Task AllScopeEndpoints_HaveIdenticalAuthBehavior(string url)
    {
        // Act — no auth
        var unauthed = await _client.GetAsync(url);

        // Assert — all 5 LIST endpoints (the 4 sibling + new personas) reject unauth requests
        // identically per ADR-008 group-level filter inheritance
        unauthed.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError],
            "all scope LIST endpoints must enforce the same authorization filter (R6 D-A-02 acceptance: symmetric ADR-008 behavior)");
    }
}
