using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 083 (Phase 0c Secure Documents, Wave 2).
///
/// <para>Task 083 DELETED <c>Api/DocumentsEndpoints.cs</c> and with it the last two drive-keyed
/// app-only (managed-identity) write routes in the BFF:</para>
/// <list type="bullet">
///   <item><c>PUT /api/drives/{driveId}/upload</c></item>
///   <item><c>DELETE /api/drives/{driveId}/items/{itemId}</c></item>
/// </list>
///
/// <para><b>What was wrong.</b> Each took an SPE drive id straight off the ROUTE and wrote — or
/// DESTROYED — as the MANAGED IDENTITY. <c>SpeFileStore.UploadSmallAsync</c> and
/// <c>DeleteFileAsync</c> take no caller context and land on <c>_factory.ForApp()</c>, so unlike the OBO
/// routes SPE performed NO caller-side check: the operation proceeded regardless of the caller's
/// permission on that drive. Their only gate was <c>RequireAuthorization("canwritefiles")</c> →
/// <c>ResourceAccessRequirement("upload_file")</c> → <c>ResourceAccessHandler</c>, which is real and
/// fail-closed but resolves DOCUMENT rights from a DRIVE id — <c>ExtractResourceId</c> accepts
/// containerId / driveId / documentId / id interchangeably. A real mechanism pointed at the wrong
/// resource domain (ADR-003 authorization seams; ADR-008 requires the decision be an endpoint filter on
/// the right resource domain). This is finding #4's shape, on the pair that survived task 073 because
/// they lived outside the file it deleted.</para>
///
/// <para><b>Why the previous comment defending them was wrong</b>, recorded because it is the most
/// instructive part of this retirement. The deleted source read: <i>"The two endpoints BELOW are
/// deliberately retained: they use canwritefiles on routes that DO carry a {driveId} resource, so their
/// per-resource check is satisfiable."</i> CARRYING a resource is not the same as carrying the resource
/// the policy EVALUATES. The policy looks the value up as <c>sprk_documents({id})</c>, so a real Graph
/// drive id (<c>b!…</c>) is not a GUID, <c>RetrievePrincipalAccess</c> 400s, and the caller is denied;
/// conversely a valid <c>sprk_document</c> GUID passes the gate but is not addressable as a drive. No
/// constructible request both passed the gate and moved bytes. The routes were therefore ACCIDENTALLY
/// safe — protected by value-space disjointness between GUIDs and <c>b!…</c> ids — and a sentence in the
/// source had recorded that accident as a design decision. See <c>.claude/FAILURE-MODES.md</c> AP-12.
/// That accident stops holding the moment either id domain widens, which is why the disposition is
/// deletion and not "leave it, it denies anyway".</para>
///
/// <para><b>Why RETIRED and not GATED.</b> Both were dead, for DIFFERENT reasons — each verified
/// first-hand rather than inherited from a prior note, per the task-076 lesson that a "zero callers"
/// claim must be re-checked against HEAD:</para>
/// <list type="number">
///   <item><c>PUT …/upload</c> was dead UPSTREAM. Its only caller,
///   <c>src/dataverse/webresources/spaarke_documents/DocumentOperations.js</c>
///   <c>processFileUpload</c>, first calls <c>GET /api/containers/{containerId}/drive</c> — deleted by
///   <c>spaarke-auth-v4-dataverse-MI</c> task 090 — and throws
///   <i>"Failed to get container drive information."</i> before the PUT is ever constructed.</item>
///   <item><c>DELETE …/items/{itemId}</c> was reachable in code (<c>processFileDelete</c> reads
///   <c>driveId</c>/<c>itemId</c> off form attributes, depending on no deleted route) but CANNOT
///   AUTHENTICATE: that file's <c>getAuthToken</c> returns <c>null</c> and its <c>apiCall</c> sends only
///   <c>credentials: 'include'</c>. The BFF's schemes are JwtBearer + ApiKey + Ciam — there is no cookie
///   scheme — so every call 401s before any policy runs. Whether the web resource is deployed is not
///   determinable from the repo (manual portal deploy per its README) and turned out not to matter.</item>
/// </list>
///
/// <para>Gating instead would have minted a SECOND record-keyed upload surface and a SECOND record-keyed
/// delete surface — a root <c>CLAUDE.md</c> §11 reuse failure on its face — because the sanctioned
/// record-keyed replacements already ship: creation via task 076's record-keyed route, deletion via
/// <c>Api/DocumentOperationsEndpoints.cs</c> → <c>DocumentCheckoutService</c>, which reads
/// <c>DriveId</c>/<c>ItemId</c> off the AUTHORIZED <c>sprk_document</c> row rather than off the request.
/// Deletion is remedy #2 in <c>RouteAuthorizationGuardTests</c>' own remedy list and follows task
/// 071/073/076 precedent.</para>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> someone re-adds a drive-keyed write route —
/// most plausibly while restoring the legacy <c>spaarke_documents</c> web resource, whose config block
/// still names both endpoints, or from one of the stale docs describing <c>/api/drives/{driveId}/…</c> as
/// the file API — and an unauthorized app-only write and DESTROY path silently returns. Nothing else in
/// the suite would notice, because a re-added route would simply start answering.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> A status code alone is the wrong bar for a write
/// route: a 403 returned AFTER the delete was issued is not a denial. So the load-bearing assertion is
/// <see cref="RetiredDriveKeyedWriteRoutes_AreAbsentFromTheEndpointTable"/>, which enumerates
/// <c>EndpointDataSource</c> and proves no handler exists to reach the operation at all — unfakeable by
/// any fixture or status-code mapping. The HTTP assertions add the behavioural half: 404 without a
/// bearer proves absence rather than rejection (ASP.NET Core routes BEFORE it authorizes, so a route
/// that EXISTS and carries RequireAuthorization answers 401, not 404), and 404 WITH a bearer proves the
/// 404 is not itself an authentication artifact.</para>
/// </summary>
[Trait("status", "repaired")]
public class DriveKeyedWriteRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    /// <summary>
    /// The two retired route patterns, exactly as they were registered in the deleted
    /// <c>Api/DocumentsEndpoints.cs</c>. Compared against the live endpoint table.
    /// </summary>
    private static readonly (string Verb, string Pattern)[] RetiredRoutes =
    {
        ("PUT", "/api/drives/{driveId}/upload"),
        ("DELETE", "/api/drives/{driveId}/items/{itemId}"),
    };

    /// <summary>
    /// The authorization policies task 083 deleted along with the routes. Both were orphaned by the
    /// deletion — <c>canwritefiles</c> had exactly these two consumers, and <c>canreadfiles</c> already
    /// had none — and both carry the wrong-resource-domain shape described in the class summary.
    /// </summary>
    private static readonly string[] RetiredPolicies = { "canwritefiles", "canreadfiles" };

    public DriveKeyedWriteRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    // Deliberately NO Authorization header: routing precedes authorization, so an absent route
    // answers 404 while a present one answers 401. See the class summary.
    private HttpClient CreateAnonymousClient() => _factory.CreateClient();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        // CustomWebAppFactory's FakeAuthHandler authenticates any request carrying a bearer token,
        // so this client clears the 401 stage. A 404 here therefore cannot be an auth artifact.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    // =============================================================================================
    // THE LOAD-BEARING ASSERTION — the write and destroy paths are unreachable, not merely refused
    // =============================================================================================

    [Fact]
    public void RetiredDriveKeyedWriteRoutes_AreAbsentFromTheEndpointTable()
    {
        // Enumerating the composed EndpointDataSource asks the only question that actually matters for a
        // retired write route: is there a handler at all? This is strictly stronger than asserting a
        // status code, and it is immune to the failure mode where a fixture change makes every request
        // 404 and turns the behavioural assertions below into vacuous passes.
        var endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        endpoints.Should().NotBeEmpty(
            "the endpoint table must be non-empty for this assertion to mean anything — an empty table "
            + "would make every 'route is absent' check below trivially true");

        var survivors = new List<string>();

        foreach (var (verb, pattern) in RetiredRoutes)
        {
            var normalized = pattern.TrimStart('/');

            var matches = endpoints.Where(e =>
                string.Equals(e.RoutePattern.RawText?.TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase));

            foreach (var match in matches)
            {
                var verbs = match.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                            ?? Array.Empty<string>();

                if (verbs.Count == 0 || verbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
                {
                    survivors.Add($"{verb} {pattern}  (registered as: {match.DisplayName})");
                }
            }
        }

        survivors.Should().BeEmpty(
            "these drive-keyed app-only (managed-identity) write routes were RETIRED by "
            + "unified-access-control-r2 task 083 and must not be re-registered. Each took an SPE drive "
            + "id off the route and wrote or DESTROYED as the managed identity, so SPE applied no "
            + "caller-side check, behind a policy that resolved DOCUMENT rights from a DRIVE id. There "
            + "are no callers: the supported creation path is the task-076 record-keyed route, and the "
            + "supported deletion path is Api/DocumentOperationsEndpoints.cs -> DocumentCheckoutService, "
            + "which reads DriveId/ItemId off the AUTHORIZED sprk_document row. If a genuine need for an "
            + "app-only HTTP write route reappears, it must authorize against the OWNING RECORD via the "
            + "task 075/076 container resolver — not against a caller-supplied drive id.\n\n"
            + "Re-registered routes:\n  " + string.Join("\n  ", survivors));
    }

    /// <summary>
    /// The policies are gone too, and their absence is worth asserting separately: re-registering
    /// <c>canwritefiles</c> is the first move someone makes when restoring one of these routes, and it
    /// is the step that would make the restoration LOOK authorized.
    ///
    /// <para>⚠️ This is NOT the DI-registration test ADR-038 §7 B3 bans. B3 forbids asserting that
    /// wiring EXISTS (<c>Assert.NotNull(services.GetRequiredService&lt;X&gt;())</c>) because the app
    /// starting already proves it. This asserts a security-relevant ABSENCE, which the app starting does
    /// not prove in either direction — a re-registered policy starts up perfectly happily. The behaviour
    /// protected is concrete: a named policy that resolves the wrong resource domain returns the wrong
    /// allow/deny answer, and does so silently.</para>
    /// </summary>
    [Fact]
    public async Task RetiredWrongResourceDomainPolicies_AreNotRegistered()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        // Control first: a policy that SHOULD exist must resolve. Without this, a provider that returned
        // null for everything would make the absence assertions below vacuous — the same trap the
        // positive controls at the bottom of this file guard against for the HTTP assertions.
        var survivingPolicy = await provider.GetPolicyAsync("canuploadfiles");
        survivingPolicy.Should().NotBeNull(
            "canuploadfiles (ResourceAccessRequirement(\"driveitem.content.upload\")) is still "
            + "registered in AuthorizationModule. If this is null, the policy provider is not resolving "
            + "named policies at all and the absence assertions below prove nothing.");

        var resurrected = new List<string>();

        foreach (var name in RetiredPolicies)
        {
            if (await provider.GetPolicyAsync(name) is not null)
            {
                resurrected.Add(name);
            }
        }

        resurrected.Should().BeEmpty(
            "these named authorization policies were DELETED by unified-access-control-r2 task 083 and "
            + "must not be re-registered. Both bound a ResourceAccessRequirement whose handler resolves "
            + "the route value as sprk_documents({id}) via ExtractResourceId, which accepts containerId / "
            + "driveId / documentId interchangeably — so the policy authorizes an SPE key against DOCUMENT "
            + "rights (ADR-003; ADR-008). canwritefiles was orphaned when task 083 deleted its only two "
            + "consumers; canreadfiles had already had zero consumers before that. A files read/write "
            + "policy that is genuinely needed again must evaluate the OWNING RECORD via the task 075/076 "
            + "resolver.\n\n"
            + "Re-registered policies:\n  " + string.Join("\n  ", resurrected));
    }

    // =============================================================================================
    // BEHAVIOURAL HALF — absence over HTTP, unauthenticated and authenticated
    // =============================================================================================

    [Fact]
    public async Task RetiredDriveKeyedUploadRoute_WithoutBearer_Returns404NotRouted()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAnonymousClient()
            .PutAsync("/api/drives/b!test-drive/upload?fileName=f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "PUT /api/drives/{driveId}/upload was retired by task 083 — it wrote the request body into a "
            + "caller-named drive as the managed identity. A 401 here means the route was re-added.");
    }

    [Fact]
    public async Task RetiredDriveKeyedUploadRoute_WithValidBearer_Returns404AndNeverReachesTheWrite()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAuthenticatedClient()
            .PutAsync("/api/drives/b!test-drive/upload?fileName=f.txt", content);

        // An AUTHENTICATED caller is the one that mattered: under the old route this request reached
        // speFileStore.UploadSmallAsync and the bytes landed. 404 proves there is no longer a handler to
        // reach — the write did not happen because it cannot.
        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "an authenticated caller must not reach this retired write path either. Anything other than "
            + "404 — including 403 — means a handler exists again, and a 403 raised after the upload was "
            + "issued would not be a denial at all.");
    }

    [Fact]
    public async Task RetiredDriveKeyedDeleteRoute_WithoutBearer_Returns404NotRouted()
    {
        var response = await CreateAnonymousClient()
            .DeleteAsync("/api/drives/b!test-drive/items/test-item");

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "DELETE /api/drives/{driveId}/items/{itemId} was retired by task 083. This is the DESTROY "
            + "half, and the one whose caller path was actually reachable in code — it read driveId and "
            + "itemId off form attributes and so depended on no deleted route. A 401 here means the "
            + "route was re-added.");
    }

    [Fact]
    public async Task RetiredDriveKeyedDeleteRoute_WithValidBearer_Returns404AndNeverReachesTheDestroy()
    {
        var response = await CreateAuthenticatedClient()
            .DeleteAsync("/api/drives/b!test-drive/items/test-item");

        // A destroy is the worst case for a wrong-resource-domain decision: unlike a misplaced write,
        // there is not even a record left to audit afterwards. So this assertion is about reachability,
        // not about the answer the route would have given.
        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "an authenticated caller must not reach this retired destroy path. Anything other than 404 "
            + "means a handler exists again — and for a delete, a 403 raised after DeleteFileAsync was "
            + "issued is not a denial, it is a report about something already gone.");
    }

    // =============================================================================================
    // POSITIVE CONTROLS — prove the 404s above mean "route absent", not "fixture 404s everything"
    // =============================================================================================

    /// <summary>
    /// Without these controls, a fixture change that made every request 404 would silently turn every
    /// absence assertion above into a vacuous pass. Task 060 proved this is not hypothetical: its
    /// positive controls caught two real defects.
    ///
    /// <para>Both name <c>PUT /api/obo/me/files/{*path}</c> — the record-LESS OBO upload, which is
    /// mapped, carries <c>RequireAuthorization()</c>, and accepts no container or drive parameter. It is
    /// the same control the sibling <c>MiContainerKeyedWriteRouteRetirementTests</c> uses, deliberately:
    /// a control route shared by both retirement guards has one place to be re-pointed if it is ever
    /// itself retired, rather than two that can drift apart.</para>
    /// </summary>
    [Fact]
    public async Task SurvivingOboUploadRoute_WithoutBearer_Returns401NotFound()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAnonymousClient()
            .PutAsync("/api/obo/me/files/f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "PUT /api/obo/me/files/{*path} is mapped and requires authorization. If this returns 404 the "
            + "route was removed — AND every absence assertion in this file has become vacuous, because "
            + "a fixture that 404s everything would look identical.");
    }

    [Fact]
    public async Task SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAuthenticatedClient()
            .PutAsync("/api/obo/me/files/f.txt", content);

        // Deliberately asserts NOT-404 rather than a specific code: the authorized path's status depends
        // on Graph/Dataverse behaviour the fixture does not fully stand up. What must stay true is that
        // the route is ROUTED, which is what makes the retired routes' 404s meaningful.
        response.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            "the surviving OBO upload route must still be routed for an authenticated caller. A 404 here "
            + "means this file's authenticated-404 assertions prove nothing.");
    }
}
