using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 091 — proves every route in
/// <c>Api/SpeAdmin/ContainerItemEndpoints.cs</c> is subject to the SPE-admin role gate.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> <c>Api/SpeAdminEndpoints.cs</c> builds the <c>/api/spe</c> group with
/// <c>RequireAuthorization()</c> + <c>AddSpeAdminAuthorizationFilter()</c> (layer 1: is the caller an
/// SPE admin at all) + <c>AddSpeAdminTenantScopeFilter()</c> (layer 2: whose data may that admin
/// reach). Eighteen sibling endpoint groups register ON that group and inherit both. This file's
/// routes were registered on the <b>root app</b> instead — same URL prefix, neither filter. Bare
/// <c>.RequireAuthorization()</c> means <i>authenticated</i>, and no <c>DefaultPolicy</c> /
/// <c>FallbackPolicy</c> override exists to raise that bar. So any authenticated caller could
/// enumerate, download, preview, mint a sharing link for, delete, and upload into any container id
/// they named, with the client-supplied <c>configId</c> unchecked across tenants.
/// </para>
/// <para>
/// <b>Why this test is HTTP-level and not a filter unit test.</b> The sibling
/// <see cref="SpeAdminAuthorizationLayerTests"/> invokes the filter directly, which is right for
/// asking "does the filter decide correctly". It structurally cannot ask <i>this</i> question, because
/// the defect was never in the filter — it was that the filter was never attached. Nor can endpoint
/// reflection answer it: as <c>RouteAuthorizationGuardTests</c> records, <c>AddEndpointFilter</c>
/// appends to an internal filter-factory list compiled into the endpoint's <c>RequestDelegate</c> and
/// contributes NOTHING to <c>EndpointBuilder.Metadata</c> — there is no <c>IEndpointFilterMetadata</c>
/// to reflect over. A real request through the real pipeline is the only instrument that observes
/// whether a filter actually runs.
/// </para>
/// <para>
/// <b>Non-vacuity.</b> A test asserting 403 passes trivially if the fixture denies everything for an
/// unrelated reason (misconfigured auth, a 403 from some other filter, a route that does not exist).
/// Three controls close that off: <see cref="AGenuineAdminGroupRoute_ForACallerWithNoAdminRole_IsForbidden"/>
/// proves the fixture can observe the gate on a route that always had it;
/// <see cref="EveryContainerItemRoute_ForAnAdminCaller_IsNotBlockedByTheRoleGate"/> proves the gate is
/// not simply refusing everyone; and every route is requested with a syntactically valid
/// <c>configId</c> so the request reaches the filter pipeline instead of short-circuiting at parameter
/// binding.
/// </para>
/// <para>ADR-038 §2 path #1 (security-auth). ADR-008 — the fix is filters + registration, never
/// per-handler checks.</para>
/// </remarks>
public sealed class SpeAdminContainerItemRouteGateTests
    : IClassFixture<SpeAdminRouteGateFixture>
{
    private readonly SpeAdminRouteGateFixture _fixture;

    public SpeAdminContainerItemRouteGateTests(SpeAdminRouteGateFixture fixture) => _fixture = fixture;

    /// <summary>A container id shaped like a real SPE container id.</summary>
    private const string ContainerId = "b!TESTCONTAINERIDFORROUTEGATETESTS0000000000";

    /// <summary>An item id shaped like a real DriveItem id.</summary>
    private const string ItemId = "01TESTITEMIDFORROUTEGATETESTS";

    /// <summary>
    /// Syntactically valid so the request reaches the FILTER pipeline. Minimal-API parameter binding
    /// runs before endpoint filters, so a missing/invalid <c>configId</c> would short-circuit at 400
    /// and the test would assert nothing about authorization.
    /// </summary>
    private const string ConfigId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    /// <summary>
    /// The prefix <c>SpeAdminEndpoints.MapSpeAdminEndpoints</c> gives the group these routes register
    /// on. Routes in the endpoint file are group-relative; the URLs below are absolute.
    /// </summary>
    private const string SpeAdminGroupPrefix = "/api/spe";

    /// <summary>
    /// Every route registered by <c>ContainerItemEndpoints.MapContainerItemEndpoints</c>.
    /// <para>
    /// This is the WHOLE file, not the write subset. Task 083's census reported three routes here
    /// because its instrument scanned for SPE <i>write sinks</i>; the six read routes — including file
    /// download and sharing-link creation — were invisible to it. A tool finds what it was built to
    /// look for, and the count it returns is not the size of the problem.
    /// </para>
    /// <para>
    /// Adding a route to that file without adding it here is caught by
    /// <see cref="TheRouteTableCoversEveryRouteInTheEndpointFile"/>.
    /// </para>
    /// </summary>
    public static TheoryData<string, string> EveryContainerItemRoute() => new()
    {
        { "GET",    $"/api/spe/containers/{ContainerId}/items?configId={ConfigId}" },
        { "GET",    $"/api/spe/containers/{ContainerId}/items/{ItemId}/versions?configId={ConfigId}" },
        { "GET",    $"/api/spe/containers/{ContainerId}/items/{ItemId}/thumbnails?configId={ConfigId}" },
        { "POST",   $"/api/spe/containers/{ContainerId}/items/{ItemId}/share?configId={ConfigId}" },
        { "GET",    $"/api/spe/containers/{ContainerId}/items/{ItemId}/content?configId={ConfigId}" },
        { "GET",    $"/api/spe/containers/{ContainerId}/items/{ItemId}/preview?configId={ConfigId}" },
        { "DELETE", $"/api/spe/containers/{ContainerId}/items/{ItemId}?configId={ConfigId}" },
        { "POST",   $"/api/spe/containers/{ContainerId}/folders?configId={ConfigId}" },
        { "POST",   $"/api/spe/containers/{ContainerId}/items/upload?configId={ConfigId}" },
    };

    private static HttpRequestMessage Request(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        // POST routes bind a body; supply an empty JSON object so binding succeeds and the request
        // reaches the filter pipeline rather than failing at 400 before authorization runs.
        if (method == "POST")
            request.Content = JsonContent.Create(new { });

        return request;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // THE test this class exists for
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A signed-in caller holding no Spaarke admin app role must be refused on EVERY route in the
    /// file. Before task 091 this failed on all nine: they were registered on the root app, so the
    /// group's role filter never ran and the request proceeded to the handler.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryContainerItemRoute))]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task EveryContainerItemRoute_ForACallerWithNoAdminRole_IsForbidden(
        string method, string url)
    {
        using var client = _fixture.CreateClientWithRoles();

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "every /api/spe route must sit behind the SPE-admin role gate; {0} {1} reaching the " +
            "handler means it was registered outside the /api/spe group and any authenticated " +
            "caller can drive it",
            method, url);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Controls — without these the assertion above can pass vacuously
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POSITIVE CONTROL for the fixture. A route that has ALWAYS been inside the <c>/api/spe</c> group
    /// must refuse the same non-admin caller. If this fails, the fixture is not exercising the role
    /// gate at all and every assertion in this class is meaningless.
    /// </summary>
    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task AGenuineAdminGroupRoute_ForACallerWithNoAdminRole_IsForbidden()
    {
        using var client = _fixture.CreateClientWithRoles();

        var response = await client.GetAsync("/api/spe/containertypes");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "this route was never in question — it is the control proving this fixture can observe " +
            "the role gate. If it does not 403, the nine-route assertion proves nothing");
    }

    /// <summary>
    /// NEGATIVE CONTROL for the gate. Attaching the role filter must not refuse a legitimate admin.
    /// A gate that blocks the shipped SPE Admin client gets reverted, and the revert reopens all nine
    /// routes — so "does not over-block" is part of the fix, not a nicety.
    /// </summary>
    /// <remarks>
    /// Asserts only that the ROLE gate did not fire. An admin here still meets the tenant-scope filter
    /// with an unknown <c>configId</c> and then a handler with no reachable Graph, so 404/400/500 are
    /// all legitimate outcomes. The one outcome that would mean the gate over-blocks is 403.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryContainerItemRoute))]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task EveryContainerItemRoute_ForAnAdminCaller_IsNotBlockedByTheRoleGate(
        string method, string url)
    {
        using var client = _fixture.CreateClientWithRoles("Admin");

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            "an Admin app role satisfies layer 1; {0} {1} returning 403 for an admin means the gate " +
            "over-blocks and would break the shipped SPE Admin client",
            method, url);
    }

    /// <summary>
    /// AUTHENTICATION CONTROL. Task 091 deleted the per-route <c>.RequireAuthorization()</c> from all
    /// nine routes because the group supplies it — which means authentication now rests ENTIRELY on
    /// group inheritance. That is a reasonable thing to rely on and an unreasonable thing to assume, so
    /// it is asserted: an anonymous caller must be challenged, not served.
    /// </summary>
    /// <remarks>
    /// 401, not 403 — nothing is known about an anonymous caller's roles, so a role-based refusal would
    /// be a guess. Same distinction <see cref="SpeAdminAuthorizationLayerTests"/> pins at the filter
    /// level; this asserts the pipeline actually reaches it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryContainerItemRoute))]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task EveryContainerItemRoute_ForAnAnonymousCaller_IsUnauthorized(
        string method, string url)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "these routes carry no per-route RequireAuthorization() — the /api/spe group supplies it. " +
            "{0} {1} serving an anonymous caller means that inheritance is not in effect",
            method, url);
    }

    /// <summary>
    /// CENSUS CONTROL. The route table above is hand-maintained, and a hand-maintained census of this
    /// exact surface is what let the defect survive four recounts — <c>RouteAuthorizationGuardTests</c>
    /// governs 12 files and this file was not among them. Asserts the table has one entry per
    /// <c>Map{Verb}</c> call site in the endpoint file, so a tenth route cannot be added silently.
    /// </summary>
    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void TheRouteTableCoversEveryRouteInTheEndpointFile()
    {
        var source = File.ReadAllText(EndpointFilePath());

        var mapped = System.Text.RegularExpressions.Regex
            .Matches(source, @"\.Map(Get|Post|Put|Patch|Delete)\s*\(\s*""(?<route>[^""]+)""")
            .Select(m => m.Groups["route"].Value)
            .ToList();

        mapped.Should().HaveCountGreaterThan(
            0, "the scan must find routes, or this control passes vacuously");

        // Every route MUST be group-relative. An absolute "/api/..." template here is the signature of
        // the task 091 defect: it is what let these routes be registered on the root app while still
        // resolving to admin URLs. Registering on the group is what attaches the two filters, and a
        // route that spells its own full path is a route that does not need the group to resolve.
        mapped.Should().OnlyContain(
            route => !route.StartsWith("/api/", StringComparison.Ordinal),
            "routes in this file register on the /api/spe group and must be group-relative; an " +
            "absolute /api/... template is how the pre-091 root-app registration went unnoticed");

        var covered = EveryContainerItemRoute()
            .Select(row => ((string)row[1]).Split('?')[0])
            .ToList();

        foreach (var route in mapped)
        {
            // Compare by shape: the group prefix, plus the template with its {placeholders} replaced
            // by the concrete values this class uses.
            var concrete = (SpeAdminGroupPrefix + route)
                .Replace("{id}", ContainerId)
                .Replace("{itemId}", ItemId);

            covered.Should().Contain(
                concrete,
                "route {0} exists in ContainerItemEndpoints.cs but is not in this class's table, so " +
                "nothing asserts it is gated — the exact blind spot task 091 closed",
                route);
        }

        covered.Should().HaveCount(
            mapped.Count,
            "the table must not carry stale entries either — a route removed from the endpoint file " +
            "leaves an assertion that silently tests nothing");
    }

    private static string EndpointFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repository root must be locatable from the test output directory");

        return Path.Combine(
            dir!.FullName,
            "src", "server", "api", "Sprk.Bff.Api", "Api", "SpeAdmin", "ContainerItemEndpoints.cs");
    }
}

