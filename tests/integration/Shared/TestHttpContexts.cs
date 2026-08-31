using System.Security.Claims;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Builds the <see cref="HttpContext"/> shape a real request actually has, for tests that call a
/// service directly rather than through the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (issue #863).</b> Dozens of service-level tests passed a bare
/// <c>new DefaultHttpContext()</c> — an ANONYMOUS principal with no <c>oid</c> and no <c>tid</c>.
/// No route in the BFF can produce that: every session-scoped group carries
/// <c>RequireAuthorization()</c>, so by the time a service sees an <c>HttpContext</c> it always has
/// an authenticated Entra principal. Passing an anonymous one is a fixture that models a request
/// which cannot occur.
/// </para>
/// <para>
/// That is not cosmetic. It is precisely why <c>ComposeService.LoadAsync</c> could not tell whose
/// session it was resuming: the tests it was written against never had a caller identity, so the
/// code was never written to need one. Per <c>.claude/constraints/bff-extensions.md</c> §F.2
/// (Fixture-Config-FIRST), when a test fails on a non-contract fixture value, the FIXTURE is the
/// defect — repair it here rather than relaxing the production check.
/// </para>
/// </remarks>
internal static class TestHttpContexts
{
    /// <summary>Tenant used by service-level tests that do not otherwise care about the tenant.</summary>
    public const string DefaultTenantId = "00000000-0000-0000-0000-0000000000t1";

    /// <summary>
    /// An authenticated context for <paramref name="oid"/> — the claim shape Entra issues under
    /// inbound claim-type mapping: the long schema URI for the object id (see
    /// <c>CallerResolution</c> for why the short <c>oid</c> form does not exist under mapping, and
    /// why both are emitted here so either resolution path works).
    /// </summary>
    public static DefaultHttpContext Authenticated(
        string? oid = null,
        string? tenantId = null)
    {
        var resolvedOid = oid ?? TestSessionOwner.Oid;
        var claims = new List<Claim>
        {
            new("oid", resolvedOid),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", resolvedOid),
            new("tid", tenantId ?? DefaultTenantId),
        };

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
    }
}
