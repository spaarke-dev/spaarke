namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after revoking external access from a Contact.
/// </summary>
/// <param name="SpeContainerMembershipRevoked">Whether the Contact was successfully removed from the SPE container.</param>
/// <param name="WebRoleRemoved">Whether the "Secure Project Participant" web role was removed from the Contact (only true when Contact has no remaining active participations).</param>
/// <param name="DeactivatedCount">
/// How many <c>sprk_externalrecordaccess</c> rows were deactivated for this logical grant.
/// <para>Added by unified-access-control-r2 task 010 (spec FR-09, finding A-11). Revoke used to
/// deactivate exactly one row by id, so a duplicate grant survived revocation invisibly. It now sweeps
/// every active row for the grant's logical key, and this count makes the outcome explicit rather than
/// inferable: <c>0</c> is a safe no-op (the grant was already fully inactive), <c>1</c> is the normal
/// case, and <c>&gt;1</c> means duplicates existed and were collapsed.</para>
/// <para>Additive and optional — existing callers (AccessGrantModal PCF, External SPA) that ignore the
/// field are unaffected.</para>
/// </param>
public record RevokeAccessResponse(
    bool SpeContainerMembershipRevoked,
    bool WebRoleRemoved,
    int DeactivatedCount = 0);
