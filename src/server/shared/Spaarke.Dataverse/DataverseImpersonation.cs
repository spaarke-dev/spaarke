using System.Net.Http;

namespace Spaarke.Dataverse;

/// <summary>
/// Web API impersonation helper: stamps a per-request Dataverse impersonation header so a query runs AS the
/// calling user (effective privileges = INTERSECTION of the app user's and the impersonated user's), letting
/// Dataverse do row-level filtering natively in one query. Additive + opt-in: callers that do NOT impersonate
/// are byte-unchanged (no header added).
/// <para>
/// <b>Header / value contract (verified against MS Learn):</b>
/// <list type="bullet">
///   <item><c>MSCRMCallerID</c> = the target <b>systemuserid</b> (a Dataverse user GUID). This is the header this
///     helper stamps, because the Spaarke read path already resolves the caller's <c>systemuserid</c>
///     (Azure AD <c>oid</c> → <c>systemuserid</c> via <c>azureactivedirectoryobjectid</c>, per
///     <see cref="DataverseAccessDataSource"/> / <c>UserPrivilegeChecker</c>).</item>
///   <item><c>CallerObjectId</c> = the target user's <b>Entra (Azure AD) object id</b>. The alternative when only
///     the AAD oid is known; NOT used here to avoid a per-value ambiguity.</item>
/// </list>
/// (Note: <c>notes/access-model-decision.md</c> pairs <c>MSCRMCallerID</c> with the AAD oid — that pairing is
/// incorrect per MS Learn; <c>MSCRMCallerID</c> takes the <c>systemuserid</c>. This helper uses the correct pairing.)
/// </para>
/// <para>
/// Requires the BFF application user to hold the Delegate role privilege <c>prvActOnBehalfOfAnotherUser</c>
/// (owner / Dataverse-admin config; a go-live prerequisite). Until that is configured, an impersonated call fails
/// at Dataverse rather than silently widening access — the correct fail direction.
/// </para>
/// </summary>
public static class DataverseImpersonation
{
    /// <summary>The Web API impersonation header that carries the target <c>systemuserid</c>.</summary>
    public const string CallerIdHeader = "MSCRMCallerID";

    /// <summary>
    /// Adds the <see cref="CallerIdHeader"/> impersonation header for <paramref name="impersonatedSystemUserId"/>
    /// when it is a real user id. A <c>null</c> or <see cref="System.Guid.Empty"/> value adds NO header — the request
    /// is left exactly as an app-only request (existing consumers unchanged). The header is set (not appended) so a
    /// re-used request message cannot accumulate duplicate impersonation identities.
    /// </summary>
    /// <param name="request">The per-request message to stamp.</param>
    /// <param name="impersonatedSystemUserId">The target Dataverse <c>systemuserid</c>, or null/empty for none.</param>
    public static void Apply(HttpRequestMessage request, Guid? impersonatedSystemUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (impersonatedSystemUserId is null || impersonatedSystemUserId.Value == Guid.Empty)
            return;

        // Replace any prior impersonation identity so the value is unambiguous.
        request.Headers.Remove(CallerIdHeader);
        request.Headers.Add(CallerIdHeader, impersonatedSystemUserId.Value.ToString());
    }
}
