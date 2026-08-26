using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Authentication;

/// <summary>
/// <c>tests/integration/tenant/**</c> — tenant-isolation KEEP category (ADR-038 §2 path #4).
/// Pins the ADR-014 / ADR-015 invariant that a caller cannot NAME its own tenant
/// (<c>spaarkeai-compose-r8</c> task 059).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three mechanisms, not one.</b> Task 059 was filed against the <c>X-Tenant-Id</c> header tier.
/// Enumerating before modifying found 19 sites across THREE mechanisms: the header (16 sites), a
/// second header name on the Precedent admin route, and a <c>?tenantId=</c> query string on the two
/// visualization routes.
/// </para>
/// <para>
/// <b>And the filed one was the LESS severe.</b> The header sat at the end of a <c>??</c> chain, so
/// it was only ever consulted when the principal carried NO <c>tid</c> claim in either form. A
/// caller holding a valid Entra token in tenant A could not use it to become tenant B — tier 1
/// short-circuited first. The header defect is therefore LATENT: it needs a claim-less authenticated
/// principal, which the codebase does mint (<c>ApiKeyAuthenticationHandler</c>) but only on a route
/// that never read this tier. The query-string mechanism had no such guard —
/// <see cref="VisualizationEndpoints.GetRelatedDocuments"/> read <c>?tenantId=</c> and consulted no
/// claim at all, and <see cref="VisualizationEndpoints.IndexTemporaryContent"/> let the query string
/// OUTRANK the claim. Those two were live cross-tenant read and write for any authenticated user.
/// Both classes are covered below; see
/// <c>projects/spaarkeai-compose-r8/notes/059-tenant-header-decisions.md</c> for the full
/// enumeration and the per-caller decisions.
/// </para>
/// <para>
/// <b>Why these are reachability tests, not string tests.</b> The directory README is explicit: a
/// test here must fail when the boundary is actually crossed. Asserting that a resolver returns
/// tenant A stays green through the exact refactor that reintroduces the hole elsewhere. So each
/// endpoint test takes a caller authenticated in tenant A, points them at tenant B, and then asserts
/// on tenant B — a session that must still exist, a service call that must never have been made.
/// </para>
/// <para>
/// <b>Observed to fail before the fix.</b> Against pre-059 code, 4 of these failed and the failure
/// output is recorded in the notes file §5. Note honestly which ones did NOT:
/// <see cref="Delete_WithATenantClaim_IgnoresASpoofedHeader"/> passed pre-fix, because the claim
/// already won. It is kept as a precedence guard, not claimed as proof of the fix.
/// </para>
/// </remarks>
[Trait("category", "tenant-isolation")]
public sealed class TenantSelectionByRequestTests
{
    private const string TenantA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string TenantB = "bbbbbbbb-5555-6666-7777-888888888888";

    private static ChatSessionManager BuildManager()
        => new(
            cache: new InMemoryTenantCache(),
            dataverseRepository: new CapturingChatDataverseRepository(),
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null);

    /// <summary>A caller genuinely authenticated in <paramref name="tenantId"/>.</summary>
    private static ClaimsPrincipal PrincipalIn(string tenantId) =>
        new(new ClaimsIdentity(
            [new Claim("tid", tenantId), new Claim("oid", "11111111-1111-1111-1111-111111111111")],
            authenticationType: "Test"));

    /// <summary>
    /// Authenticated, but carrying no tenant claim in either form — the shape
    /// <see cref="ApiKeyAuthenticationHandler"/> mints, and the only shape that ever reached the
    /// <c>X-Tenant-Id</c> tier.
    /// </summary>
    private static ClaimsPrincipal PrincipalWithNoTenantClaim() =>
        new(new ClaimsIdentity(
            [new Claim("oid", "22222222-2222-2222-2222-222222222222")], authenticationType: "Test"));

