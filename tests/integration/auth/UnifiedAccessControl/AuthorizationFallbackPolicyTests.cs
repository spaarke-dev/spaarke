using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The RUNTIME half of "no BFF route is anonymous by omission" — the authorization <b>FallbackPolicy</b>
/// (owner round 14 item 2, unified-access-control-r2 task 167 f1).
///
/// <para><b>The failure being guarded.</b> Before owner round 14 the BFF set no FallbackPolicy, so an endpoint that
/// declared neither <c>RequireAuthorization(...)</c> nor <c>AllowAnonymous()</c> was callable by ANYONE, signed in or
/// not. The build-time rule (<c>RouteAuthorizationGuardTests.NoRouteIsAnonymousByOmission</c>) refuses such a route,
/// but a build rule protects the source, not the running app. The FallbackPolicy
/// (<see cref="AuthorizationModule.ApplyFallbackPolicy"/>) makes the omission answer 401 at runtime. Both stay: the
/// guard keeps every route's intent declared, the fallback keeps the runtime closed if a route ever slips past.</para>
///
/// <para><b>How this proves it.</b> A real endpoint with no authorization metadata cannot exist in the BFF (the guard
/// forbids it), so the behaviour is shown two ways: (1) in a minimal host built on the SAME
/// <see cref="AuthorizationModule.ApplyFallbackPolicy"/> the BFF calls — with and without it, so the negative control
/// shows the fallback is what closes the endpoint; and (2) in the REAL app through the one input the fallback also
/// governs there: a request that matches no endpoint is challenged 401 when anonymous and answered 404 when signed in.
/// The real app's endpoint table is also read: every mapped endpoint carries authorization metadata or an explicit
/// <c>IAllowAnonymous</c> — the runtime twin of the build-time rule.</para>
///
/// <para><b>Positive controls.</b> The explicitly anonymous probes (<c>/ping</c>, <c>/status</c>, <c>/healthz</c>) still
/// answer without a token: the fallback never applies to an endpoint that declares <c>AllowAnonymous()</c>. CORS
/// preflights are unaffected because <c>UseCors</c> answers them before <c>UseAuthorization</c>
/// (<c>CorsAndAuthTests.Cors_Preflight_AllowsConfiguredOrigin</c> preflights an UNMAPPED path and still gets 204).</para>
///
/// <para><b>Consequence elsewhere.</b> "An anonymous request to a deleted route answers 404" stopped being true with the
/// fallback — it answers 401. The route-retirement regression tests therefore prove absence WITH a bearer (404) and
/// from the endpoint table, never by an anonymous 404.</para>
/// </summary>
[Trait("status", "repaired")]
public class AuthorizationFallbackPolicyTests : IClassFixture<CustomWebAppFactory>
{
    private const string NoSuchRoute = "/api/zz-uac-r2-167-no-such-route";

    private readonly CustomWebAppFactory _factory;

    public AuthorizationFallbackPolicyTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        // CustomWebAppFactory's FakeAuthHandler authenticates any request that carries an Authorization header.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    // =============================================================================================
    // THE REAL APP
    // =============================================================================================

