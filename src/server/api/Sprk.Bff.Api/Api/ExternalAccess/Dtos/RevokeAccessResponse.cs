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
    /// No removal was attempted — no <c>ContainerId</c> was supplied.
    /// </summary>
    /// <remarks>
    /// <para><b>Task 020 narrowed this.</b> It used to ALSO mean "this was an organization-grant revoke",
    /// because an org revoke names no single grantee and the endpoint had no way to expand the
    /// organization to its members. An org revoke with a container now enumerates the organization's
    /// active members and reports one of the three outcomes below — so seeing <c>NotAttempted</c> on a
    /// request that supplied a <c>ContainerId</c> is now a defect signal rather than the org case.</para>
    /// </remarks>
    NotAttempted,

    /// <summary>
    /// A matching permission was found and deleted. For an ORGANIZATION revoke: at least one member's
    /// permission was deleted and no member's removal failed.
    /// </summary>
    PermissionRemoved,

    /// <summary>
    /// The container's permissions were read successfully and this Contact holds none. Genuinely absent —
    /// the expected result under the broker-only model. For an ORGANIZATION revoke: the member list was
    /// established (possibly empty) and no member held a permission.
    /// </summary>
    NoPermissionFound,

    /// <summary>
    /// The permission could not be read, matched, or deleted. The Contact may RETAIN file access; the
    /// revoke should be retried. For an ORGANIZATION revoke this covers BOTH "at least one member's
    /// permission could not be removed" and "the member list could not be established at all" — the two
    /// are told apart by <see cref="SpeOrgMemberCleanupSummary.MembersEnumerated"/>.
    /// </summary>
    Failed
}

/// <summary>
/// Member-granularity detail for the SPE cleanup performed by an ORGANIZATION-grant revoke (task 020,
/// spec FR-16b). <c>null</c> on the response of a per-contact revoke, which cleans exactly one identity.
/// </summary>
/// <param name="MembersEnumerated">
/// How many ACTIVE members the organization was found to have — or <c>null</c> when the member list could
/// NOT be established at all (the junction query failed, or the organization has more members than one
/// revoke request may sweep). <c>null</c> is the load-bearing value: it is the difference between
/// "we swept everyone and there was nothing to remove" and "we do not know who to sweep", which the
/// counts alone cannot express. It always pairs with
/// <see cref="SpeContainerRevokeOutcome.Failed"/>.
/// </param>
/// <param name="PermissionsRemoved">Members whose container permission was found and deleted.</param>
/// <param name="PermissionsNotFound">
/// Members whose identity was resolved and who held no container permission. The ordinary, healthy answer
/// under the broker-only model — nothing in this product grants a member a container ACL.
/// </param>
/// <param name="Failed">
/// Members whose container permission could NOT be confirmed removed — their email was unreadable or
/// absent (so their ACL entry is unfindable), or Graph refused the delete. <b>Non-zero means those people
/// may RETAIN file access</b>, so the revoke MUST NOT be reported as an SPE success.
/// </param>
/// <remarks>
/// Mirrors <c>SpeBulkRemovalResult</c>'s lesson from task 016/017: a bare success count cannot express
/// "3 of 12 removed", so a caller that treats any completed call as success reports a revoked grant while
/// nine people keep file access. The failure count is what makes the incomplete case sayable.
/// </remarks>
public sealed record SpeOrgMemberCleanupSummary(
    int? MembersEnumerated,
    int PermissionsRemoved,
    int PermissionsNotFound,
    int Failed);

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
/// <param name="SpeOrgMemberCleanup">
/// Member-granularity detail, present ONLY for an organization-grant revoke (task 020, spec FR-16b).
/// <para>An org revoke deactivates the grant for every member at once, so its SPE side is a many-identity
/// sweep and a single outcome cannot say how far it got. <paramref name="SpeContainerOutcome"/> remains
/// the verdict — "some members retain access" is <c>Failed</c>, never a success — and this carries the
/// arithmetic behind it.</para>
/// </param>
/// <remarks>
/// <b>Removed by task 017</b>: <c>WebRoleRemoved</c>, a Power Pages relic (register H-8b). It was
/// hard-coded to <c>false</c> at every call site — Spaarke does not manage Power Pages web roles — so it
/// described a subsystem that is not there and could only ever mislead.
/// </remarks>
public record RevokeAccessResponse(
    bool SpeContainerMembershipRevoked,
    SpeContainerRevokeOutcome SpeContainerOutcome,
    int DeactivatedCount = 0,
    SpeOrgMemberCleanupSummary? SpeOrgMemberCleanup = null);