    // ─────────────────────────────────────────────────────────────────────────
    // Mechanism 1 — the X-Tenant-Id header (latent: reachable only without a tid claim)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithoutAnyTenantClaim_CannotReachAnotherTenantsSessionViaTheHeader()
    {
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(TenantB, documentId: null);

        var httpContext = new DefaultHttpContext { User = PrincipalWithNoTenantClaim() };
        httpContext.Request.Headers["X-Tenant-Id"] = TenantB;

        var result = await ChatEndpoints.DeleteSessionAsync(
            victim.SessionId, manager, httpContext,
            NullLogger<ChatSessionManager>.Instance, CancellationToken.None);

        // The reachability assertion — stated about tenant B's data, so it cannot be satisfied by a
        // resolver that merely *reports* something else.
        (await manager.GetSessionAsync(TenantB, victim.SessionId)).Should().NotBeNull(
            "a principal with no tenant claim must not be able to erase tenant B's session — and its " +
            "durable 90-day file bytes with it — by naming tenant B in a request header");

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(400,
                "a principal with no tenant claim has no tenant: the request must fail rather than " +
                "adopt the tenant the caller supplied");
    }

    [Fact]
    public async Task Delete_WithATenantClaim_IgnoresASpoofedHeader()
    {
        // Precedence guard. This PASSED before the fix too — the `??` chain consulted the header only
        // when both claim forms were absent. Kept because "the claim wins" is the property that made
        // the header defect latent rather than live, and a future refactor that reorders the chain
        // would turn it live again with nothing else to catch it.
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(TenantB, documentId: null);

        var httpContext = new DefaultHttpContext { User = PrincipalIn(TenantA) };
        httpContext.Request.Headers["X-Tenant-Id"] = TenantB;

        var result = await ChatEndpoints.DeleteSessionAsync(
            victim.SessionId, manager, httpContext,
            NullLogger<ChatSessionManager>.Instance, CancellationToken.None);

        (await manager.GetSessionAsync(TenantB, victim.SessionId)).Should().NotBeNull(
            "tenant A's caller must not reach tenant B's session");

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(404,
                "the session does not exist in the caller's own tenant, and a cross-tenant miss must " +
                "be a 404 rather than a 403 — a 403 confirms the session exists somewhere else");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mechanism 2 — the ?tenantId= query string (LIVE before the fix)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_WithSpoofedTenantQueryString_IndexesIntoTheCallersOwnTenant()
    {
        var service = new Mock<IVisualizationService>();
        var httpContext = new DefaultHttpContext { User = PrincipalIn(TenantA) };
        httpContext.Request.QueryString = new QueryString($"?tenantId={TenantB}");
        httpContext.Request.ContentType = "multipart/form-data; boundary=----test";
        httpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection
            {
                new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 0, 5, "file", "a.txt")
                {
                    Headers = new HeaderDictionary(),
                },
            });

        await VisualizationEndpoints.IndexTemporaryContent(
            httpContext, service.Object, NullLogger<Program>.Instance, CancellationToken.None);

