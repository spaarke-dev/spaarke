using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.UnifiedAccessControl;

/// <summary>
/// Pins the identifier the authorization filters hand to <see cref="IAccessDataSource"/>.
///
/// <para><b>Why this file exists.</b> On 2026-08-26 every document request in dev returned 403 while
/// 11,932 tests stayed green. <c>DocumentAuthorizationFilter</c> read
/// <see cref="ClaimTypes.NameIdentifier"/> and passed it to a Dataverse lookup keyed on
/// <c>systemuser.azureactivedirectoryobjectid</c>. With inbound claim-type mapping left on (the default,
/// and what this app runs) .NET routes the token's <c>sub</c> to <see cref="ClaimTypes.NameIdentifier"/>
/// and its <c>oid</c> to the long-form schema claim. Entra's <c>sub</c> is a <i>pairwise</i>, non-GUID
/// identifier, so the lookup matched nothing — and a zero match is indistinguishable from "no rights".
/// Confirmed from production logs:</para>
/// <code>
///   sub  d12L59FR…rkjg   → AccessRights: None → DENIED
///   oid  c74ac1af-…      → RetrievePrincipalAccess SUCCESS, GrantedAccess=Read,Write,Delete,…
/// </code>
///
/// <para><b>Why the existing suite could not see it — read this before editing any auth fixture.</b>
/// The UAC fixtures issue <c>oid</c> and <see cref="ClaimTypes.NameIdentifier"/> as the SAME constant
/// (<c>DocumentDestroyAuthorizationTestFixture</c>). When the two claims are interchangeable, reading
/// either one passes, so no test in the suite could distinguish correct from broken. That is why this
/// file's principals give them <b>DIVERGENT</b> values, and why the assertion is on <i>which identifier
/// was resolved</i> rather than merely on the resulting status code: a test that only asserts "access
/// granted" re-collapses the moment a fixture regresses, and would have passed against the broken code.</para>
///
/// <para>The divergent-claims pattern is not new — <c>CommunicationCreateRecordThreadContractTests</c>
/// already does it. This is an inconsistency to close across the auth fixtures, not a convention to
/// invent.</para>
/// </summary>
public sealed class CallerObjectIdResolutionTests
{
    // Deliberately unlike each other, and shaped like the real thing: `oid` is a GUID (what Dataverse
    // stores in azureactivedirectoryobjectid); `sub` is Entra's pairwise base64url identifier.
    private const string Oid = "c74ac1af-ff3b-46fb-83e7-3063616e959c";
    private const string Sub = "d12L59FRq8kZ0m2Xr7bTn4wPqLzYhVcJ8sNdEuRkjg";
    private const string DocumentId = "1d761626-b8a1-f111-aaad-7ced8ddc4a05";

    private const string OidSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    // ── CallerResolution: precedence, in isolation ────────────────────────────────────────────────

    [Fact]
    public void ResolveObjectId_PrefersOid_WhenNameIdentifierAlsoPresent()
    {
        var user = Principal(
            new Claim("oid", Oid),
            new Claim(ClaimTypes.NameIdentifier, Sub));

        CallerResolution.ResolveObjectId(user).Should().Be(Oid,
            "NameIdentifier carries `sub` under inbound claim mapping and must never win over `oid`");
    }

    [Fact]
    public void ResolveObjectId_AcceptsTheLongFormOidClaim()
    {
        // Inbound claim mapping rewrites `oid` to this schema URI, so the short form may be absent.
        var user = Principal(
            new Claim(OidSchemaClaim, Oid),
            new Claim(ClaimTypes.NameIdentifier, Sub));

        CallerResolution.ResolveObjectId(user).Should().Be(Oid);
    }

