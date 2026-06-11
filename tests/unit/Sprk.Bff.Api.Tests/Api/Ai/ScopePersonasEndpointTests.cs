using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly CustomWebAppFactory _factory;

    public ScopePersonasEndpointTests(CustomWebAppFactory factory)
    {
        _factory = factory;
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

    // =========================================================================
    // Rate Limiting (ADR-016 — R6 audit item 03 atomic fix)
    //
    // All 5 scope LIST endpoints MUST apply the `ai-context` rate-limit policy
    // (60 req/min sliding window per user, defined in RateLimitingModule).
    // Pre-fix state: 4 canonical endpoints (skills/knowledge/tools/actions) lacked
    // the policy; the 5th (personas, added by task 002) inherited the gap via
    // sibling parity. Atomic fix applied 2026-06-07.
    //
    // Verification strategy: introspect the live EndpointDataSource for the
    // `EnableRateLimitingAttribute` metadata. This is contract-level — it verifies
    // the policy is registered on the endpoint regardless of whether the test
    // factory wires up the full rate-limit middleware chain. (See
    // RateLimitingIntegrationTests for behavioral 429 verification on the shared
    // ai-context policy.)
    // =========================================================================

    [Theory]
    [InlineData("/api/ai/scopes/skills")]
    [InlineData("/api/ai/scopes/knowledge")]
    [InlineData("/api/ai/scopes/tools")]
    [InlineData("/api/ai/scopes/actions")]
    [InlineData("/api/ai/scopes/personas")]
    public void AllScopeEndpoints_HaveAiContextRateLimitPolicy(string pattern)
    {
        // Arrange — pull live endpoint data sources from the booted test host
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var endpoint = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(e => string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase));

        // Assert — endpoint exists
        endpoint.Should().NotBeNull(
            $"endpoint {pattern} must be registered (R6 audit item 03 covers all 5 scope LIST endpoints)");

        // Assert — endpoint has the `ai-context` rate-limit policy applied
        var rateLimitMetadata = endpoint!.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimitMetadata.Should().NotBeNull(
            $"endpoint {pattern} must have RequireRateLimiting metadata per ADR-016 (R6 audit item 03 atomic fix)");
        rateLimitMetadata!.PolicyName.Should().Be("ai-context",
            $"endpoint {pattern} must use the shared `ai-context` policy (60 req/min sliding window per user) — sibling-parity per the 4 canonical scope endpoints");
    }

    [Fact]
    public void AllScopeEndpoints_PreserveAuthorizationFilter_AfterRateLimitAdd()
    {
        // Arrange — verify R6 audit item 03 did NOT disturb the group-level RequireAuthorization()
        // filter (per stop-and-report trigger: "adding rate limit breaks any existing endpoint test").
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var patterns = new[]
        {
            "/api/ai/scopes/skills",
            "/api/ai/scopes/knowledge",
            "/api/ai/scopes/tools",
            "/api/ai/scopes/actions",
            "/api/ai/scopes/personas"
        };

        foreach (var pattern in patterns)
        {
            var endpoint = dataSource.Endpoints
                .OfType<RouteEndpoint>()
                .FirstOrDefault(e => string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase));

            endpoint.Should().NotBeNull($"endpoint {pattern} must be registered");

            // Endpoint must still carry the group-level authorization metadata
            // (RequireAuthorization() produces AuthorizeAttribute in the metadata chain).
            var authMetadata = endpoint!.Metadata
                .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .ToList();
            authMetadata.Should().NotBeEmpty(
                $"endpoint {pattern} must retain group-level RequireAuthorization() after rate-limit addition (ADR-008 unchanged by audit item 03)");
        }
    }
}
