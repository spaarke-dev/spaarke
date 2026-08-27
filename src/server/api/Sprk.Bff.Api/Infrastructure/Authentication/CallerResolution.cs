using System.Security.Claims;

namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// The single place the BFF answers "who is this caller?" — one function per PURPOSE, never one
/// function with a fallback chain that silently ranks them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three identity spaces, and they are not interchangeable.</b>
/// </para>
/// <list type="table">
///   <item><term>Entra <c>oid</c></term><description>tenant-stable GUID. Joins to Dataverse
///     <c>systemuser.azureactivedirectoryobjectid</c>. Use for anything crossing into Dataverse.</description></item>
///   <item><term>Entra <c>sub</c></term><description><i>Pairwise</i> — unique per (user, application).
///     Joins to nothing outside this app. Legitimate ONLY as a local opaque key.</description></item>
///   <item><term>Dataverse <c>systemuserid</c></term><description>Dataverse's own PK. This is what
///     <c>ownerid</c> / <c>createdby</c> hold — NOT an Entra id.</description></item>
/// </list>
/// <para>
/// <b>The outage this type exists to prevent (UAT 2026-08-26).</b> With inbound claim-type mapping left
/// on — the default, and what this app runs — .NET renames the token's claims: <c>sub</c> becomes
/// <see cref="ClaimTypes.NameIdentifier"/> and <c>oid</c> becomes <see cref="ObjectIdSchemaClaim"/>.
/// Code that read <see cref="ClaimTypes.NameIdentifier"/> therefore handed Dataverse a pairwise
/// non-GUID identifier that can match no <c>systemuser</c>. A zero match is indistinguishable from
/// "no rights", so it denied every caller on every gated route. Production evidence:
/// </para>
/// <code>
///   sub  d12L59FR…rkjg  ->  AccessRights: None  ->  DENIED
///   oid  c74ac1af-…     ->  RetrievePrincipalAccess SUCCESS, GrantedAccess=Read,Write,Delete,…
/// </code>
/// <para>
/// <b>FOUR broken shapes — the last two look the MOST correct.</b> Recognise them; they are the reason
/// this type takes no "preferred order" parameter and offers no chain:
/// </para>
/// <code>
///   FindFirst(NameIdentifier)                       // -> sub
///   FindFirst(NameIdentifier) ?? FindFirst("oid")   // -> sub; the ?? tail is DEAD (sub always present)
///   FindFirst("oid") ?? FindFirst(NameIdentifier)   // -> sub; short "oid" DOESN'T EXIST under mapping
///   FindFirst("oid")                                // -> null
/// </code>
/// <para>
/// The third shape shipped with the comment <i>"prefer Entra 'oid' claim for stability"</i>. The intent
/// was right, the order was right, and it still resolved <c>sub</c>. That is why correctness here cannot
/// be left to per-site ordering.
/// </para>
/// <para>
/// <b>Why <see cref="ResolveObjectId"/> has NO <see cref="ClaimTypes.NameIdentifier"/> fallback.</b> An
/// earlier version of this type ended its chain with one. That is the OFFICE_009 pattern — the 2026-06
/// incident whose "fix" placed a correct source in front of the broken read and left the broken read in
/// place, which is precisely why nine latent sites survived to be rediscovered. A fallback does not
/// remove a wrong approach; it ranks it, and silent fall-through is the defect itself. The tail served
/// no real caller either: Entra principals always carry an <c>oid</c>, and
/// <c>ApiKeyAuthenticationHandler</c> mints neither claim. Its only beneficiary was the test fixtures
/// that hid the bug.
/// </para>
/// <para>
/// <b>Null means 401, never 403.</b> A caller who cannot be identified has not been found to lack
/// permission. Call sites MUST answer <c>401 Unauthorized</c> and MUST NOT substitute a default.
/// </para>
/// <para>
/// <b>Do NOT add an <c>HttpContext</c> overload.</b> These methods take a <see cref="ClaimsPrincipal"/>
/// so they <i>cannot</i> read an identity from a header, route value or body — the same structural
/// guarantee <c>TenantResolution</c> makes for <c>tid</c>.
/// </para>
/// </remarks>
public static class CallerResolution
{
    /// <summary>The Entra v2.0 object-id claim as it appears when inbound claim mapping is OFF.</summary>
    public const string ObjectIdClaim = "oid";

    /// <summary>
    /// The WS-Federation long-form of the object-id claim. This is the form that actually exists when
    /// inbound claim-type mapping is left ON (the default, and what this app runs), so it is not an
    /// "alternate" — for this application it is the primary.
    /// </summary>
    public const string ObjectIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Resolves the caller's Entra <b>object id</b> — the identifier Dataverse joins on — or
    /// <see langword="null"/> when the principal carries no object-id claim in either form.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT fall back to <see cref="ClaimTypes.NameIdentifier"/>; see the type-level
    /// remarks. A <see langword="null"/> result means the caller is unidentifiable: answer 401.
    /// </remarks>
    /// <param name="user">The authenticated principal — normally <c>HttpContext.User</c>.</param>
    public static string? ResolveObjectId(ClaimsPrincipal? user)
    {
        if (user is null) return null;

        return Normalize(user.FindFirst(ObjectIdClaim)?.Value)
            ?? Normalize(user.FindFirst(ObjectIdSchemaClaim)?.Value);
    }

    /// <summary>
    /// Resolves the caller's Entra object id as a <see cref="Guid"/>, or <see langword="null"/> when it
    /// is absent or not a GUID.
    /// </summary>
    /// <remarks>
    /// Provided because several call sites need a <see cref="Guid"/> and were hand-rolling
    /// <c>Guid.TryParse</c> over a value that could never parse — which silently skipped the work
    /// guarded by it (a membership event that was never published) or returned 401 to every caller.
    /// </remarks>
    public static Guid? ResolveObjectIdGuid(ClaimsPrincipal? user) =>
        Guid.TryParse(ResolveObjectId(user), out var id) ? id : null;

    /// <summary>
    /// Resolves a <b>LOCAL opaque key</b> for the caller — suitable for rate-limit partitions,
    /// idempotency scoping and cache keys, and for nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefers the object id, then accepts <c>sub</c> / <see cref="ClaimTypes.NameIdentifier"/>. Unlike
    /// <see cref="ResolveObjectId"/>, accepting <c>sub</c> here is CORRECT: it is stable per
    /// (user, application), which is exactly what a partition key needs, and its pairwise nature is a
    /// privacy property rather than a defect.
    /// </para>
    /// <para>
    /// <b>MUST NOT cross an application boundary.</b> Never pass this to Dataverse, to Graph, into a
    /// persisted row, or into an audit field that must correlate to a user — use
    /// <see cref="ResolveObjectId"/> for those. The name is deliberately unlike the others so that
    /// misuse is visible in review.
    /// </para>
    /// </remarks>
    public static string? ResolveOpaqueCallerKey(ClaimsPrincipal? user)
    {
        if (user is null) return null;

        return ResolveObjectId(user)
            ?? Normalize(user.FindFirst("sub")?.Value)
            ?? Normalize(user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