/// <summary>
/// Host fixture whose caller's app roles are chosen per request, so a NON-admin caller can be
/// expressed. <see cref="WorkspaceTestFixture"/>'s own handler hard-codes
/// <c>roles: SystemAdmin</c> — every caller through it is an admin, which cannot exercise a role gate.
/// </summary>
public sealed class SpeAdminRouteGateFixture : WorkspaceTestFixture
{
    internal const string RolesHeader = "X-Test-App-Roles";

    /// <summary>
    /// Sent for a signed-in caller holding NO app roles. A sentinel is required rather than an empty
    /// header value because <see cref="HttpClient"/> drops headers whose value is empty — which would
    /// make "signed in with no roles" arrive identically to "not signed in", collapsing the 403 case
    /// into the 401 case. (Discovered by this class's own anonymous control failing 10 tests.)
    /// </summary>
    internal const string NoRoles = "(none)";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Registered AFTER the base so this scheme wins as the default. Same layering the
        // OfficeSaveTestFixture uses for its probe substitution.
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RoleHeaderAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RoleHeaderAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthHandler>(
                RoleHeaderAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>An authenticated caller holding exactly <paramref name="roles"/> — none by default.</summary>
    public HttpClient CreateClientWithRoles(params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(RolesHeader, roles.Length == 0 ? NoRoles : string.Join(",", roles));
        return client;
    }

    /// <summary>
    /// A caller who is not signed in at all. Sends no roles header, which
    /// <see cref="RoleHeaderAuthHandler"/> treats as "no credential presented" — distinct from
    /// <see cref="CreateClientWithRoles()"/> with no roles, which is a signed-in caller holding none.
    /// </summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

/// <summary>
/// Authenticates every request and grants exactly the app roles named in
/// <see cref="SpeAdminRouteGateFixture.RolesHeader"/>. The caller is always signed in — this fixture
/// tests the ROLE gate (403), not authentication (401), which
/// <see cref="SpeAdminAuthorizationLayerTests"/> already covers.
/// </summary>
internal sealed class RoleHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RoleHeaderAuth";

    public RoleHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header at all == no credential presented, so the request is anonymous and the pipeline
        // must challenge it. An EMPTY header is different: a signed-in caller holding zero app roles.
        // Collapsing the two would make the 401-vs-403 distinction untestable.
        if (!Request.Headers.ContainsKey(SpeAdminRouteGateFixture.RolesHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", "0b7d1f60-9c3a-4d21-8f5e-2a6b7c8d9e01"),
            new(ClaimTypes.NameIdentifier, "0b7d1f60-9c3a-4d21-8f5e-2a6b7c8d9e01"),
            new(ClaimTypes.Name, "Route Gate Test Caller"),
            new("tid", "11111111-2222-3333-4444-555555555555"),
        };

        if (Request.Headers.TryGetValue(SpeAdminRouteGateFixture.RolesHeader, out var header))
        {
            claims.AddRange(
                header.ToString()
                      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(role => role != SpeAdminRouteGateFixture.NoRoles)
                      .Select(role => new Claim("roles", role)));
        }

        // "roles" as the role claim type so IsInRole() resolves the same claims Entra emits.
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
