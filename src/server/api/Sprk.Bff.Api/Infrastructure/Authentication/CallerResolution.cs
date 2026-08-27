using System.Security.Claims;

namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// The single place the BFF answers "which Entra object id is this caller?".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> <see cref="Spaarke.Dataverse.IAccessDataSource"/> maps the caller to a
/// Dataverse <c>systemuser</c> row by querying <c>azureactivedirectoryobjectid eq '{id}'</c>. That column
/// holds the Entra <b>object id</b> (<c>oid</c>) — a tenant-stable GUID. Any other identifier zero-matches,
/// and a zero match is indistinguishable from "no rights": it collapses to
/// <c>AccessRights.None</c> and the caller is denied. So the identifier handed to authorization is not a
/// free choice; it MUST be <c>oid</c>.
/// </para>
/// <para>
/// <b>The trap this closes (UAT 2026-08-26, D-6).</b> Three authorization filters read
/// <see cref="ClaimTypes.NameIdentifier"/> directly. With inbound claim-type mapping left on — the default,
/// and what this app runs — .NET's <c>DefaultInboundClaimTypeMap</c> routes the token's <c>sub</c> to
/// <see cref="ClaimTypes.NameIdentifier"/> and its <c>oid</c> to the long-form
/// <see cref="ObjectIdSchemaClaim"/>. Entra's <c>sub</c> is a <i>pairwise</i>, non-GUID identifier: it
/// differs per application and can never equal an <c>oid</c>. Those filters therefore denied
/// <b>every</b> caller on <b>every</b> route they gate — an unconditional 403 that read as a permission
/// problem. Same class of hazard, same mitigation, as the <c>TenantResolution</c> chokepoint for
/// <c>tid</c> (landing separately with spaarkeai-compose-r8) — named here as a pattern, not a
/// code reference, because that type is not on master yet.
/// </para>
/// <para>
/// <b>Why the test suite could not catch it.</b> The auth fixtures issue <c>oid</c> and
/// <see cref="ClaimTypes.NameIdentifier"/> as the <i>same constant</i>, so the two are interchangeable in
/// tests and never are in production. A regression test MUST give them DIVERGENT values, or it re-creates
/// the blind spot rather than covering it.
/// </para>
/// <para>
/// <b>On the <see cref="ClaimTypes.NameIdentifier"/> tail.</b> It is kept deliberately, byte-identical to
/// the ~15 call sites across the BFF that already resolve in this order, so behaviour is uniform. It is a
/// compatibility affordance for non-Entra principals (test fixtures, named API-key schemes), NOT a
/// fallback that can rescue a real Entra caller: any principal that reaches it in production yields a
/// <c>sub</c>-shaped value that will not match a <c>systemuser</c>. Order is the whole contract — both
/// <c>oid</c> forms must be consulted first.
/// </para>
/// <para>
/// <b>What a null return means.</b> The caller carries no usable identity claim. Call sites MUST answer
/// <c>401 Unauthorized</c>, never <c>403 Forbidden</c>: a caller who cannot be identified has not been
/// found to lack permission.
/// </para>
/// <para>
/// Do NOT add an <c>HttpContext</c> overload. This type takes a <see cref="ClaimsPrincipal"/> so that it
/// <i>cannot</i> read an identity out of a header, route value or body. An overload taking the request
/// would restore exactly the reachability this type exists to remove.
/// </para>
/// </remarks>
public static class CallerResolution
{
    /// <summary>The Entra v2.0 object-id claim — the tenant-stable GUID Dataverse matches on.</summary>
    public const string ObjectIdClaim = "oid";

    /// <summary>
    /// The WS-Federation-style long-form of the same claim. Emitted instead of <see cref="ObjectIdClaim"/>
    /// when inbound claim-type mapping is left on, so both forms must be accepted.
    /// </summary>
    public const string ObjectIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Resolves the caller's Entra object id from their authenticated claims, or <see langword="null"/>
    /// when the principal carries no usable identity claim in any accepted form.
    /// </summary>
    /// <param name="user">
    /// The authenticated principal — normally <c>HttpContext.User</c>. Pass the principal, never the
    /// request; see the type-level remarks for why the request is deliberately out of reach.
    /// </param>
    public static string? ResolveObjectId(ClaimsPrincipal? user)
    {
        if (user is null) return null;

        return Normalize(user.FindFirst(ObjectIdClaim)?.Value)
            ?? Normalize(user.FindFirst(ObjectIdSchemaClaim)?.Value)
            ?? Normalize(user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
