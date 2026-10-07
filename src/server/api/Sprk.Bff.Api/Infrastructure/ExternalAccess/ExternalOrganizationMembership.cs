using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// Moved here unchanged from Api/ExternalAccess/RevokeExternalAccessEndpoint.cs by unified-access-control-r2
// task 137, whose single grant-cache invalidation routine (ExternalParticipationService) needs the
// organization -> members filter from Infrastructure. The type's own remarks already called this a pure file move.

/// <summary>
/// The ACTIVE member contacts of one <c>sprk_organization</c>, and whether that list is trustworthy.
/// </summary>
/// <param name="ContactIds">Distinct member contact ids. Empty when the organization has no active members.</param>
/// <param name="ExceededBound">
/// <c>true</c> when the organization has MORE members than one revoke request may sweep, so
/// <paramref name="ContactIds"/> is a truncation rather than the member list. A caller must treat this as
/// "unknown membership", never as the answer — a truncated sweep reported as success is precisely the
/// failure this type exists to make unsayable.
/// </param>
internal readonly record struct OrganizationMemberSet(
    IReadOnlyList<Guid> ContactIds,
    bool ExceededBound);

/// <summary>
/// Reads <c>sprk_contactorganization</c> — the contact↔organization membership junction — in the
/// organization → members direction.
/// </summary>
/// <remarks>
/// <para><b>CLAUDE.md §11 justification (task 020).</b> <i>Existing</i>:
/// <c>ExternalParticipationService.ReadOrganizationMembershipsAsync</c> (named <c>QueryActiveOrgIdsAsync</c>
/// until task 109) reads the same junction in the INVERSE direction (contact → organizations).
/// <i>Extension</i>: not callable — it is private and built on a raw
/// <c>HttpClient</c> with its own app-only token flow, whereas the revoke path holds a
/// <c>DataverseWebApiClient</c>; reaching it would drag the participation service's token flow into the
/// revoke path. So the QUERY SHAPE is mirrored, not the code. <i>Cost of doing nothing</i>: an
/// organization-grant revoke deactivates the grant for every member and reports success while every one
/// of those members keeps their SPE container permission, and therefore continued access to the
/// project's files — a population no other code path will ever clean up (see the broker-only note on
/// <see cref="SpeContainerMembershipService.GrantMembershipAsync"/>).</para>
///
/// <para><b>Deliberately the smallest surface that answers one question</b> — "who are this
/// organization's active members" — rather than a general-purpose membership service. Task 043
/// (FR-24/FR-25 org expansion) needs the same answer and should EXTEND this rather than write a third
/// reader. Task 137 hoisted it here from <c>RevokeExternalAccessEndpoint.cs</c> (a pure file move): the single
/// grant-cache invalidation routine pages the SAME <see cref="ActiveMembersFilter"/> to completion, where this
/// bounded reader serves the revoke path's SPE cleanup.</para>
///
/// <para><b>Schema live-verified 2026-08-26</b> (Dataverse MCP <c>describe</c>), which is not optional
/// here: three Phase 0 tasks (070, 016, 017) turned on a stale column name, and a wrong one in a
/// revocation query reads as "nothing to revoke" — silently. Confirmed: collection
/// <c>sprk_contactorganizations</c>; lookups <c>sprk_contact</c> → <c>contact</c> and
/// <c>sprk_organization</c> → <c>sprk_organization</c>, projected as <c>_sprk_contact_value</c> /
/// <c>_sprk_organization_value</c>; <c>statecode</c> Active(0)/Inactive(1). This confirmed the assumption
/// that stood as a caveat comment in <c>QueryActiveOrgIdsAsync</c>; task 109 removed that caveat and cites
/// this record instead.</para>
/// </remarks>
internal static class ExternalOrganizationMembership
{
    internal const string EntitySet = "sprk_contactorganizations";

    internal const string MemberSelect = "_sprk_contact_value";

    /// <summary>
    /// The largest membership one revoke request will sweep.
    /// </summary>
    /// <remarks>
    /// <para>Task 020's escalation trigger names &gt;200 members as an owner decision rather than an
    /// implementation detail. The bound is not decoration: <c>DataverseWebApiClient.QueryAsync</c> reads
    /// ONE page and discards <c>@odata.nextLink</c>, so an unbounded query on a large organization would
    /// return a silently truncated list that looks exactly like a complete one. Asking for
    /// <c>Bound + 1</c> converts that silent truncation into a detectable, reportable condition.</para>
    /// <para>Live check 2026-08-26: the largest organization in the environment has <b>1</b> active
    /// member, so this is a guard rail rather than a live limit.</para>
    /// </remarks>
    internal const int MaxMembersPerSweep = 200;

    /// <summary>
    /// The <c>$filter</c> selecting one organization's ACTIVE memberships.
    /// </summary>
    /// <remarks>
    /// Mirrors the read path's junction <c>$filter</c> (<c>ExternalParticipationService
    /// .BuildOrganizationMembershipFilter</c>), inverted: <c>statecode</c> only. It deliberately does NOT
    /// filter on <c>sprk_enddate</c> / <c>sprk_startdate</c> — see the remarks on the caller for why a
    /// superset of the read path's conferring set matters more than being date-correct here. (The read
    /// filter also admits a NULL <c>statecode</c>, task 109; this one does not. Dataverse writes no null
    /// <c>statecode</c>, so the two select the same rows — recorded rather than changed, because this
    /// task's scope on the revoke path is comments only.)
    /// </remarks>
    internal static string ActiveMembersFilter(Guid organizationId)
        => $"_sprk_organization_value eq {organizationId} and statecode eq 0";

    /// <summary>
    /// The distinct ACTIVE member contacts of an organization.
    /// </summary>
    /// <remarks>Exceptions propagate: "the query failed" and "the organization has no members" must never
    /// be the same answer on a revocation path — that equivalence is the shape of finding A-13 and of the
    /// <c>ListExternalMembersAsync</c> defect task 016 filed.</remarks>
    internal static async Task<OrganizationMemberSet> QueryActiveMembersAsync(
        DataverseWebApiClient dataverseClient, Guid organizationId, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<ContactOrganizationRow>(
            EntitySet,
            filter: ActiveMembersFilter(organizationId),
            select: MemberSelect,
            top: MaxMembersPerSweep + 1,
            cancellationToken: ct);

        // The bound is checked on RAW rows, before Distinct: duplicate junction rows must not be able to
        // collapse an over-bound organization back under the limit and hide the truncation.
        if (rows.Count > MaxMembersPerSweep)
            return new OrganizationMemberSet(Array.Empty<Guid>(), ExceededBound: true);

        var contactIds = rows
            .Where(r => r.ContactId.HasValue)
            .Select(r => r.ContactId!.Value)
            .Distinct()
            .ToList();

        return new OrganizationMemberSet(contactIds, ExceededBound: false);
    }

    /// <summary>Minimal projection of a <c>sprk_contactorganization</c> junction row.</summary>
    internal sealed class ContactOrganizationRow
    {
        [JsonPropertyName("_sprk_contact_value")]
        public Guid? ContactId { get; set; }
    }
}
