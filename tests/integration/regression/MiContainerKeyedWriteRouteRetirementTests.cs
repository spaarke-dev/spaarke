using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 073 (Phase 0c Secure Documents, Wave 1).
///
/// <para>Task 073 DELETED <c>Api/UploadEndpoints.cs</c> and with it three app-only
/// (managed-identity) write routes:</para>
/// <list type="bullet">
///   <item><c>PUT /api/containers/{containerId}/files/{*path}</c></item>
///   <item><c>POST /api/containers/{containerId}/upload</c></item>
///   <item><c>PUT /api/upload-session/chunk</c></item>
/// </list>
///
/// <para><b>What was wrong.</b> Each took an SPE key straight off the route and wrote as the
/// MANAGED IDENTITY — <c>SpeFileStore.UploadSmallAsync</c> / <c>CreateUploadSessionAsync</c> take no
/// caller context and land on <c>UploadSessionManager</c>'s <c>_factory.ForApp()</c>. So unlike the
/// OBO routes, SPE performed NO caller-side check: the write proceeded regardless of the caller's
/// container permission. Their only gate was
/// <c>RequireAuthorization("canwritefiles")</c> → <c>ResourceAccessRequirement</c> →
/// <c>ResourceAccessHandler</c>, which is real and fail-closed but resolves DOCUMENT rights from a
/// CONTAINER id (<c>ExtractResourceId</c> accepts containerId / driveId / documentId / id
/// interchangeably). A real mechanism pointed at the wrong resource domain — finding #4.</para>
///
/// <para><b>Two aggravations found while retiring them</b>, neither of which the task anticipated:</para>
/// <list type="number">
///   <item><c>PUT /api/containers/{containerId}/files/{*path}</c> passed its route
///   <c>containerId</c> into <c>UploadSmallAsync</c>'s <c>driveId</c> parameter, which reaches
///   <c>graphClient.Drives[driveId]</c>. The blast radius was therefore any DRIVE the managed
///   identity could address, not merely any SPE container.</item>
///   <item><c>POST /api/containers/{containerId}/upload</c> returned
///   <c>UploadSessionDto(string UploadUrl, …)</c> — a Graph PRE-AUTHENTICATED upload URL. That is a
///   bearer-free write credential: its holder can PUT bytes to the drive with no token at all, and
///   outside the BFF entirely, until it expires. Minting one app-only for a caller-named container
///   is strictly worse than the single unauthorized write of the route above.</item>
/// </list>
///
/// <para><b>Why RETIRED and not GATED.</b> A repo-wide caller sweep found ZERO callers of all three.
/// (Historical, as of task 073.) Every live upload flow then used the OBO sibling
/// <c>PUT /api/obo/containers/{id}/files/{*path}</c> — 11
/// call sites via <c>EntityCreationService.ts:493</c>, <c>Spaarke.SdapClient</c>
/// <c>UploadOperation.ts:27</c>, and <c>document-upload/SdapApiClient.ts:101</c>. Gating instead would
/// have required a container→owning-record mapping, which tasks 075/076 own; building one here would
/// have produced the second copy that task 075's constraints explicitly forbid. Deletion is remedy #2
/// in <c>RouteAuthorizationGuardTests</c>' own remedy list and follows task 071's precedent.
/// <c>PUT /api/upload-session/chunk</c> was additionally a STUB that fabricated
/// <c>NextExpectedRanges</c> and never called Graph, so a client trusting it reported success while
/// writing nothing — and it logged the pre-authenticated session URL at information level.</para>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> someone re-adds one of these routes — most
/// plausibly while "restoring" upload support after reading one of the ~45 stale doc mentions that
/// still describe <c>PUT /api/containers/{id}/files/{path}</c> as the upload endpoint (e.g.
/// <c>src/server/api/Sprk.Bff.Api/docs/SPE.BFF.API-TECHNICAL-OVERVIEW.md:689</c>,
/// <c>SDAP-CLIENT-V2-PACKAGE-OVERVIEW.md:140</c>) — and an unauthorized app-only write path silently
/// returns. Nothing else in the suite would notice, because a re-added route would simply start
/// answering.</para>
///
/// <para><b>Why the assertions are shaped this way.</b> A status code alone is the wrong bar for a
/// write route: a 403 returned AFTER the upload was issued is not a denial. So the load-bearing
/// assertion here is <see cref="RetiredMiWriteRoutes_AreAbsentFromTheEndpointTable"/>, which
/// enumerates <c>EndpointDataSource</c> and proves no handler exists to reach the write at all —
/// unfakeable by any fixture or status-code mapping. The HTTP assertions add the behavioural half:
/// 404 without a bearer proves absence rather than rejection (ASP.NET Core routes BEFORE it
/// authorizes, so a route that EXISTS and carries RequireAuthorization answers 401, not 404), and 404
/// WITH a bearer proves the 404 is not itself an authentication artifact.</para>
/// </summary>
[Trait("status", "repaired")]
public class MiContainerKeyedWriteRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    /// <summary>
    /// The three retired route patterns, exactly as they were registered in the deleted
    /// <c>Api/UploadEndpoints.cs</c>. Compared against the live endpoint table.
    /// </summary>
    private static readonly (string Verb, string Pattern)[] RetiredRoutes =
    {
        ("PUT", "/api/containers/{containerId}/files/{*path}"),
        ("POST", "/api/containers/{containerId}/upload"),
        ("PUT", "/api/upload-session/chunk"),
    };

    public MiContainerKeyedWriteRouteRetirementTests(CustomWebAppFactory factory)
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
    // THE LOAD-BEARING ASSERTION — the write path is unreachable, not merely refused
    // =============================================================================================

    [Fact]
    public void RetiredMiWriteRoutes_AreAbsentFromTheEndpointTable()
    {
        // Enumerating the composed EndpointDataSource asks the only question that actually matters for
        // a retired write route: is there a handler at all? This is strictly stronger than asserting a
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
                var verbs = match.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods
                            ?? Array.Empty<string>();

                if (verbs.Count == 0 || verbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
                {
                    survivors.Add($"{verb} {pattern}  (registered as: {match.DisplayName})");
                }
            }
        }

        survivors.Should().BeEmpty(
            "these app-only (managed-identity) write routes were RETIRED by unified-access-control-r2 "
            + "task 073 and must not be re-registered. Each wrote into a caller-named SPE container or "
            + "drive as the managed identity, so SPE applied no caller-side check, behind a policy that "
            + "resolved DOCUMENT rights from a CONTAINER id. There are no callers: the supported "
            + "user-context path is PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}, "
            + "and the supported "
            + "app-only path is the in-process SpeFileStore facade. If a genuine need for an app-only "
            + "HTTP write route reappears, it must authorize against the OWNING RECORD via the task "
            + "075/076 container resolver — not against the caller-supplied container id.\n\n"
            + "Re-registered routes:\n  " + string.Join("\n  ", survivors));
    }

    // =============================================================================================
    // BEHAVIOURAL HALF — absence over HTTP, unauthenticated and authenticated
    // =============================================================================================

    [Fact]
    public async Task RetiredMiSmallFileUploadRoute_WithoutBearer_Returns404NotRouted()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAnonymousClient()
            .PutAsync("/api/containers/test-container/files/f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "PUT /api/containers/{containerId}/files/{*path} was retired by task 073 — it wrote the "
            + "request body into a caller-named drive as the managed identity. A 401 here means the "
            + "route was re-added.");
    }

    [Fact]
    public async Task RetiredMiSmallFileUploadRoute_WithValidBearer_Returns404AndNeverReachesTheWrite()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAuthenticatedClient()
            .PutAsync("/api/containers/test-container/files/f.txt", content);

        // An AUTHENTICATED caller is the one that mattered: under the old route this request reached
        // speFileStore.UploadSmallAsync and the bytes landed. 404 proves there is no longer a handler
        // to reach — the write did not happen because it cannot.
        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "an authenticated caller must not reach this retired write path either. Anything other "
            + "than 404 — including 403 — means a handler exists again, and a 403 raised after the "
            + "upload was issued would not be a denial at all.");
    }

    [Fact]
    public async Task RetiredMiUploadSessionCreateRoute_WithValidBearer_Returns404NotRouted()
    {
        var response = await CreateAuthenticatedClient()
            .PostAsync("/api/containers/test-container/upload?path=f.txt", content: null);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "POST /api/containers/{containerId}/upload was retired by task 073 — it returned a Graph "
            + "PRE-AUTHENTICATED UploadUrl for a caller-named container, i.e. a bearer-free write "
            + "credential usable outside the BFF until expiry. This is the route whose re-addition "
            + "would be most damaging and least visible.");
    }

    [Fact]
    public async Task RetiredMiUploadChunkRoute_WithValidBearer_Returns404NotRouted()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.Add("Content-Range", "bytes 0-2/3");

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/upload-session/chunk")
        {
            Content = content,
        };
        request.Headers.Add("Upload-Session-Url", "https://example.invalid/upload-session");

        var response = await CreateAuthenticatedClient().SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "PUT /api/upload-session/chunk was retired by task 073. It carried NO resource key, so no "
            + "per-route mechanism could ever apply to it; it was also a STUB that fabricated "
            + "NextExpectedRanges without calling Graph, and it logged the caller-supplied "
            + "pre-authenticated session URL. The working equivalent is PUT /api/obo/upload-session/chunk.");
    }

    // =============================================================================================
    // POSITIVE CONTROLS — prove the 404s above mean "route absent", not "fixture 404s everything"
    // =============================================================================================

    /// <summary>
    /// Without these controls, a fixture change that made every request 404 would silently turn every
    /// absence assertion above into a vacuous pass.
    ///
    /// RE-POINTED 2026-09-03 (task 076). Both controls in this section used to name
    /// <c>PUT /api/obo/containers/{id}/files/{*path}</c> — described here as "the OBO twin the live
    /// upload flows actually call (11 wizard call sites)". That route is now DELETED: every one of
    /// those call sites moved onto contracts that name no container, and the route went with the last
    /// of them. A positive control MUST name a route that survives, so both now name
    /// <c>PUT /api/obo/me/files/{*path}</c> — the record-LESS replacement, which is mapped, carries
    /// <c>RequireAuthorization()</c>, and accepts no container parameter.
    /// </summary>
    [Fact]
    public async Task SurvivingOboUploadRoute_WithoutBearer_Returns401NotFound()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAnonymousClient()
            .PutAsync("/api/obo/me/files/f.txt", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "PUT /api/obo/me/files/{*path} is mapped and requires authorization. If this returns 404 "
            + "the route was removed — AND every absence assertion in this file has become vacuous, "
            + "because a fixture that 404s everything would look identical.");
    }

    [Fact]
    public async Task SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });

        var response = await CreateAuthenticatedClient()
            .PutAsync("/api/obo/me/files/f.txt", content);

        // Deliberately asserts NOT-404 rather than a specific code: the authorized path's status
        // depends on Graph/Dataverse behaviour the fixture does not fully stand up. What must stay
        // true is that the route is ROUTED, which is what makes the retired routes' 404s meaningful.
        response.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            "the surviving OBO upload route must still be routed for an authenticated caller. A 404 "
            + "here means this file's authenticated-404 assertions prove nothing.");
    }
}
