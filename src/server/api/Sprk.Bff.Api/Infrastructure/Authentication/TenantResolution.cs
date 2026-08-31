using System.Security.Claims;

namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// The single place the BFF answers "which tenant is this caller in?".
/// Tenant identity is derived from the authenticated principal and from nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type takes a <see cref="ClaimsPrincipal"/> and not an <c>HttpContext</c>.</b>
/// Before <c>spaarkeai-compose-r8</c> task 059 the BFF resolved tenant as
/// <c>tid claim ?? schema-URI claim ?? X-Tenant-Id header</c>, copy-pasted inline at 16 call
/// sites, plus a query-string variant and a second header name. The header tier let any caller
/// that could reach an endpoint name its own tenant. A rule saying "do not read the header" would
/// have to be remembered at every future call site; a signature that has no access to the request
/// cannot read a header at all. That is the enforcement mechanism here, and it is deliberate —
/// the same idiom ADR-049 invariant 7 uses to keep document text out of the edit-placement path.
/// Do NOT add an <c>HttpContext</c> overload: it would restore exactly the reachability this type
/// exists to remove.
/// </para>
/// <para>
/// <b>Why removing the header tier broke no caller.</b> Task 059 enumerated every sender before
/// changing resolution. All six production senders sit behind one function
/// (<c>useSseStream.ts readSseStream</c>), which derives the header by base64-decoding the
/// <c>tid</c> claim out of the very token it puts in the same request's <c>Authorization</c>
/// header — so tier 1 reads the identical value from a source the caller cannot forge. The only
/// principal in the system that genuinely carries no <c>tid</c> (the <c>RagApiKey</c> scheme,
/// <see cref="ApiKeyAuthenticationHandler"/>) never consumed this tier: its one endpoint takes the
/// tenant from the request body. Evidence:
/// <c>projects/spaarkeai-compose-r8/notes/059-tenant-header-decisions.md</c>.
/// </para>
/// <para>
/// <b>What a null return means.</b> The caller is authenticated but carries no tenant claim.
/// Every call site MUST fail the request rather than substituting a default — a tenant that
/// cannot be established is not a tenant that can be guessed.
/// </para>
/// </remarks>
public static class TenantResolution
{
    /// <summary>The Entra v2.0 tenant claim.</summary>
    public const string TenantIdClaim = "tid";

    /// <summary>
    /// The WS-Federation-style long-form of the same claim. Emitted instead of <see cref="TenantIdClaim"/>
    /// when inbound claim-type mapping is left on, so both forms must be accepted.
    /// </summary>
    public const string TenantIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>
    /// Resolves the caller's tenant from their authenticated claims, or <see langword="null"/> when the
    /// principal carries no tenant claim in either form.
    /// </summary>
    /// <param name="user">
    /// The authenticated principal — normally <c>HttpContext.User</c>. Pass the principal, never the
    /// request; see the type-level remarks for why the request is deliberately out of reach.
    /// </param>
    public static string? ResolveTenantId(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        // Whitespace is treated as absent so an empty claim falls through to the long form rather
        // than short-circuiting it — the `??` chain this replaces only fell through on null.
        return Normalize(user.FindFirst(TenantIdClaim)?.Value)
            ?? Normalize(user.FindFirst(TenantIdSchemaClaim)?.Value);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
