using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// What happened to the revoked Contact's SPE container permission.
/// </summary>
/// <remarks>
/// <para>A bool cannot express this (task 017, finding A-13). The old <c>bool</c> conflated four
/// different states, and its default answer on "no permission matched" was <c>true</c> — so
/// <c>/revoke</c> reported SPE success in exactly the case where it had removed nothing. Per this task's
/// ADR-003 constraint, "confirmed absent" and "match failed" must be distinguishable.</para>
///
/// <para><b>Reading these in context.</b> Spaarke is broker-only — nothing in the product ADDS a
/// container permission — so <see cref="NoPermissionFound"/> is the ordinary, healthy answer for most
/// contacts. <see cref="PermissionRemoved"/> means a legacy or externally-created ACL was cleaned up.
/// <see cref="Failed"/> is the one that needs attention: the person may still have file access.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SpeContainerRevokeOutcome>))]
public enum SpeContainerRevokeOutcome
{
    /// <summary>
    /// No removal was attempted — no <c>ContainerId</c> was supplied, or this was an organization-grant
    /// revoke, which names no single grantee and therefore no identity key to match.
    /// </summary>
    NotAttempted,

    /// <summary>A matching permission was found and deleted.</summary>
    PermissionRemoved,

    /// <summary>
    /// The container's permissions were read successfully and this Contact holds none. Genuinely absent —
    /// the expected result under the broker-only model.
    /// </summary>
    NoPermissionFound,

    /// <summary>
    /// The permission could not be read, matched, or deleted. The Contact may RETAIN file access; the
    /// revoke should be retried.
    /// </summary>
    Failed
}

/// <summary>
/// Response returned after revoking external access from a Contact.
/// </summary>
/// <param name="SpeContainerMembershipRevoked">
/// <c>true</c> only when a container permission was actually deleted.
/// <para>Task 017 (finding A-13) made this honest. It previously returned <c>true</c> whenever the
/// matcher found nothing — which was always, because the matcher searched for the contact's GUID inside
/// an email — so it was effectively a constant <c>true</c> that meant nothing. Prefer
/// <paramref name="SpeContainerOutcome"/>: this flag cannot distinguish "nothing to remove" from
/// "removal failed", and both report <c>false</c>.</para>
/// </param>
/// <param name="SpeContainerOutcome">
/// The precise SPE outcome. Added by task 017 because the boolean above cannot express the four states
/// an operator needs to tell apart — in particular "genuinely absent" versus "we could not tell".
/// </param>
/// <param name="DeactivatedCount">
/// How many <c>sprk_externalrecordaccess</c> rows were deactivated for this logical grant.
/// <para>Added by unified-access-control-r2 task 010 (spec FR-09, finding A-11). Revoke used to
/// deactivate exactly one row by id, so a duplicate grant survived revocation invisibly. It now sweeps
/// every active row for the grant's logical key, and this count makes the outcome explicit rather than
/// inferable: <c>0</c> is a safe no-op (the grant was already fully inactive), <c>1</c> is the normal
/// case, and <c>&gt;1</c> means duplicates existed and were collapsed.</para>
/// </param>
/// <remarks>
/// <b>Removed by task 017</b>: <c>WebRoleRemoved</c>, a Power Pages relic (register H-8b). It was
/// hard-coded to <c>false</c> at every call site — Spaarke does not manage Power Pages web roles — so it
/// described a subsystem that is not there and could only ever mislead.
/// </remarks>
public record RevokeAccessResponse(
    bool SpeContainerMembershipRevoked,
    SpeContainerRevokeOutcome SpeContainerOutcome,
    int DeactivatedCount = 0);