    [Fact]
    public async Task RealApp_AnAnonymousRequestThatNoEndpointDeclaresAuthorizationFor_IsChallenged401()
    {
        var response = await _factory.CreateClient().GetAsync(NoSuchRoute);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "the authorization FallbackPolicy (AuthorizationModule.ApplyFallbackPolicy) applies to every request that "
            + "reaches no endpoint authorization metadata — an unmatched request is exactly that. Before owner round 14 "
            + "this answered 404 to an anonymous caller; a 404 (or 200) here means the fallback is not applied.");
    }

    [Fact]
    public async Task RealApp_TheSameRequestWithABearer_Is404_SoThe401AboveIsTheFallbackNotARoute()
    {
        var response = await AuthenticatedClient().GetAsync(NoSuchRoute);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "a signed-in caller satisfies the fallback and then meets routing's 404. Without this control the 401 above "
            + "could be any authentication failure rather than the fallback challenging an unmatched request.");
    }

    [Theory]
    [InlineData("/ping")]
    [InlineData("/status")]
    [InlineData("/healthz")]
    public async Task RealApp_ExplicitlyAnonymousProbes_StayReachableWithoutSigningIn(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Unauthorized,
            $"{path} declares AllowAnonymous(); the FallbackPolicy never applies to an endpoint that declares it. A 401 "
            + "here would take down the App Service health check, the slot-swap warm-up and the deploy pollers.");
        ((int)response.StatusCode).Should().BeLessThan(500, $"{path} should be served in the test host");
    }

    [Fact]
    public void RealApp_EveryMappedEndpoint_DeclaresAuthorizationOrAnonymity()
    {
        var endpoints = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        endpoints.Should().HaveCountGreaterThan(300,
            "the endpoint table must be the real one for this check to mean anything");

        var undeclared = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null
                        && e.Metadata.GetMetadata<AuthorizationPolicy>() is null
                        && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>())} "
                         + $"{e.RoutePattern.RawText}  ({e.DisplayName})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        undeclared.Should().BeEmpty(
            "every BFF endpoint must declare RequireAuthorization(...) or AllowAnonymous() — the runtime twin of "
            + "RouteAuthorizationGuardTests.NoRouteIsAnonymousByOmission. The FallbackPolicy would answer these 401, "
            + "but an undeclared endpoint means the build-time guard missed a registration form; teach the scanner it.");

        // Non-vacuous: both kinds exist, and the liveness probe is one of the anonymous ones.
        endpoints.Should().Contain(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null && e.RoutePattern.RawText == "/ping");
        endpoints.Should().Contain(e => e.Metadata.GetMetadata<IAuthorizeData>() != null);
    }

    [Fact]
    public async Task EndpointTableHelper_TellsAMappedRouteFromAMissingOne_WhereAnAnonymous401CannotAnyMore()
    {
        // The reason EndpointTable exists: anonymously, a mapped and an unmapped path now answer the same 401.
        (await _factory.CreateClient().GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _factory.CreateClient().GetAsync(NoSuchRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // POSITIVE: mapped routes, including a parameterised and an aggregator-bound one.
        EndpointTable.Maps(_factory, "GET", "/api/me").Should().BeTrue();
        EndpointTable.Maps(_factory, "GET", "/ping").Should().BeTrue();
        EndpointTable.Maps(_factory, "GET", $"/api/documents/{Guid.NewGuid()}/preview-url").Should().BeTrue();
        EndpointTable.Maps(_factory, "GET", "/api/spe/containers?configId=x").Should().BeTrue();

        // NEGATIVE: an unmapped path, a mapped path with the wrong verb, and a retired route.
        EndpointTable.Maps(_factory, "GET", NoSuchRoute).Should().BeFalse();
        EndpointTable.Maps(_factory, "POST", "/ping").Should().BeFalse();
        EndpointTable.Maps(_factory, "GET", "/api/obo/containers/c1/children").Should().BeFalse();
    }

    // =============================================================================================
    // A HOST BUILT ON THE SAME AuthorizationModule.ApplyFallbackPolicy — with and without it
    // =============================================================================================

    private static async Task<WebApplication> StartHostAsync(bool applyFallbackPolicy)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(FakeAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            if (applyFallbackPolicy)
            {
                AuthorizationModule.ApplyFallbackPolicy(options);
            }
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/declares-nothing", () => Results.Text("served"));
        app.MapGet("/declares-anonymous", () => Results.Text("served")).AllowAnonymous();
        app.MapGet("/declares-sign-in", () => Results.Text("served")).RequireAuthorization();

        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app, bool signedIn)
    {
        var client = app.GetTestClient();
        if (signedIn)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        }

        return client;
    }

    [Fact]
    public async Task WithTheFallbackPolicy_AnEndpointThatDeclaresNothing_Is401ForAnonymous_AndServedWhenSignedIn()
    {
        await using var app = await StartHostAsync(applyFallbackPolicy: true);

        (await Client(app, signedIn: false).GetAsync("/declares-nothing")).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "the omission now fails CLOSED");

        var signedIn = await Client(app, signedIn: true).GetAsync("/declares-nothing");
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK, "the fallback asks only for an authenticated user");
        (await signedIn.Content.ReadAsStringAsync()).Should().Be("served");
    }

    [Fact]
    public async Task WithTheFallbackPolicy_DeclaredShapesKeepTheirMeaning()
    {
        await using var app = await StartHostAsync(applyFallbackPolicy: true);
        var anonymous = Client(app, signedIn: false);

        (await anonymous.GetAsync("/declares-anonymous")).StatusCode.Should().Be(
            HttpStatusCode.OK, "an explicit AllowAnonymous() is never overridden by the fallback");
        (await anonymous.GetAsync("/declares-sign-in")).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "RequireAuthorization() decides on its own policy, as before");
        (await anonymous.GetAsync("/no-such-route")).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "an unmatched request reaches no metadata, so the fallback challenges it");
        (await Client(app, signedIn: true).GetAsync("/no-such-route")).StatusCode.Should().Be(
            HttpStatusCode.NotFound, "a signed-in caller passes the fallback and meets routing's 404");
    }

    [Fact]
    public async Task NegativeControl_WithoutTheFallbackPolicy_AnEndpointThatDeclaresNothing_ServesAnyone()
    {
        // The state before owner round 14. If this ever answered 401, the test host would be closing the endpoint by
        // some other means and the two tests above would prove nothing about ApplyFallbackPolicy.
        await using var app = await StartHostAsync(applyFallbackPolicy: false);

        var response = await Client(app, signedIn: false).GetAsync("/declares-nothing");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "without a fallback an undeclared endpoint is public — the hole");
        (await response.Content.ReadAsStringAsync()).Should().Be("served");
    }
}