        service.Verify(
            s => s.IndexTemporaryContentAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), TenantB, It.IsAny<CancellationToken>()),
            Times.Never,
            "content uploaded by a tenant-A caller must never be indexed into tenant B's search " +
            "partition, however the caller spells the tenant in the request");

        service.Verify(
            s => s.IndexTemporaryContentAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), TenantA, It.IsAny<CancellationToken>()),
            Times.Once,
            "and the upload must still succeed — into the caller's own tenant");
    }

    [Fact]
    public async Task Related_WithSpoofedTenantQueryString_ReadsOnlyTheCallersOwnTenant()
    {
        var service = new Mock<IVisualizationService>();
        service
            .Setup(s => s.GetRelatedDocumentsAsync(
                It.IsAny<Guid>(), It.IsAny<VisualizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentGraphResponse());

        var httpContext = new DefaultHttpContext { User = PrincipalIn(TenantA) };
        httpContext.Request.QueryString = new QueryString($"?tenantId={TenantB}");

        await VisualizationEndpoints.GetRelatedDocuments(
            Guid.NewGuid(),
            new VisualizationQueryParameters(),
            httpContext,
            service.Object,
            NullLogger<Program>.Instance,
            CancellationToken.None);

        service.Verify(
            s => s.GetRelatedDocumentsAsync(
                It.IsAny<Guid>(),
                It.Is<VisualizationOptions>(o => o.TenantId == TenantB),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "before task 059 this route took its tenant from ?tenantId= and consulted no claim at " +
            "all, so any authenticated user could read any tenant's document-relationship graph by " +
            "editing the URL");

        service.Verify(
            s => s.GetRelatedDocumentsAsync(
                It.IsAny<Guid>(),
                It.Is<VisualizationOptions>(o => o.TenantId == TenantA),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the graph must still be returned — scoped to the caller's own tenant");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The resolver itself
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tid")]
    [InlineData("http://schemas.microsoft.com/identity/claims/tenantid")]
    public void ResolveTenantId_AcceptsBothClaimForms(string claimType)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, TenantA)], "Test"));
        TenantResolution.ResolveTenantId(user).Should().Be(TenantA);
    }

    [Fact]
    public void ResolveTenantId_TreatsAWhitespaceClaimAsAbsent_AndFallsThroughToTheLongForm()
    {
        // The `??` chain this replaced fell through only on null, so a whitespace `tid` short-circuited
        // the long form and produced a blank tenant at the call site.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tid", "   "),
                new Claim("http://schemas.microsoft.com/identity/claims/tenantid", TenantA),
            ], "Test"));

        TenantResolution.ResolveTenantId(user).Should().Be(TenantA);
    }

    [Fact]
    public void ResolveTenantId_WithNoTenantClaim_IsNull() =>
        TenantResolution.ResolveTenantId(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "x")], "Test")))
            .Should().BeNull();

    // ─────────────────────────────────────────────────────────────────────────
    // Tripwire
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reachability tests above prove the boundary holds on the routes they exercise. They cannot
    /// prove it holds on the other 16 sites that resolved tenant inline, nor on the 20th someone adds
    /// next quarter — and "copy the resolution block from the endpoint next door" is precisely how
    /// this reached 19 sites. So this asserts the structural property instead: nothing in the BFF
    /// reads a tenant out of the request. <see cref="TenantResolution"/> takes a
    /// <see cref="ClaimsPrincipal"/> and has no access to an <c>HttpContext</c>, so the only route
    /// back to the old behaviour is a fresh <c>Request.Headers[...]</c> read — which is what this
    /// catches. (The query-string mechanism is closed differently and needs no scan: the bound
    /// property was DELETED, so reading it again is a compile error.)
    /// </summary>
    [Fact]
    public void NoBffSourceFileResolvesTenantFromTheRequest()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "src", "server", "api", "Sprk.Bff.Api");
        Directory.Exists(apiRoot).Should().BeTrue("the BFF source tree must be locatable from the test assembly");

        // Two shapes, both matched structurally rather than by name — renaming the header constant
        // (X-Tenant-Id -> X-Spaarke-Tenant-Id -> …) or the query key must not slip past.
        //
        // The query-string arm is not hypothetical padding: FOUR live sites were found that way
        // (both visualization routes and both Compose routes), and each one took the caller's word
        // for the tenant with no claim consulted at all. They were strictly worse than the header
        // tier this task was filed against, and none of them was in the filed scope.
        // The query arm deliberately matches "[FromQuery … tenantId" anywhere on the line, so it
        // catches BOTH forms that existed: the inline parameter (`[FromQuery] string tenantId`) and
        // the bound property whose name lives inside the attribute
        // (`[FromQuery(Name = "tenantId")]` on its own line, above the property).
        var requestTenant = new Regex(
            @"Headers\[[^\]]*[Tt]enant[^\]]*\]|\[FromQuery[^\r\n]*[Tt]enantId",
            RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, i) => (path, line, number: i + 1))
                .Where(x => requestTenant.IsMatch(x.line)))
            .Select(x => $"{Path.GetRelativePath(apiRoot, x.path)}:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "tenant identity must come from the authenticated principal via " +
            "TenantResolution.ResolveTenantId(HttpContext.User). A request-supplied tenant lets the " +
            "caller choose whose data it addresses — and since task 060 that value is the partition " +
            "key of a durable 90-day blob store, so a spoofed value misplaces bytes permanently.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}