    [Fact]
    public void ResolveObjectId_NEVER_FallsBackToNameIdentifier()
    {
        // THE structural assertion. An earlier version ended this chain with a NameIdentifier tail —
        // the OFFICE_009 pattern, where a correct source is placed in front of a broken read and the
        // broken read is left in place. A fallback does not remove a wrong approach, it RANKS it, and
        // silent fall-through is the defect itself. If this test ever fails, someone re-added the tail.
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, Sub));

        CallerResolution.ResolveObjectId(user).Should().BeNull(
            "NameIdentifier carries `sub`, which can never match a systemuser — resolving it here would "
            + "hand Dataverse an identifier guaranteed to fail, which is the original defect");
    }

    [Fact]
    public void ResolveObjectId_ReturnsNull_WhenNoIdentityClaimAtAll()
    {
        CallerResolution.ResolveObjectId(Principal(new Claim("tid", "t"))).Should().BeNull(
            "call sites must answer 401 for an unidentifiable caller — never 403");
    }

    [Fact]
    public void ResolveObjectIdGuid_ParsesTheOid_AndRejectsASub()
    {
        // Several sites hand-rolled Guid.TryParse over a value that could never parse — which silently
        // skipped the work it guarded (a membership event never published) or 401'd every caller.
        CallerResolution.ResolveObjectIdGuid(Principal(new Claim("oid", Oid)))
            .Should().Be(Guid.Parse(Oid));

        CallerResolution.ResolveObjectIdGuid(Principal(new Claim(ClaimTypes.NameIdentifier, Sub)))
            .Should().BeNull("a pairwise `sub` is not a GUID and must not be coerced into one");
    }

    // ── ResolveOpaqueCallerKey: the one place `sub` is CORRECT ────────────────────────────────────

    [Fact]
    public void ResolveOpaqueCallerKey_PrefersOid_ButAcceptsSub()
    {
        // A rate-limit partition / idempotency scope / cache key legitimately accepts `sub`: it is
        // stable per (user, application), which is exactly what partitioning needs. The separate name
        // is the point — misuse should be visible in review rather than hidden in a chain.
        CallerResolution.ResolveOpaqueCallerKey(Principal(
            new Claim("oid", Oid), new Claim(ClaimTypes.NameIdentifier, Sub)))
            .Should().Be(Oid, "prefer the stable id when it exists");

        CallerResolution.ResolveOpaqueCallerKey(Principal(new Claim(ClaimTypes.NameIdentifier, Sub)))
            .Should().Be(Sub, "unlike ResolveObjectId, falling back here is correct by design");
    }

    // ── The filter: what identifier actually reaches IAccessDataSource ────────────────────────────

    [Fact]
    public async Task DocumentAuthorizationFilter_ResolvesTheOid_NotTheSub()
    {
        var capture = new CapturingAccessDataSource(AccessRights.Read);
        var filter = BuildFilter(capture, "read");
        var context = InvocationContext(Principal(
            new Claim("oid", Oid),
            new Claim(ClaimTypes.NameIdentifier, Sub)));

        var nextCalled = false;
        await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        // THE assertion. Status code alone is not enough: with a fixture whose oid == sub, "allowed"
        // passes against the broken code too. This pins the identifier itself.
        capture.ObservedUserId.Should().Be(Oid,
            "the filter must ask IAccessDataSource about the caller's Entra OBJECT ID — passing `sub` "
            + "matches no systemuser and denies every caller on every route this filter gates");
        capture.ObservedUserId.Should().NotBe(Sub);
        nextCalled.Should().BeTrue("a caller with Read must reach the handler");
    }

    [Fact]
    public async Task DocumentAuthorizationFilter_Returns401_NotForbidden_WhenCallerCannotBeIdentified()
    {
        // An unidentifiable caller has not been FOUND to lack permission. Reporting 403 here would be
        // the same category error the original defect made visible.
        var capture = new CapturingAccessDataSource(AccessRights.Read);
        var filter = BuildFilter(capture, "read");
        var context = InvocationContext(Principal(new Claim("tid", "t")));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        StatusOf(result).Should().Be(StatusCodes.Status401Unauthorized);
        capture.ObservedUserId.Should().BeNull("authorization must not be consulted for an unknown caller");
    }

    [Fact]
    public async Task DocumentAuthorizationFilter_StillDenies_WhenTheOidGenuinelyLacksRights()
    {
        // Guards the opposite failure: the fix must not turn the filter into a rubber stamp. Resolving
        // the RIGHT caller and getting "no" must still be a 403.
        var capture = new CapturingAccessDataSource(AccessRights.None);
        var filter = BuildFilter(capture, "read");
        var context = InvocationContext(Principal(
            new Claim("oid", Oid),
            new Claim(ClaimTypes.NameIdentifier, Sub)));

        var nextCalled = false;
        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        capture.ObservedUserId.Should().Be(Oid, "it must still have asked about the right caller");
        nextCalled.Should().BeFalse();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestScheme"));

    private static DocumentAuthorizationFilter BuildFilter(IAccessDataSource source, string operation)
    {
        var authService = new AuthorizationService(
            source,
            new IAuthorizationRule[] { new OperationAccessRule(NullLogger<OperationAccessRule>.Instance) },
            NullLogger<AuthorizationService>.Instance);
        return new DocumentAuthorizationFilter(authService, operation);
    }

    private static EndpointFilterInvocationContext InvocationContext(ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        http.Request.RouteValues["documentId"] = DocumentId;
        // REQUIRED, not incidental: AuthorizationService fails closed when the caller token is absent
        // (finding A-2 — a caller-scoped evaluation must never degrade to app-only). Without this the
        // filter denies before IAccessDataSource is ever consulted, and the assertions below would be
        // measuring the wrong guard.
        http.Request.Headers.Authorization = "Bearer test-caller-token";
        return EndpointFilterInvocationContext.Create(http);
    }

    private static int? StatusOf(object? result) => result switch
    {
        IStatusCodeHttpResult s => s.StatusCode,
        _ => null,
    };

    /// <summary>
    /// Records the <c>userId</c> the filter passes through <see cref="AuthorizationService"/>. This is
    /// the seam the defect lived at, so it is the seam the test observes — not the HTTP status.
    /// </summary>
    private sealed class CapturingAccessDataSource(AccessRights rights) : IAccessDataSource
    {
        public string? ObservedUserId { get; private set; }

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
            ObservedUserId = userId;
            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = rights,
            });
        }
    }
}
