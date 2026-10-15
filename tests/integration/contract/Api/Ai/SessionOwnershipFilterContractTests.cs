using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <c>tests/integration/contract/**</c> — endpoint-contract KEEP category (ADR-038 §2). The wire contract of
/// <c>AddSessionOwnershipFilter()</c> attached to real routes: status, <c>application/problem+json</c>, and the
/// <c>errorCode</c> + <c>correlationId</c> members a client and an operator join on (ADR-019). Issue #1800.
/// </summary>
/// <remarks>
/// The sibling contract suites (<c>DispatchSessionEndpointContractTests</c>, <c>SummarizeSessionEndpointContractTests</c>,
/// …) assert the error code with a substring check on the raw body. That passes when <c>errorCode</c> is renamed or
/// dropped as long as the value text appears anywhere, and it never checks that <c>correlationId</c> carries the
/// request's trace identifier. Here the body is parsed and each member is asserted by name and value, on a minimal
/// in-process host (real routing + the real filter extension, a real <see cref="ChatSessionManager"/>; only the
/// authenticated principal is stubbed, from request headers) so the assertions are about the filter alone.
/// </remarks>
[Trait("category", "endpoint-contract")]
public sealed class SessionOwnershipFilterContractTests : IAsyncLifetime
{
    private const string Tenant = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string Owner = "11111111-aaaa-bbbb-cccc-000000000001";
    private const string OtherUser = "22222222-aaaa-bbbb-cccc-000000000002";
    private const string Correlation = "corr-1800-trace-identifier";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private ChatSessionManager _manager = null!;
    private int _handlerInvocations;

    public async Task InitializeAsync()
    {
        _manager = new ChatSessionManager(
            cache: new InMemoryTenantCache(),
            dataverseRepository: new CapturingChatDataverseRepository(),
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_manager);

        _app = builder.Build();

        // The caller and the trace identifier come from headers; everything after this is production code.
        _app.Use(async (ctx, next) =>
        {
            var claims = new List<Claim>();
            if (ctx.Request.Headers.TryGetValue("x-test-oid", out var oid))
            {
                claims.Add(new Claim("oid", oid.ToString()));
            }

            if (ctx.Request.Headers.TryGetValue("x-test-tid", out var tid))
            {
                claims.Add(new Claim("tid", tid.ToString()));
            }

            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
            ctx.TraceIdentifier = Correlation;
            await next();
        });

        _app.MapGet("/api/test/sessions/{sessionId}", () =>
        {
            Interlocked.Increment(ref _handlerInvocations);
            return Results.Ok();
        }).AddSessionOwnershipFilter();

        // A route the filter should never have been attached to: no {sessionId} segment.
        _app.MapGet("/api/test/not-session-scoped", () =>
        {
            Interlocked.Increment(ref _handlerInvocations);
            return Results.Ok();
        }).AddSessionOwnershipFilter();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(
        string url, string? oid = Owner, string? tid = Tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (oid is not null)
        {
            request.Headers.Add("x-test-oid", oid);
        }

        if (tid is not null)
        {
            request.Headers.Add("x-test-tid", tid);
        }

        var response = await _client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(raw) ? default : JsonDocument.Parse(raw).RootElement.Clone();
        return (response, body);
    }

    [Fact]
    public async Task Get_AnotherUsersSession_Returns404_WithErrorCodeAndCorrelationIdMembers()
    {
        var victim = await _manager.CreateSessionAsync(Tenant, Owner, documentId: null);

        var (response, body) = await GetAsync($"/api/test/sessions/{victim.SessionId}", oid: OtherUser);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("title").GetString().Should().Be("Not Found");
        body.GetProperty("errorCode").GetString().Should().Be(
            SessionOwnershipFilterExtensions.NotFoundOrNotOwnedErrorCode,
            "a client tells THIS 404 from any other on the route by errorCode");
        body.GetProperty("correlationId").GetString().Should().Be(Correlation,
            "ADR-019: the response must carry the request's trace identifier so it joins the deny log line");
        body.GetProperty("detail").GetString().Should().Be("The chat session was not found.",
            "ADR-019: a fixed sentence — no identifier is echoed back to a caller probing with it");
        body.GetProperty("detail").GetString().Should().NotContain(victim.SessionId);
        body.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7231#section-6.5.4");
        _handlerInvocations.Should().Be(0, "the handler must not run for a denied caller");
    }

    [Fact]
    public async Task Get_MissingSession_Returns404_WithTheSameErrorCodeAsAForeignSession()
    {
        var (response, body) = await GetAsync("/api/test/sessions/ffffffffffffffffffffffffffffffff");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.GetProperty("errorCode").GetString().Should().Be(
            SessionOwnershipFilterExtensions.NotFoundOrNotOwnedErrorCode);
        body.GetProperty("correlationId").GetString().Should().Be(Correlation);
        _handlerInvocations.Should().Be(0);
    }

    [Fact]
    public async Task Get_PrincipalWithoutTenantClaim_Returns401_WithTidMissingErrorCodeAndCorrelationId()
    {
        var victim = await _manager.CreateSessionAsync(Tenant, Owner, documentId: null);

        var (response, body) = await GetAsync($"/api/test/sessions/{victim.SessionId}", tid: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.GetProperty("status").GetInt32().Should().Be(401);
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.GetProperty("detail").GetString().Should().Be("Tenant ID not found in token claims (tid).");
        body.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7235#section-3.1");
        body.GetProperty("errorCode").GetString().Should().Be(SessionOwnershipFilterExtensions.TenantMissingErrorCode);
        body.GetProperty("errorCode").GetString().Should().Be("auth.tid-missing",
            "the same spelling SummarizeSessionEndpoint and DispatchSessionEndpoint publish — clients key on it");
        body.GetProperty("correlationId").GetString().Should().Be(Correlation);
        _handlerInvocations.Should().Be(0);
    }

    [Fact]
    public async Task Get_PrincipalWithoutObjectIdClaim_Returns401_AndDoesNotRunTheHandler()
    {
        var victim = await _manager.CreateSessionAsync(Tenant, Owner, documentId: null);

        var (response, body) = await GetAsync($"/api/test/sessions/{victim.SessionId}", oid: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.GetProperty("detail").GetString().Should().Be("User identity not found");
        body.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7235#section-3.1");
        // errorCode / correlationId on this 401 are deliberately NOT asserted either way: ADR-019 requires them and #1853 will add them.
        _handlerInvocations.Should().Be(0);
    }

    [Fact]
    public async Task Get_RouteWithoutASessionIdSegment_Returns500_AndNeverReachesTheHandler()
    {
        // The filter attached to a route that cannot supply the id it needs to check. Passing through would
        // make the guard silently inert there; the contract is a loud 500.
        var (response, body) = await GetAsync("/api/test/not-session-scoped");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.GetProperty("detail").GetString().Should().Be("Session ownership could not be established.");
        body.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc7231#section-6.6.1");
        _handlerInvocations.Should().Be(0, "an unchecked request must not reach the handler");
    }

    [Fact]
    public async Task Get_OwnersOwnSession_ReachesTheHandler()
    {
        // Positive control: a filter answering 404 to everyone satisfies every test above.
        var mine = await _manager.CreateSessionAsync(Tenant, Owner, documentId: null);

        var (response, _) = await GetAsync($"/api/test/sessions/{mine.SessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _handlerInvocations.Should().Be(1);
    }
}
