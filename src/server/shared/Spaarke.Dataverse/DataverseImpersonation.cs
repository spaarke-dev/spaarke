using System.Net.Http;

namespace Spaarke.Dataverse;

/// <summary>
/// Web API impersonation helper: stamps a per-request Dataverse impersonation header so a query runs AS a
/// user (effective privileges = INTERSECTION of the app user's and the impersonated user's), letting
/// Dataverse do row-level filtering natively in one query.
/// <para>
/// <b>Fail closed (unified-access-control-r2 task 104, #990).</b> Both entry points take a non-nullable id and
/// THROW on <see cref="Guid.Empty"/> before touching the request, so a caller that asks to impersonate can never
/// produce an app-only (unscoped) request. There is deliberately no "impersonate if I have an id" overload: a
/// caller that does not impersonate does not call this helper at all. Until task 104 the helper silently added
/// no header for a null or empty id, and only <c>DataverseWebApiService.RetrieveMultipleImpersonatedAsync</c>
/// refused one — so a new call site that skipped that method degraded to an app-only query with HTTP 200.
/// </para>
/// <para>
/// <b>Header / value contract</b> (Microsoft Learn, "Impersonate another user using the Web API", checked
/// 2026-09-15):
/// <list type="bullet">
///   <item><c>CallerObjectId</c> = the user's <b>Microsoft Entra object id</b>
///     (<c>systemuser.azureactivedirectoryobjectid</c>). Microsoft marks this header <b>preferred</b>.
///     Use <see cref="ApplyAsEntraUser"/>.</item>
///   <item><c>MSCRMCallerID</c> = the Dataverse <b>systemuserid</b>. Microsoft marks this header <b>legacy</b>;
///     the BFF read path already resolves the caller's systemuserid, so it stays.
///     Use <see cref="ApplyAsSystemUser"/>. (<c>notes/access-model-decision.md</c> pairs <c>MSCRMCallerID</c> with
///     the Entra oid. That pairing is wrong: the oid belongs in <c>CallerObjectId</c>.)</item>
/// </list>
/// A request carries exactly ONE of the two: Microsoft does not document what Dataverse does when both are
/// present, so each entry point removes the other header before stamping its own.
/// </para>
/// <para>
/// <b>What the helper cannot check, and why.</b> Microsoft does not document what Dataverse returns for an
/// unknown, disabled or unlicensed user, or for an object id from another tenant, so the helper cannot rely on
/// Dataverse to refuse those. It checks what it can know: a non-empty id and, for an Entra object id, that the
/// token's tenant is the Dataverse org's tenant. The caller supplies both tenant ids, so the helper takes no
/// configuration dependency, and <b>the check is only as strong as the caller's sources</b>: the token tenant
/// must come from the validated token's <c>tid</c>, and the org tenant from configuration, never from the
/// same value twice. It does not check the target environment: <c>DataverseWebApiService</c> sends relative URIs
/// on an <c>HttpClient</c> whose base address is the configured org, so the environment is fixed by
/// construction there. A direct caller that builds its own request owns the environment choice.
/// </para>
/// <para>
/// Requires the calling application user to hold <c>prvActOnBehalfOfAnotherUser</c> (the Delegate role; it
/// cannot be inherited through a team). Without it Dataverse returns <c>CannotActOnBehalfOfAnotherUser</c>
/// (0x8004A110) rather than widening access.
/// </para>
/// </summary>
public static class DataverseImpersonation
{
    /// <summary>The legacy Web API impersonation header. It carries the target <c>systemuserid</c>.</summary>
    public const string CallerIdHeader = "MSCRMCallerID";

    /// <summary>The preferred Web API impersonation header. It carries the target's Microsoft Entra object id.</summary>
    public const string CallerObjectIdHeader = "CallerObjectId";

    /// <summary>
    /// Makes <paramref name="request"/> run AS the Dataverse user <paramref name="systemUserId"/>
    /// (<c>MSCRMCallerID</c>). Replaces any impersonation identity already on the request.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="systemUserId"/> is <see cref="Guid.Empty"/>. Refused, never sent app-only.
    /// </exception>
    public static void ApplyAsSystemUser(HttpRequestMessage request, Guid systemUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (systemUserId == Guid.Empty)
            throw new ArgumentException(
                "Impersonation requires a non-empty systemuserid; refusing to send the request app-only (fail closed).",
                nameof(systemUserId));

        Stamp(request, CallerIdHeader, systemUserId);
    }

    /// <summary>
    /// Makes <paramref name="request"/> run AS the user whose Microsoft Entra object id is
    /// <paramref name="entraObjectId"/> (<c>CallerObjectId</c>). Replaces any impersonation identity already on
    /// the request.
    /// </summary>
    /// <param name="request">The per-request message to stamp.</param>
    /// <param name="entraObjectId">The user's Entra object id (<c>oid</c>) from a validated token.</param>
    /// <param name="callerTenantId">The <c>tid</c> of the validated token the object id came from.</param>
    /// <param name="dataverseTenantId">The tenant the target Dataverse org belongs to.</param>
    /// <exception cref="ArgumentException">Any of the three ids is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The token's tenant is not the Dataverse org's tenant. Refused, because Microsoft does not document what
    /// Dataverse does with a foreign object id.
    /// </exception>
    public static void ApplyAsEntraUser(
        HttpRequestMessage request,
        Guid entraObjectId,
        Guid callerTenantId,
        Guid dataverseTenantId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (entraObjectId == Guid.Empty)
            throw new ArgumentException(
                "Impersonation requires a non-empty Entra object id; refusing to send the request app-only (fail closed).",
                nameof(entraObjectId));
        if (callerTenantId == Guid.Empty)
            throw new ArgumentException(
                "Impersonation by Entra object id requires the token's tenant id (fail closed).",
                nameof(callerTenantId));
        if (dataverseTenantId == Guid.Empty)
            throw new ArgumentException(
                "Impersonation by Entra object id requires the Dataverse org's tenant id (fail closed).",
                nameof(dataverseTenantId));
        if (callerTenantId != dataverseTenantId)
            throw new InvalidOperationException(
                $"Refusing to impersonate an Entra object id from tenant {callerTenantId} against a Dataverse org in "
                + $"tenant {dataverseTenantId} (fail closed).");

        Stamp(request, CallerObjectIdHeader, entraObjectId);
    }

    /// <summary>Exactly one impersonation identity per request: clears both headers, then sets one.</summary>
    private static void Stamp(HttpRequestMessage request, string header, Guid id)
    {
        request.Headers.Remove(CallerIdHeader);
        request.Headers.Remove(CallerObjectIdHeader);
        request.Headers.Add(header, id.ToString());
    }
}
