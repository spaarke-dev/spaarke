// teams-app-r1 Task 022 (2026-08-04) — Accessible-record-set composition (the core authz gate).
//
// design.md §5 / spec FR-06 — authorization is uniform ("is this record in the principal's
// accessible-record set?"), but the SET is composed per principal plane:
//
//     accessible(principal) =
//         systemuser  → ADR-034 membership (auto — trusted internal staff, Dataverse-governed)
//       ∪ contact     → sprk_externalrecordaccess grants (per-record, materialized)
//       ∪ contact     → standing-grant runtime membership (IFF contact.sprk_standinggrant is set)
//
// EXACT composition rules honored here (design §5 per-principal table):
//   • systemuser principal  = ADR-034 membership ONLY (automatic). No grants/standing term.
//   • contact-only principal = sprk_externalrecordaccess grants  ∪  (standing-grant membership
//                              IFF the contact holds a standing grant). NEVER automatic membership
//                              without an explicit grant OR an explicit standing-grant policy flag.
//
// This GENERALIZES the CIAM-only ExternalCallerContext.HasProjectAccess record∈set check (which
// modeled the contact-grant plane only) to compose all three sources for EVERY principal type task
// 020 resolves. It is the single place the authorization boundary is composed + audited, and is the
// authz-before-stream gate task 030 (broker document access) depends on.
//
// unified-access-control-r2 task 135 (defect C1): the CIAM contact (external SPA) is composed HERE too,
// through ComposeForCiamContactAsync — the same contact-plane composition and veto pipeline as the
// workforce contact, minus the two derived-member terms (owner decision A2; see ComposeContactPlaneAsync).
// Before that, the CIAM strategy built its scope from the grant set alone and no veto ran on it.
//
// Broker-only (ADR-028 A2 NFR-02): reads membership/grant/flag data APP-ONLY against the already-
// resolved principal (task 020). No caller-token exchange (no OBO), no Graph SDK types, no
// AI-internal types.

using Spaarke.Dataverse;               // AccessRights — the rights type (root CLAUDE.md §11: reuse, do not fork)
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Composes and evaluates the accessible-record set for a resolved <see cref="WorkforcePrincipal"/>
/// per design.md §5 / spec FR-06 — and, since task 135, for a resolved CIAM contact
/// (<see cref="ComposeForCiamContactAsync"/>). The single enforcement primitive: given a principal + entity
/// type + record id, decide membership in the composed set (deny anything outside it).
/// </summary>
/// <remarks>
/// ADR-010 testing seam: the interface lets the endpoint filter (and task 030) be exercised against
/// a substitute composer, and lets the composition itself be unit-tested per principal plane.
/// </remarks>
public interface IAccessibleRecordSetService
{
    /// <summary>
    /// Composes the full accessible-record set of the given <paramref name="entityType"/> for the
    /// principal, unioning the design §5 sources that apply to the principal's plane.
    /// </summary>
    Task<AccessibleRecordSet> ComposeAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct);

    /// <summary>
    /// Composes the accessible-record set of <paramref name="entityType"/> for a CIAM (Entra External ID)
    /// contact — the external SPA's caller, already resolved to <paramref name="contactId"/> by its plane
    /// strategy (unified-access-control-r2 task 135 · defect C1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SAME contact-plane composition a workforce contact-only principal gets through
    /// <see cref="ComposeAsync"/> — one implementation, not a copy: the explicit grant term with Secure
    /// pre-max suppression (FR-22), then the deny-list veto over the contact and its organizations (FR-23),
    /// then Restricted (FR-21, nothing contact-sourced survives).
    /// </para>
    /// <para>
    /// The one difference is that the two DERIVED-MEMBER terms (standing-grant membership and organization
    /// expansion) are not composed: a CIAM contact has never had them, and owner decision A2 (round 3,
    /// 2026-09-30) keeps both planes as they work today. See the branch point in the shared composition.
    /// </para>
    /// <para>
    /// Takes a contact id rather than a <see cref="WorkforcePrincipal"/> on purpose: a CIAM caller is not a
    /// workforce identity, and passing one through as a <see cref="WorkforcePrincipal"/> would hand its
    /// <c>Kind</c>, <c>TenantId</c> and <c>Oid</c> to code written for workforce callers. Identity resolution
    /// (ADR-028 A1, oid-first) stays with the caller; this method never resolves identity.
    /// </para>
    /// <para>
    /// The result's <see cref="AccessibleRecordSet.PrincipalKind"/> is
    /// <see cref="WorkforcePrincipalKind.ContactOnly"/>: the set is a contact's, whichever plane asked.
    /// </para>
    /// </remarks>
    Task<AccessibleRecordSet> ComposeForCiamContactAsync(
        Guid contactId, string entityType, CancellationToken ct);

    /// <summary>
    /// The enforcement decision: does the principal hold <see cref="AccessRights.Read"/> on
    /// <paramref name="recordId"/> of <paramref name="entityType"/>? A <c>false</c> result MUST be enforced
    /// as a DENY (not merely an omission) by the caller.
    /// </summary>
    /// <remarks>
    /// Read only — it answers "may the caller SEE this record", not "may the caller change it".
    /// A mutating route MUST use <see cref="IsOperationPermittedAsync"/> instead; treating membership
    /// as permission to write is the defect FR-19 removes.
    /// <para>
    /// Task 136 (defect C2): this used to answer "is the id a key in the composed map", and two live paths
    /// put keys in that map with no rights at all (a Secure-suppressed organization grant; a matter or work
    /// assignment grant row with no level). It now answers through <see cref="AccessibleRecordSet.Contains"/>,
    /// which means "holds Read".
    /// </para>
    /// </remarks>
    Task<bool> IsRecordAccessibleAsync(
        WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct);

    /// <summary>
    /// The rights-aware enforcement decision (task 033 / FR-19): does the principal hold
    /// <b>every</b> right in <paramref name="requiredRights"/> on <paramref name="recordId"/>?
    /// </summary>
    /// <remarks>
    /// Fail-closed on every path: an empty record id, a record outside the composed set, and rights
    /// that do not cover the requirement all return <c>false</c>. A faulted composition throws rather
    /// than returning <c>false</c>, so a caller cannot mistake an outage for a considered deny.
    /// </remarks>
    Task<bool> IsOperationPermittedAsync(
        WorkforcePrincipal principal,
        string entityType,
        Guid recordId,
        AccessRights requiredRights,
        CancellationToken ct);

    /// <summary>
    /// The WRITE-time No Access check (unified-access-control-r2 task 139 · FR-23 · C4 fix direction): does the FR-23
    /// deny veto allow <paramref name="recordId"/> to this grantee, deny it, or could it not tell? Only
    /// <see cref="NoAccessCheckAnswer.Allowed"/> lets a grant proceed.
    /// </summary>
    /// <param name="entityType">The root's LOGICAL name (<c>sprk_project</c> / <c>sprk_matter</c> / <c>sprk_workassignment</c>).</param>
    /// <param name="recordId">The record the grant would be written on.</param>
    /// <param name="granteeContactId">The contact grantee, checked as a direct subject AND through its active
    /// organization memberships (read here). <c>null</c> for an organization-wide grant, or for a contact that does
    /// not exist yet.</param>
    /// <param name="granteeOrganizationIds">Further organization subjects: the organization of an organization-wide
    /// grant, or the firm a contact grant names.</param>
    /// <remarks>
    /// <para>The decision comes from the SAME code as the read-path veto (<c>ResolveDenyVetoAsync</c>) — the record's
    /// referenced organizations, the subject's wall set, the one <see cref="INoAccessListReader"/> — so the key shapes
    /// are never re-implemented for the write path.</para>
    /// <para><b>A tri-state, not a <c>bool</c></b> (task 142 r4 · owner round 13 item 4). A provable entry answers
    /// <see cref="NoAccessCheckAnswer.Denied"/>. A READ FAULT answers <see cref="NoAccessCheckAnswer.Unverifiable"/> —
    /// an unreadable membership read, an unreadable referenced-organization read, a fail-closed or absent deny-list
    /// answer, a call without a record, and any exception other than the caller's own cancellation (an HttpClient
    /// timeout included). Before r4 every one of them was absorbed into "denied", so a caller could not tell an outage
    /// from the record's policy. Both refusals fail CLOSED: a caller must never grant on anything but
    /// <see cref="NoAccessCheckAnswer.Allowed"/>, and must report <see cref="NoAccessCheckAnswer.Unverifiable"/> as a
    /// fault. Nothing to check (no contact, no organization) answers <see cref="NoAccessCheckAnswer.Allowed"/>.</para>
    /// <para>Never throws, except for the caller's own cancellation.</para>
    /// </remarks>
    Task<NoAccessCheckAnswer> CheckGranteeNoAccessAsync(
        string entityType,
        Guid recordId,
        Guid? granteeContactId,
        IReadOnlyCollection<Guid> granteeOrganizationIds,
        CancellationToken ct);
}

/// <summary>
/// The write-time No Access check's answer (unified-access-control-r2 task 142 r4 · owner round 13 item 4) — a
/// TRI-STATE, so a read fault is reported as a fault instead of being absorbed into "denied".
/// </summary>
public enum NoAccessCheckAnswer
{
    /// <summary>Every input was read and no active entry covers the grantee on the record: the grant may proceed.</summary>
    Allowed,

    /// <summary>An active No Access entry covers the grantee on the record — the record's policy. Refuse.</summary>
    Denied,

    /// <summary>
    /// The check could not be completed (Dataverse 5xx, throttling, a timeout, unreadable memberships or referenced
    /// organizations, a fail-closed or absent deny-list answer). Refuse — fail closed, it NEVER grants — and report it
    /// as a fault, never as an entry on the list.
    /// </summary>
    Unverifiable,
}

/// <summary>
/// The composed accessible-record set for one principal + entity type, with source provenance for
/// auditability. <see cref="Contains"/> is the enforcement check.
/// </summary>
public sealed class AccessibleRecordSet
{
    public required WorkforcePrincipalKind PrincipalKind { get; init; }
    public required string EntityType { get; init; }

    /// <summary>
    /// The evaluator's answer: <c>(recordId → rights)</c> (unified-access-control-r2 task 032 / FR-19).
    /// <para>
    /// This replaces a bare <c>HashSet&lt;Guid&gt;</c>, which STRUCTURALLY could not carry a level —
    /// the reason matters and work assignments had no rights at all (register A-8 / B-8). Terms
    /// contribute per-record rights and compose by HIGHEST-WINS max; vetoes then REMOVE entries.
    /// </para>
    /// <para>
    /// ⚠️ <b>A veto is never a value in this map.</b> "No Access" is not representable as a level: under
    /// max() a low value is simply ignored, so an ethical wall modelled as a level would fail silently
    /// in exactly the case it exists for (ADR-003 as amended by task 030). Vetoes delete keys.
    /// </para>
    /// <para>
    /// <b>No key without Read (task 136 · defect C2).</b> Every composition removes, as its last step, any
    /// entry whose rights lack <see cref="AccessRights.Read"/> — a Secure-suppressed organization grant and a
    /// level-less matter or work-assignment grant row both used to survive here as a key worth nothing, and
    /// every presence-gated read admitted it. The views below (<see cref="RecordIds"/>,
    /// <see cref="Contains"/>, <see cref="Count"/>) ALSO require Read, so a set built some other way (a
    /// test double, a future composer that forgets the step) still fails closed.
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<Guid, AccessRights> Rights { get; init; }

    private IReadOnlySet<Guid>? _recordIds;

    /// <summary>
    /// The de-duplicated record ids the principal may READ for this entity type.
    /// <para>
    /// As of task 032 this is a DERIVED VIEW over <see cref="Rights"/>, not a stored second collection,
    /// so ids and rights cannot disagree. Kept at this exact shape so <c>Tier2ScopeFilterInjector</c>,
    /// the module scope predicates and <c>CallerPrincipalResolver</c> are unaffected. Since task 136 an
    /// entry whose rights lack Read is not in this view.
    /// </para>
    /// </summary>
    public IReadOnlySet<Guid> RecordIds => _recordIds ??= Rights
        .Where(kvp => kvp.Value.HasFlag(AccessRights.Read))
        .Select(kvp => kvp.Key)
        .ToHashSet();

    /// <summary>
    /// The rights the principal holds on <paramref name="recordId"/>, or
    /// <see cref="AccessRights.None"/> when the record is not in the set. Fail-closed by construction:
    /// absence is None, never a default grant.
    /// </summary>
    public AccessRights RightsFor(Guid recordId) =>
        Rights.TryGetValue(recordId, out var rights) ? rights : AccessRights.None;

    /// <summary>Which design §5 union terms contributed to this set (audit + test introspection).</summary>
    public required AccessibleRecordSetSources Sources { get; init; }

    /// <summary>
    /// NFR-03 (unified-access-control-r2 task 015): <c>true</c> when composition stopped at the
    /// <see cref="CapLimit"/> ceiling while the source still had more records — i.e. this set is
    /// KNOWN INCOMPLETE. Callers MUST surface it to the user ("Only {CapLimit} records
    /// displayed"); they MUST NOT present a capped set as the whole truth.
    /// <para>
    /// <c>false</c> means composition ran to exhaustion, so the set is complete. Reaching the
    /// ceiling EXACTLY with nothing left to read is complete, not capped — the flag reports
    /// "there is more that you are not seeing", never "the count equals the limit".
    /// </para>
    /// </summary>
    public bool Capped { get; init; }

    /// <summary>
    /// The ceiling that produced <see cref="Capped"/>, so a caller can render the NFR-03
    /// message without hard-coding the number. Meaningful only when <see cref="Capped"/>.
    /// </summary>
    public int CapLimit { get; init; } = MembershipResolveOptions.MaxLimit;

    /// <summary>How many records the principal may read — the size of <see cref="RecordIds"/>.</summary>
    public int Count => RecordIds.Count;

    /// <summary>
    /// The enforcement check: <c>true</c> iff the principal holds <see cref="AccessRights.Read"/> on the
    /// record (task 136 · defect C2 — it used to be "the id is a key", which a None-rights key satisfied).
    /// </summary>
    /// <remarks>
    /// <c>HasFlag(Read)</c> on <see cref="AccessRights.None"/> is <c>false</c>, so an absent record and a
    /// present-but-powerless one give the same answer. Never test <c>HasFlag(None)</c>: it is always true.
    /// </remarks>
    public bool Contains(Guid recordId) => RightsFor(recordId).HasFlag(AccessRights.Read);
}

/// <summary>
/// Flags for which design §5 union terms contributed to an <see cref="AccessibleRecordSet"/>.
/// </summary>
/// <param name="SystemUserMembership">systemuser → ADR-034 membership (automatic) term applied.</param>
/// <param name="ContactGrants">contact → sprk_externalrecordaccess grants term applied.</param>
/// <param name="StandingGrantMembership">contact → standing-grant runtime membership term applied
/// (only when the contact held a standing grant).</param>
/// <param name="OrgExpansionMembership">
/// contact → ORG-EXPANSION membership term applied (design §4.5 term 4 / FR-24 + FR-25, task 043):
/// records referencing — via a registry-listed org-typed lookup — an organization the contact actively
/// belongs to, at that organization's standing-grant baseline.
/// <para>
/// ⚠️ <b>Reports that the term RAN, not that it yielded records</b> — <c>true</c> iff at least one
/// organization held a standing grant WITH a recognised baseline, i.e. iff the term COULD contribute.
/// A walk that then matched no records still leaves this <c>true</c>.
/// <para>
/// Wording tightened 2026-09-17 after task 043's code-review gate read the previous phrasing as
/// promising the stronger "did contribute" and flagged the code as contradicting its own doc. The code
/// is right and deliberately mirrors <paramref name="StandingGrantMembership"/>, which task 042 set
/// the same way: an organization with the flag unset, no baseline, or an unreadable row yields
/// <c>Rights == None</c>, never enters a bucket, and leaves this <c>false</c>. That is the rule task
/// 042 actually established — provenance must not claim a term that could contribute nothing (see
/// notes/task-042-standing-grant-levels.md §6.2) — and it is the distinction FR-30 provenance reads
/// (task 064) will consume, so the two flags must mean the same thing.
/// </para>
/// </para>
/// </param>
public readonly record struct AccessibleRecordSetSources(
    bool SystemUserMembership,
    bool ContactGrants,
    bool StandingGrantMembership,
    bool OrgExpansionMembership = false);

/// <summary>
/// The outcome of the ONE read of a subject's organization memberships (<c>sprk_contactorganization</c>):
/// two NAMED sets, plus whether the read could be completed at all.
/// </summary>
/// <param name="ConferringOrganizationIds">
/// Organizations whose membership CONFERS access today — an active junction row, current on both date
/// bounds (<c>sprk_startdate</c> ≤ today ≤ <c>sprk_enddate</c>, a null bound being unbounded), under an
/// ACTIVE <c>sprk_organization</c>. Owner decisions D-2 part 1, D-10, and ISS-026's read guard. Read by
/// every ADDITIVE org term: org expansion (here) and org grants
/// (<c>ExternalParticipationService.QueryOrganizationGrantRowsAsync</c>). Empty on a fault.
/// </param>
/// <param name="WallSubjectOrganizationIds">
/// Organizations the subject is checked against by the FR-23 deny veto — every ACTIVE junction row, bounded
/// on <c>statecode</c> ONLY. Deliberately a SUPERSET of the conferring set: an org-keyed ethical wall keeps
/// binding a former member, a not-yet-started one, and a member of an inactive organization (owner D-2
/// part 2, D-10). Date-bounding it would be a fail-OPEN change to a veto. Empty on a fault — so a consumer
/// MUST read <paramref name="Unreadable"/> first.
/// </param>
/// <param name="Unreadable">The read could not be completed. Additive terms contribute nothing; the veto
/// denies every queried candidate.</param>
/// <remarks>
/// <para>🔴 <b>Why this is an outcome and not a list.</b> One junction read feeds two consumers whose safe
/// failure directions are OPPOSITE:</para>
/// <list type="bullet">
/// <item>the ADDITIVE org terms, where over-inclusion is an over-GRANT — so a failed read must contribute
/// NOTHING;</item>
/// <item>the FR-23 deny-veto SUBJECT, where over-inclusion is merely a stricter wall — so a failed read must
/// deny EVERY candidate (the behaviour <c>ResolveDenyVetoAsync</c> has always had).</item>
/// </list>
/// <para>Collapsing both onto a bare empty list converts the veto's fail-CLOSED into a fail-OPEN: an empty
/// subject-org list looks exactly like "belongs to no organization", so the wall simply stops matching.
/// <see cref="Unreadable"/> keeps the two decisions distinct while still costing one read (NFR-02).</para>
/// <para>✅ <b>The guarantee now covers every fault — task 109 (ISS-019, #998).</b> Task 043's code-review
/// gate recorded that <see cref="Unreadable"/> was set only for faults reaching the evaluator as an
/// EXCEPTION (token/API-url acquisition): a junction QUERY failure (HTTP 500/403, timeout) was swallowed
/// inside <c>ExternalParticipationService</c>'s private query into an empty list, arrived as
/// <c>Unreadable: false</c>, and silently removed the wall's organization axis for that subject. The query
/// now returns <see cref="Failed"/> itself, which is the fix "one layer down" that review asked for — not a
/// second query for the veto path, which would have re-introduced the two-snapshot hazard task 043
/// removed.</para>
/// <para>Two NAMED sets rather than one set plus a predicate, so a consumer cannot pick up "the
/// organizations" without saying which question it is asking.</para>
/// </remarks>
internal readonly record struct ActiveOrgMemberships(
    IReadOnlyList<Guid> ConferringOrganizationIds,
    IReadOnlyList<Guid> WallSubjectOrganizationIds,
    bool Unreadable)
{
    /// <summary>No contact subject, or a subject that genuinely belongs to no organization.</summary>
    internal static ActiveOrgMemberships None { get; } = new(Array.Empty<Guid>(), Array.Empty<Guid>(), false);

    /// <summary>The read faulted — contribute nothing, and deny every queried candidate.</summary>
    internal static ActiveOrgMemberships Failed { get; } = new(Array.Empty<Guid>(), Array.Empty<Guid>(), true);
}

/// <inheritdoc />
public sealed class AccessibleRecordSetService : IAccessibleRecordSetService
{
    /// <summary>
    /// The root entity types <c>sprk_externalrecordaccess</c> grants can target (task 028 — closes the
    /// R1 design §5 known-gap #2: grants are no longer project-only). Each grant row carries exactly one
    /// typed root FK (project / matter / work assignment — verified live). Membership (ADR-034) and
    /// standing-grant membership span all entities; grants now span these three root types.
    /// </summary>
    internal const string ProjectEntity = "sprk_project";
    internal const string MatterEntity = "sprk_matter";
    internal const string WorkAssignmentEntity = "sprk_workassignment";

    private static readonly HashSet<string> GrantSupportedRootEntities =
        new(StringComparer.OrdinalIgnoreCase) { ProjectEntity, MatterEntity, WorkAssignmentEntity };

    /// <summary>Whether <c>sprk_externalrecordaccess</c> grants apply to the given entity type.</summary>
    private static bool IsGrantSupported(string entityType) =>
        GrantSupportedRootEntities.Contains(entityType);

    /// <summary>
    /// The granted <c>(recordId → rights)</c> of <paramref name="entityType"/> within a grant set
    /// (task 032 — was <c>GrantedIdsFor</c>, returning bare ids).
    /// <para>
    /// Every root type now contributes its row's OWN level. Matters and work assignments previously
    /// contributed an id and no level, which is the structural defect FR-19 removes.
    /// </para>
    /// </summary>
    private static IEnumerable<KeyValuePair<Guid, AccessRights>> GrantedRightsFor(
        ExternalGrantSet grants, string entityType)
        => GrantedRightsFor(grants, entityType, isDirectOnly: _ => false);

    /// <summary>
    /// The ONE pre-max suppression predicate over a composition's flag read (ADR-003 item 8; FR-22 widened by
    /// task 138): is this record DIRECT-ONLY — Secure OR Limited — for contacts?
    /// </summary>
    /// <remarks>
    /// <para>Both compositions (the systemuser plane's contact-grants term, and the shared contact plane that
    /// serves the workforce contact AND the CIAM contact since task 135) build their predicate HERE, and every
    /// term that suppresses consults it: the grant term (<see cref="GrantedRightsFor(ExternalGrantSet, string, Func{Guid, bool})"/>,
    /// direct level only), the standing-grant membership term and the organization-expansion term (nothing).
    /// There is no second suppression path, and no post-max subtraction.</para>
    /// <para>An id absent from the map is not suppressed. That is safe on the read path because
    /// <c>ExternalParticipationService.GetRootRecordFlagsAsync</c> returns every id it was asked about for a
    /// flag-bearing type (unreadable ones as <see cref="RootRecordFlags.Unreadable"/>, which IS direct-only);
    /// the write-time policy, which cannot rely on that, treats absence as unreadable instead
    /// (<see cref="ExternalGrantLifecycle.DecideGrantPolicy"/>).</para>
    /// </remarks>
    private static Func<Guid, bool> DirectOnlyPredicate(IReadOnlyDictionary<Guid, RootRecordFlags> flags)
        => id => flags.TryGetValue(id, out var f) && f.IsDirectOnly;

    /// <summary>
    /// The grant term with <b>direct-only pre-max suppression</b> applied (task 037 · FR-22; Limited joined
    /// Secure in task 138).
    /// </summary>
    /// <param name="isDirectOnly">Whether a given record id is direct-only for contacts — Secure OR Limited
    /// (<see cref="DirectOnlyPredicate"/>).</param>
    /// <remarks>
    /// For a direct-only record the grant contributes only its <b>direct</b> level — the caller's own grant
    /// rows. Anything inherited through an organization grant is suppressed, per FR-22.
    /// <para>
    /// ⚠️ <b>This is suppression, not subtraction, and the difference is the whole point.</b> The org
    /// contribution is never added, so it cannot participate in the max. Subtracting afterwards would be
    /// wrong in a way that is easy to miss: with a ViewOnly direct grant and a Collaborate org grant, the max
    /// yields Collaborate, and there is no arithmetic that recovers "Read" from it — the direct level has
    /// already been absorbed. That is why <c>ExternalParticipation.DirectAccessLevel</c> exists.
    /// </para>
    /// <para>
    /// A direct-only record whose ONLY source was an org grant has a null direct level, which maps to
    /// <see cref="AccessRights.None"/>. The term enters it at None (the max cannot resurrect it), and
    /// <see cref="RemoveEntriesWithoutRead"/> deletes it at the end of the composition (task 136 · defect C2).
    /// Until then it stayed in the answer as a key worth nothing, and every read route that asked "is the id
    /// present?" admitted it — including the app-only document content download.
    /// </para>
    /// </remarks>
    private static IEnumerable<KeyValuePair<Guid, AccessRights>> GrantedRightsFor(
        ExternalGrantSet grants, string entityType, Func<Guid, bool> isDirectOnly)
    {
        if (string.Equals(entityType, ProjectEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Projects.Select(p => KeyValuePair.Create(
                p.ProjectId,
                ExternalAccessLevels.ToAccessRights(
                    isDirectOnly(p.ProjectId) ? p.DirectAccessLevel : p.AccessLevel)));

        if (string.Equals(entityType, MatterEntity, StringComparison.OrdinalIgnoreCase))
            return grants.MatterGrants.Select(g => KeyValuePair.Create(
                g.RecordId,
                ExternalAccessLevels.ToAccessRights(
                    isDirectOnly(g.RecordId) ? g.DirectAccessLevel : g.AccessLevel)));

        if (string.Equals(entityType, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
            return grants.WorkAssignmentGrants.Select(g => KeyValuePair.Create(
                g.RecordId,
                ExternalAccessLevels.ToAccessRights(
                    isDirectOnly(g.RecordId) ? g.DirectAccessLevel : g.AccessLevel)));

        return Enumerable.Empty<KeyValuePair<Guid, AccessRights>>();
    }

    /// <summary>Every record id any term could contribute — the candidate set for one batched flag read.</summary>
    private static IEnumerable<Guid> GrantedIdsFor(ExternalGrantSet grants, string entityType)
    {
        if (string.Equals(entityType, ProjectEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Projects.Select(p => p.ProjectId);
        if (string.Equals(entityType, MatterEntity, StringComparison.OrdinalIgnoreCase))
            return grants.MatterGrants.Select(g => g.RecordId);
        if (string.Equals(entityType, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
            return grants.WorkAssignmentGrants.Select(g => g.RecordId);
        return Enumerable.Empty<Guid>();
    }

    /// <summary>
    /// The rights a MEMBERSHIP term contributes (ADR-034 membership; standing-grant membership).
    /// <para>
    /// Collaborate-equivalent, which RELOCATES rather than changes today's behaviour: the workforce
    /// strategy currently blanket-stamps Collaborate over every accessible record downstream
    /// (<c>CallerPrincipalResolver</c>, register A-8). Task 032 makes that an explicit TERM LEVEL inside
    /// the evaluator so it composes under max instead of overwriting; the stamp itself is deleted by
    /// task 033, and on the systemuser plane this term is replaced outright by Dataverse's own answer
    /// when the FR-20 swap (task 036) lands.
    /// </para>
    /// <para>
    /// It is a constant here ON PURPOSE: membership confers no per-record level, so inventing a
    /// differentiated one would be fabricating authority the source data does not carry.
    /// </para>
    /// </summary>
    internal const AccessRights MembershipTermRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Create;

    /// <summary>Nothing survives the Restricted veto — used by the contact plane, where every term is contact-sourced.</summary>
    private static readonly IReadOnlyDictionary<Guid, AccessRights> EmptyRights =
        new Dictionary<Guid, AccessRights>();

    /// <summary>
    /// The additive composition: merge a term's contribution into the accumulator, HIGHEST WINS.
    /// <para>
    /// Rights are <c>[Flags]</c>, so "highest wins" is a bitwise OR of the contributed sets — a record
    /// reached by a ViewOnly grant AND an org Collaborate grant ends at the union, exactly as the
    /// grant-row dedupe does within a single term.
    /// </para>
    /// <para>
    /// ⚠️ A term may only ADD or WIDEN. Nothing here can narrow or remove an entry — that is a veto's
    /// job, and vetoes run after the max. Keeping the two operations distinct is what stops "No Access"
    /// from being smuggled in as a low value that max() would silently discard.
    /// </para>
    /// </summary>
    private static void AccumulateTerm(
        Dictionary<Guid, AccessRights> accumulator,
        IEnumerable<KeyValuePair<Guid, AccessRights>> term)
    {
        foreach (var (recordId, rights) in term)
        {
            accumulator[recordId] = accumulator.TryGetValue(recordId, out var existing)
                ? existing | rights
                : rights;
        }
    }

    /// <summary>
    /// The ordered veto pipeline (ADR-003 as amended by task 030 — design §4.5): deny-list (task 039 /
    /// FR-23), then Restricted (task 037 / FR-21). Direct-only suppression (Secure or Limited — task 037 /
    /// FR-22, widened by task 138) happens EARLIER, on the additive TERMS before they are accumulated into
    /// <paramref name="composed"/> — it is not a slot in this method at all; see the <c>isDirectOnly</c> parameter of
    /// <see cref="GrantedRightsFor(ExternalGrantSet, string, Func{Guid, bool})"/>.
    /// <para>
    /// The order is load-bearing and is asserted by the shape of this method rather than by a comment
    /// elsewhere:
    /// </para>
    /// <list type="number">
    /// <item><b>Pre-max suppression (Secure or Limited)</b> — must run BEFORE the max, on the TERMS. After the max
    /// the suppressed term has already won and the suppression is a no-op on the only inputs that
    /// mattered. (Not in this method — see above.)</item>
    /// <item><b>Deny list</b> — post-max, FIRST among the vetoes below; removes the entry.</item>
    /// <item><b>Restricted</b> — post-max, after the deny list; removes the entry (or keeps a
    /// non-contact-sourced survivor).</item>
    /// </list>
    /// <para>
    /// A veto REMOVES a key. It never writes a value, and there is no <c>AccessRights</c> value in this
    /// codebase that means "denied" — absence is the only representation of no access.
    /// </para>
    /// </summary>
    /// <param name="composed">The post-max map, mutated in place.</param>
    /// <param name="deniedRecordIds">
    /// The FR-23 deny-list veto set for this composition (task 039) — every candidate record id the
    /// principal is denied on, whether by a direct per-record entry or by referencing an organization
    /// the principal (or their active organization membership) is denied against. Computed by the
    /// caller (<see cref="ResolveDenyVetoAsync"/>) BEFORE this method runs, from the SAME candidate id
    /// list used for the flag read below — never derived from <paramref name="composed"/>'s current
    /// keys, so it is unaffected by whatever the Restricted slot does.
    /// </param>
    /// <param name="flags">Veto flags for every candidate record (fail-closed for unreadable ones).</param>
    /// <param name="survivesRestricted">
    /// The rights that are NOT contact-sourced and therefore survive the Restricted veto — the systemuser
    /// plane's own ADR-034 membership term. Empty on the contact plane, where every term is contact-sourced.
    /// </param>
    /// <param name="contactSourcedDenials">
    /// Task 143 (owner N3): records on which the deny list removes only the CONTACT-SOURCED contribution — the
    /// systemuser plane's linked-contact or organization entries on a NON-secure record. Each keeps exactly what
    /// survives Restricted (the non-contact-sourced term), or goes when nothing does. <c>null</c> on the contact
    /// plane, where everything is contact-sourced and the deny list removes the record.
    /// </param>
    private static void ApplyVetoPipeline(
        Dictionary<Guid, AccessRights> composed,
        IReadOnlySet<Guid> deniedRecordIds,
        IReadOnlyDictionary<Guid, RootRecordFlags> flags,
        IReadOnlyDictionary<Guid, AccessRights> survivesRestricted,
        IReadOnlySet<Guid>? contactSourcedDenials = null)
    {
        // Slot 1 — deny list (ethical wall + per-child revocation). Task 039 / FR-23.
        //
        // Runs FIRST, by construction: a record removed here is gone before Restricted's loop below
        // even considers it, so a deny can never be "downgraded" into a survivable Restricted outcome —
        // and, combined with running after the additive max above (in both composers), a Full Access
        // grant plus a matching deny entry can never be resurrected: max() has already run, and nothing
        // after this point can add a key back.
        //
        // The veto REMOVES the key. It does not write None. IsOperationPermittedAsync explicitly
        // rejects AccessRights.None as a CALLER BUG (task 033), so a None written here would be refused
        // as a malformed request rather than honoured as a denial — absence is the only representation
        // of no access.
        foreach (var recordId in deniedRecordIds)
        {
            composed.Remove(recordId);
        }

        // Slot 1b — the contact-sourced half only (task 143, owner N3). On a NON-secure record the internal user's
        // own access is Dataverse's business (C9: access in MDA = access in Teams/SPA), and Q4 scopes the internal
        // wall to secure records; only what came THROUGH the linked contact is vetoed. The same survivor rule as
        // Restricted below, deliberately: the non-contact-sourced term is the part no contact rule may touch.
        if (contactSourcedDenials is not null)
        {
            foreach (var recordId in contactSourcedDenials)
            {
                if (deniedRecordIds.Contains(recordId) || !composed.ContainsKey(recordId))
                {
                    continue;
                }

                if (survivesRestricted.TryGetValue(recordId, out var surviving) && surviving != AccessRights.None)
                {
                    composed[recordId] = surviving;
                }
                else
                {
                    composed.Remove(recordId);
                }
            }
        }

        // Slot 2 — Restricted (sprk_accesspermission == Restricted). Task 037 / FR-21.
        //
        // "Only system users may have access" (register F-4). Every CONTACT-SOURCED contribution is
        // removed regardless of how strong it was — an explicit FullAccess grant included. What remains is
        // whatever the principal held through a non-contact term, which today is the systemuser plane's
        // ADR-034 membership.
        //
        // ⚠️ The veto REMOVES the key when nothing survives. It does not write None. Absence is the only
        // representation of no access: a None value would still be a key in the map, would still appear in
        // the derived RecordIds set, and would still read as "in the accessible set" to any consumer that
        // checks membership rather than rights.
        //
        // Task 137 · defect C5: an INACTIVE root shares this slot. A deactivated project, matter or work assignment
        // confers nothing contact-sourced, with exactly Restricted's survivor rule — the systemuser's own membership
        // stays — and, being read-time, reactivating the record restores access with no data change. An unreadable
        // root is both Restricted and inactive (RootRecordFlags.Unreadable), so it lands here either way.
        foreach (var recordId in composed.Keys.ToList())
        {
            if (!flags.TryGetValue(recordId, out var f) || !f.RemovesContactSourcedAccess)
            {
                continue;
            }

            if (survivesRestricted.TryGetValue(recordId, out var surviving) && surviving != AccessRights.None)
            {
                composed[recordId] = surviving;
            }
            else
            {
                composed.Remove(recordId);
            }
        }
    }

    /// <summary>
    /// The systemuser plane's Restricted survivor: its ADR-034 membership term — EXCEPT on a Restricted record when the
    /// systemuser is flagged <c>sprk_isexternal = true</c> (task 114 verifier K1, owner round 67: a Restricted record is for
    /// internal use only, and <see cref="Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.IsBarredOnRestricted"/> is
    /// the one predicate). There membership survives nothing, exactly as a contact's access does not. Inactive-only records
    /// keep the survivor rule unchanged — inactivity is not a Restricted question.
    /// </summary>
    /// <remarks>
    /// The flag is read only when a membership candidate is Restricted (NFR-02), through the authoritative cached
    /// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> (a flag change is seen within its cache lifetime). A read
    /// that fails is answered as external (fail closed, ADR-003): the Restricted candidates lose their membership term.
    /// Not here: Dataverse's own business-unit read of a NON-secure Restricted record by a user in that business unit —
    /// accepted as a known limit (owner round 77).
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, AccessRights>> SurvivesRestrictedForSystemUserAsync(
        Guid systemUserId, string entityType, IReadOnlyList<KeyValuePair<Guid, AccessRights>> membershipTerm,
        IReadOnlyDictionary<Guid, RootRecordFlags> flags, CancellationToken ct)
    {
        var survives = new Dictionary<Guid, AccessRights>();
        foreach (var (id, rights) in membershipTerm)
        {
            survives[id] = rights;
        }

        var restricted = survives.Keys.Where(id => flags.TryGetValue(id, out var f) && f.IsRestricted).ToList();
        if (restricted.Count == 0)
        {
            return survives;
        }

        bool isExternal;
        try
        {
            isExternal = await _systemUsers.IsExternalAsync(systemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[WF-AUTHZ] Whether systemuser {SystemUserId} is flagged external could not be read: failing CLOSED — their " +
                "membership term does not survive on the {Count} Restricted {EntityType} candidate(s) (task 114).",
                systemUserId, restricted.Count, entityType);
            isExternal = true;
        }

        if (!Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.IsBarredOnRestricted(isExternal, rootIsRestricted: true))
        {
            return survives;
        }

        foreach (var id in restricted)
        {
            survives.Remove(id);
        }

        _logger.LogInformation(
            "[WF-AUTHZ] Systemuser {SystemUserId} is flagged external: {Count} Restricted {EntityType} record(s) keep no " +
            "membership-term access (owner round 67, task 114).", systemUserId, restricted.Count, entityType);
        return survives;
    }

    /// <summary>
    /// The LAST step of every composition, after <see cref="ApplyVetoPipeline"/>: delete every entry whose
    /// rights lack <see cref="AccessRights.Read"/> (unified-access-control-r2 task 136 · defect C2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two live paths put a key into the composed map with no rights. A Secure root reached only through an
    /// organization grant: the term contributes the null direct level, which is None (FR-22). And a matter or
    /// work-assignment grant row with no <c>sprk_accesslevel</c>, kept as a None key by the grant read. A veto
    /// already removes keys; these were never vetoed, so they stayed — and the external read routes, the module
    /// scope and <c>/me</c> asked "is the id present?", so a key worth nothing admitted reads, including the
    /// app-only document content download. The owner's rule (C9, 2026-09-30) is that a contact gets only the
    /// records it is granted, at the granted level; a record with no rights is not a granted record.
    /// </para>
    /// <para>
    /// Removal, never a sentinel: there is no <see cref="AccessRights"/> value that means "denied", and absence
    /// is the only representation of no access (the same rule the veto slots follow). Task 042 set the
    /// precedent on the standing term — "present but powerless is worse than not accessible".
    /// </para>
    /// <para>
    /// The views on <see cref="AccessibleRecordSet"/> and on <c>CallerPrincipal</c> also require Read, so a
    /// composition that skipped this step would still fail closed there. This step is what keeps the answer
    /// itself honest for every consumer of <see cref="AccessibleRecordSet.Rights"/>.
    /// </para>
    /// </remarks>
    private static void RemoveEntriesWithoutRead(Dictionary<Guid, AccessRights> composed)
    {
        foreach (var (recordId, rights) in composed.ToList())
        {
            if (!rights.HasFlag(AccessRights.Read))
            {
                composed.Remove(recordId);
            }
        }
    }

    /// <summary>Shared "nothing to evaluate / nothing denied" result for <see cref="ResolveDenyVetoAsync"/>.</summary>
    private static readonly IReadOnlySet<Guid> EmptyDeniedSet = new HashSet<Guid>();

    /// <summary>
    /// The FR-23 deny veto's answer for one candidate batch (task 142 r4 · owner round 13 item 4): the candidates a
    /// matching entry provably denies, and — kept APART — the candidates that could not be evaluated because a read
    /// faulted.
    /// </summary>
    /// <param name="Denied">Candidates an active entry covers (the record's policy).</param>
    /// <param name="Unverifiable">Candidates the veto could not evaluate: the subject's memberships unreadable, the
    /// record's referenced organizations unreadable, a fail-closed or absent deny-list answer, or a throw. Fail closed:
    /// the read path removes them (<see cref="Removed"/>) and the write-time check refuses them — but as a FAULT
    /// (<see cref="NoAccessCheckAnswer.Unverifiable"/>), never as an entry.</param>
    private readonly record struct DenyVetoResult(IReadOnlySet<Guid> Denied, IReadOnlySet<Guid> Unverifiable)
    {
        internal static DenyVetoResult None { get; } = new(EmptyDeniedSet, EmptyDeniedSet);

        internal static DenyVetoResult AllUnverifiable(IEnumerable<Guid> ids) => new(EmptyDeniedSet, ids.ToHashSet());

        /// <summary>
        /// Every candidate the READ path removes: a provable denial OR an unverifiable candidate — exactly the set the veto
        /// removed before r4 split them (fail closed; the composition is unchanged).
        /// </summary>
        internal IReadOnlySet<Guid> Removed =>
            Unverifiable.Count == 0 ? Denied
            : Denied.Count == 0 ? Unverifiable
            : Denied.Concat(Unverifiable).ToHashSet();
    }

    /// <summary>
    /// Narrows an org-expansion walk to org-typed descriptors ONLY (task 043).
    /// </summary>
    /// <remarks>
    /// Load-bearing, not tidiness. <c>ResolveByContactAsync</c> binds the <c>ContactId</c>
    /// unconditionally, so a walk that bound organization ids WITHOUT this narrowing would return the
    /// union of contact-derived and org-derived records — and the org term credits every id it
    /// receives at the ORGANIZATION's baseline. A contact's own assignment would then silently inherit
    /// its firm's level. The returned ids are identical either way, so nothing downstream could
    /// detect it.
    /// </remarks>
    private static readonly string[] OrganizationIdentityTypeOnly = { "Organization" };

    /// <summary>
    /// Reads the subject's organization memberships ONCE per composition, for both the org-expansion term
    /// and the deny-veto subject (task 043; the one read now yields two named sets — task 109).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hoisted out of <see cref="ResolveDenyVetoAsync"/>, which used to perform this read itself. Two
    /// reads in one composition could disagree — the additive term and the wall would then be computed
    /// from different memberships — and the org-expansion term needs the ids BEFORE the membership walk
    /// anyway, since they are an input to it.
    /// </para>
    /// <para>
    /// <b>Date bounds — decided by the owner (D-2, D-10), implemented by task 109.</b> Task 043 left the
    /// read bounded on <c>statecode</c> alone because one bare-id projection could not be right for both
    /// consumers. It is one READ, not one FILTER: the read now projects both date columns and the
    /// organization's state, and returns the CONFERRING set (date-bounded at both ends, active
    /// organization) beside the WALL-SUBJECT set (<c>statecode</c> only). This method returns the outcome
    /// unchanged; the consumers below each pick their own set.
    /// </para>
    /// <para>
    /// Every fault becomes <see cref="ActiveOrgMemberships.Failed"/>: a query-level fault inside the read
    /// itself (task 109 · ISS-019), and a token/API-url fault here. Only the caller's own cancellation
    /// propagates (rethrown by the participation service's entry) — a timeout is a fault, not a
    /// cancellation.
    /// </para>
    /// </remarks>
    private async Task<ActiveOrgMemberships> ReadActiveOrgMembershipsAsync(
        Guid? subjectContactId, CancellationToken ct)
    {
        if (subjectContactId is not { } contactId || contactId == Guid.Empty)
        {
            // The deny list is keyed on contact/organization identities only (FR-23), and organization
            // membership is itself read FROM the contact — so a principal with no contact identity has
            // no relevant subject on EITHER axis. Not a failure.
            return ActiveOrgMemberships.None;
        }

        try
        {
            return await _participations
                .ReadOrganizationMembershipsAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[WF-AUTHZ] Active-organization read FAILED for contact {ContactId}. The org-expansion " +
                "term will contribute NOTHING, and the deny veto will deny every queried candidate — " +
                "the two consumers fail in opposite, deliberate directions (NFR-01).",
                contactId);
            return ActiveOrgMemberships.Failed;
        }
    }

    /// <summary>
    /// Resolves the FR-23 deny-list veto set for one composition (task 039). Builds each candidate's
    /// referenced organizations (the org-typed lookups enumerated in
    /// projects/unified-access-control-r2/notes/task-039-org-reference-inventory.md — today
    /// <c>sprk_assignedlawfirm1</c>/<c>2</c> on all three roots), takes the SUBJECT's own active
    /// organization membership as a resolved input, and queries <see cref="INoAccessListReader"/>.
    /// Shared by both principal planes so this resolution logic exists in exactly one place.
    /// </summary>
    /// <param name="entityType">The root entity type being composed.</param>
    /// <param name="candidateIds">
    /// The SAME candidate id list already built for the flag read (task 037) — never rebuilt here, per
    /// the task's own notes.
    /// </param>
    /// <param name="subjectContactId">
    /// The contact identity to check as a DIRECT subject (contact×org / contact×record deny rows) — the
    /// SAME resolved contact used for the contact-grant term, so "who is checked against the wall"
    /// never diverges from "whose grants applied". Null (or <see cref="Guid.Empty"/>) when no contact
    /// identity is available for this principal on this entity type — the deny list is keyed on
    /// contact/organization identities only (spec FR-23), and organization membership is itself read
    /// FROM the contact, so a principal with no contact identity has no deny-list-relevant subject on
    /// EITHER axis. The deny-list reader is never even queried in that case.
    /// </param>
    /// <param name="subjectOrgs">
    /// The subject's organization memberships, already resolved by
    /// <see cref="ReadActiveOrgMembershipsAsync"/> (task 043 hoisted the read out of this method so the
    /// additive org-expansion term and this veto cannot be computed from two different snapshots). This
    /// veto reads <see cref="ActiveOrgMemberships.WallSubjectOrganizationIds"/> — the <c>statecode</c>-only
    /// set — and NEVER the conferring set (owner D-2 part 2, D-10). <see cref="ActiveOrgMemberships.Unreadable"/>
    /// denies every queried candidate, and since task 109 it is set for every fault of the junction read,
    /// query-level ones included.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Denial deliberately OVER-matches (spec FR-23 / register B-10).</b> The record-side
    /// organization match uses EVERY organization the record references — it is NOT narrowed to task
    /// 041's access-conferring registry. An organization referenced only via a non-conferring lookup
    /// (e.g. opposing counsel) still denies if it is named as a deny object.
    /// </para>
    /// <para>
    /// <b>Fails closed toward DENIAL for every fault it can observe.</b> These surfaces all resolve to
    /// the SAME read-path outcome — the affected ids removed — and since task 142 r4 each is reported in
    /// <see cref="DenyVetoResult.Unverifiable"/>, never as a provable denial:
    /// </para>
    /// <list type="bullet">
    /// <item>The deny-list reader itself already fails closed (task 038), returning a deny-all-queried
    /// result with <see cref="NoAccessListResult.FailedClosed"/> set rather than throwing. Every queried id is
    /// unverifiable; a reader that returns no answer at all is the same fault.</item>
    /// <item>A record whose OWN referenced-organizations could not be resolved
    /// (<see cref="ReferencedOrganizations.Unreadable"/>) is denied DIRECTLY, independent of whatever
    /// the reader would say — it is never even added to the candidate batch sent to the reader.
    /// Silently treating an unreadable record as "references nothing" would let it slip past a real
    /// deny entry keyed on an organization it actually references but which the read could not
    /// confirm — exactly the "skipped record is an unevaluated wall" case task 039's escalation
    /// trigger names.</item>
    /// <item>A subject whose organizations could not be read
    /// (<see cref="ActiveOrgMemberships.Unreadable"/>) denies every queried candidate, mirroring
    /// <see cref="NoAccessListReader"/>'s own over-large-subject-set precedent: a subject that cannot be
    /// safely evaluated is treated the same as a subject the reader could not evaluate. Since task 109 this
    /// covers a junction QUERY failure (HTTP 500/403, timeout, any non-success status) as well as a
    /// token/API-url fault — the query reports <see cref="ActiveOrgMemberships.Failed"/> instead of an
    /// empty list (ISS-019, #998). Before that, a failed query arrived here as "belongs to no
    /// organization" and silently removed the wall's organization axis for that subject.</item>
    /// <item>Any other unexpected fault in this method is caught below and denies every queried
    /// candidate.</item>
    /// </list>
    /// <para>
    /// <b>The subject's organizations OVER-match too, on purpose</b> (owner D-2 part 2, D-10): the wall
    /// set is every organization with an ACTIVE junction row — a membership ended by date, one not yet
    /// started, and one under an inactive organization all still bind. The conferring set the additive
    /// terms use is narrower; using it here would make the wall match fewer subjects, a fail-OPEN change
    /// to a veto.
    /// </para>
    /// <para>
    /// In every case the veto is never SKIPPED — an observable fault denies; it never causes the
    /// pipeline to proceed as though nothing needed checking (spec NFR-01).
    /// </para>
    /// <para>
    /// <b>A fault is reported as a fault, not absorbed into "denied"</b> (task 142 r4 · owner round 13 item 4). Every
    /// fault above lands in <see cref="DenyVetoResult.Unverifiable"/>, a provable entry in
    /// <see cref="DenyVetoResult.Denied"/>. The read path removes BOTH (<see cref="DenyVetoResult.Removed"/> — the
    /// composition is unchanged); the write-time check answers <see cref="NoAccessCheckAnswer.Unverifiable"/> for the
    /// first and <see cref="NoAccessCheckAnswer.Denied"/> for the second, so its callers can count an outage instead of
    /// mistaking it for the record's policy. A cancellation still propagates (unchanged: a composition that cannot
    /// finish throws rather than answer).
    /// </para>
    /// </remarks>
    private async Task<DenyVetoResult> ResolveDenyVetoAsync(
        string entityType,
        IReadOnlyCollection<Guid> candidateIds,
        Guid? subjectContactId,
        ActiveOrgMemberships subjectOrgs,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return DenyVetoResult.None;
        }

        // No subject on EITHER axis: nothing to check. On the read path this is exactly "no contact" — the org set is
        // read FROM the contact, so it is empty (and readable) whenever the contact is absent. Task 139's write-time
        // entry point is the one caller that can supply an organization subject with no contact (an organization-wide
        // grant, or a contact not created yet), and then the organization axis is checked.
        var hasContactSubject = subjectContactId is { } cid && cid != Guid.Empty;
        if (!hasContactSubject && !subjectOrgs.Unreadable && subjectOrgs.WallSubjectOrganizationIds.Count == 0)
        {
            return DenyVetoResult.None;
        }

        // The subject's own organization memberships could not be read. Every queried candidate is UNVERIFIABLE —
        // removed on the read path (the same outcome this method's catch-all has always produced for this fault, now
        // decided from the hoisted read's outcome rather than by catching the read here — task 043), and a reported
        // fault at write time (task 142 r4).
        if (subjectOrgs.Unreadable)
        {
            _logger.LogError(
                "[WF-AUTHZ] Deny-veto resolution for {EntityType} ({Count} candidates) cannot proceed: " +
                "the subject's active organizations were unreadable. Failing CLOSED — every queried candidate is " +
                "unverifiable and removed; the veto is never skipped (NFR-01).",
                entityType, candidateIds.Count);
            return DenyVetoResult.AllUnverifiable(candidateIds);
        }

        try
        {
            // The WALL set — statecode only, never the conferring set (owner D-2 part 2, D-10).
            var subjectOrgIds = subjectOrgs.WallSubjectOrganizationIds;

            var referencedOrgs = await _participations
                .GetReferencedOrganizationIdsAsync(entityType, candidateIds, ct).ConfigureAwait(false);

            var unverifiable = new HashSet<Guid>();
            var candidateRecords = new List<NoAccessCandidateRecord>(candidateIds.Count);
            foreach (var recordId in candidateIds)
            {
                if (referencedOrgs.TryGetValue(recordId, out var refs) && refs.Unreadable)
                {
                    // Never sent to the reader: an unreadable record is an unevaluated wall (task 039's escalation).
                    unverifiable.Add(recordId);
                    continue;
                }

                var orgIds = referencedOrgs.TryGetValue(recordId, out var resolved)
                    ? resolved.OrganizationIds
                    : Array.Empty<Guid>();
                candidateRecords.Add(new NoAccessCandidateRecord(entityType, recordId, orgIds));
            }

            var denied = new HashSet<Guid>();
            if (candidateRecords.Count > 0)
            {
                var result = await _noAccessList
                    .GetDeniedRecordsAsync(subjectContactId, subjectOrgIds, candidateRecords, ct)
                    .ConfigureAwait(false);

                if (result is null || result.FailedClosed)
                {
                    // The reader could not complete its read (or could not safely evaluate this subject — its own
                    // precedent): its "denials" are precautionary, not provable. Every queried candidate is unverifiable.
                    // A null answer used to reach the catch-all below as a NullReferenceException; it is the same fault.
                    if (result is null)
                    {
                        _logger.LogError(
                            "[WF-AUTHZ] Deny-veto resolution for {EntityType}: the deny-list reader returned no answer for " +
                            "{Count} candidate(s). Failing CLOSED — they are unverifiable and removed (NFR-01).",
                            entityType, candidateRecords.Count);
                    }

                    unverifiable.UnionWith(candidateRecords.Select(c => c.RecordId));
                }
                else
                {
                    denied.UnionWith(result.DeniedRecordIds);
                }
            }

            return new DenyVetoResult(denied, unverifiable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WF-AUTHZ] Deny-veto resolution FAILED for {EntityType} ({Count} candidates). Failing " +
                "CLOSED — every queried candidate is unverifiable and removed; the veto is never skipped (NFR-01).",
                entityType, candidateIds.Count);
            return DenyVetoResult.AllUnverifiable(candidateIds);
        }
    }

    /// <summary>
    /// The systemuser plane's deny-list outcome (task 143): records removed WHOLE, and records on which only the
    /// contact-sourced contribution is removed (owner N3).
    /// </summary>
    private readonly record struct SystemUserDenyVeto(IReadOnlySet<Guid> RemoveWhole, IReadOnlySet<Guid> ContactSourcedOnly)
    {
        internal static SystemUserDenyVeto None { get; } = new(EmptyDeniedSet, EmptyDeniedSet);

        internal static SystemUserDenyVeto All(IEnumerable<Guid> ids) => new(ids.ToHashSet(), EmptyDeniedSet);
    }

    /// <summary>
    /// The FR-23 deny veto for a SYSTEMUSER composition (task 143 · owner Q4, N2, N3): three subjects — the
    /// systemuser itself (<c>sprk_subjectsystemuser</c>), the contacts that represent it, and those contacts'
    /// organizations — split by whether the record is secure.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Secure record</b> (or flags unreadable — an unreadable flag set counts as secure here whatever its other
    /// bits say): ANY matching entry removes the record WHOLE, membership term included. Q4: the wall binds internal users
    /// on secure records. Where Dataverse still admits the user through a team, a role or the business unit, this hides the
    /// record on Teams/SPA while MDA admits it — the owner's N2 answer, recorded as a §6.5 path-A exception.
    /// <para>The subjects for a secure record are the WRITE-TIME guard's own answer
    /// (<see cref="SecureShareNoAccessGuard"/>'s static <c>ResolveSubjectsAsync</c>, task 143 r1): the systemuser, its
    /// <c>sprk_primarycontact</c> link and every contact bound to its oid — each read STATUS-FIRST from
    /// <see cref="IContactIdentityStore"/> — and their wall organizations, plus the principal's derived contact. A
    /// faulted link, binding or membership read removes every secure candidate (ADR-003: "link unreadable → removed").
    /// Before r1 this took the contact from <c>principal.ContactId</c> alone, which <c>IIdentityNormalizationService</c>
    /// derives under a "never an exception" contract — a transient link-read fault read as "no contact", and a
    /// contact- or organization-subject wall on a secure record was never consulted (a fail-OPEN).</para></item>
    /// <item><b>Non-secure record</b>: a systemuser-subject entry removes NOTHING (Q4 scope). A derived-contact or
    /// organization entry removes only the contact-sourced contribution — the derived contact's grants — and the
    /// systemuser keeps its own term (owner N3: plane parity with MDA). The subjects are the derived contact and its
    /// organizations only: that contact is the one whose grants were composed, and a contact the normalizer could not
    /// derive contributed no grant to remove — so the normalizer's fault-swallowing cannot open anything here.</item>
    /// <item><b>Any fault removes the candidate whole</b> (ADR-003, criterion 11): an unreadable organization
    /// membership (every candidate), an unreadable link (every secure candidate), an unreadable record, a fail-closed
    /// reader answer, a denial with no provable subject kind, or a throw. Never "not walled".</item>
    /// </list>
    /// <para>Cost (NFR-02): a composition with no secure candidate makes exactly the reads it made before task 143 r1. One
    /// with a secure candidate adds the link reads (one systemuser read, plus one oid read when the user has an oid) and,
    /// when it ALSO has non-secure candidates and a contact subject, a second deny-list query for the non-secure batch.</para>
    /// </remarks>
    private async Task<SystemUserDenyVeto> ResolveSystemUserDenyVetoAsync(
        string entityType,
        IReadOnlyCollection<Guid> candidateIds,
        Guid systemUserId,
        Guid? linkedContactId,
        ActiveOrgMemberships linkedContactOrgs,
        IReadOnlyDictionary<Guid, RootRecordFlags> flags,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return SystemUserDenyVeto.None;
        }

        bool IsSecure(Guid id) => flags.TryGetValue(id, out var f) && (f.IsSecure || f.IsUnreadable);

        var anySecure = candidateIds.Any(IsSecure);
        var hasContact = linkedContactId is { } cid && cid != Guid.Empty;

        if (!anySecure && !hasContact && !linkedContactOrgs.Unreadable && linkedContactOrgs.WallSubjectOrganizationIds.Count == 0)
        {
            // Nothing could match: the systemuser subject binds only secure records, and there is no contact axis.
            return SystemUserDenyVeto.None;
        }

        if (linkedContactOrgs.Unreadable)
        {
            _logger.LogError(
                "[WF-AUTHZ] Systemuser deny veto for {SystemUserId} on {EntityType} ({Count} candidates) cannot proceed: " +
                "the linked contact's organizations were unreadable. Failing CLOSED — every candidate removed (NFR-01).",
                systemUserId, entityType, candidateIds.Count);
            return SystemUserDenyVeto.All(candidateIds);
        }

        try
        {
            var referencedOrgs = await _participations
                .GetReferencedOrganizationIdsAsync(entityType, candidateIds, ct).ConfigureAwait(false);

            var removeWhole = new HashSet<Guid>();
            var contactSourced = new HashSet<Guid>();
            var secureBatch = new List<NoAccessCandidateRecord>();
            var openBatch = new List<NoAccessCandidateRecord>();
            foreach (var recordId in candidateIds)
            {
                if (referencedOrgs.TryGetValue(recordId, out var refs) && refs.Unreadable)
                {
                    removeWhole.Add(recordId);
                    continue;
                }

                var orgIds = referencedOrgs.TryGetValue(recordId, out var resolved)
                    ? resolved.OrganizationIds
                    : Array.Empty<Guid>();
                (IsSecure(recordId) ? secureBatch : openBatch).Add(new NoAccessCandidateRecord(entityType, recordId, orgIds));
            }

            // ── Secure candidates: the write-time guard's subjects, read status-first (task 143 r1) ──
            if (secureBatch.Count > 0)
            {
                var subjects = await SecureShareNoAccessGuard.ResolveSubjectsAsync(
                        _identityStore, _participations, systemUserId,
                        hasContact ? linkedContactId : null,
                        hasContact ? linkedContactOrgs : null,
                        ct)
                    .ConfigureAwait(false);

                if (!subjects.Readable)
                {
                    _logger.LogError(
                        "[WF-AUTHZ] Systemuser deny veto for {SystemUserId} on {EntityType}: the {Fault} needed to know which " +
                        "contacts represent this user could not be read. Failing CLOSED — every SECURE candidate removed " +
                        "({Count}); never 'no contact'.",
                        systemUserId, entityType, subjects.Fault, secureBatch.Count);
                    removeWhole.UnionWith(secureBatch.Select(c => c.RecordId));
                }
                else
                {
                    var secureResult = await _noAccessList
                        .GetDeniedRecordsAsync(subjects.Subjects, secureBatch, ct).ConfigureAwait(false);
                    if (secureResult is null || secureResult.FailedClosed)
                    {
                        // A null answer is not a "nothing denied" answer; the reader never returns one. Treated as a fault.
                        removeWhole.UnionWith(secureBatch.Select(c => c.RecordId));
                    }
                    else
                    {
                        removeWhole.UnionWith(secureResult.DeniedRecordIds.Intersect(secureBatch.Select(c => c.RecordId)));
                    }
                }
            }

            // ── Non-secure candidates: the derived contact's grant contribution only (owner N3) ──
            if (openBatch.Count > 0 && (hasContact || linkedContactOrgs.WallSubjectOrganizationIds.Count > 0))
            {
                var openSubjects = new NoAccessSubjects(
                    hasContact ? new[] { linkedContactId!.Value } : Array.Empty<Guid>(),
                    linkedContactOrgs.WallSubjectOrganizationIds,
                    SystemUserId: null);

                var openResult = await _noAccessList.GetDeniedRecordsAsync(openSubjects, openBatch, ct).ConfigureAwait(false);
                if (openResult is null || openResult.FailedClosed)
                {
                    removeWhole.UnionWith(openBatch.Select(c => c.RecordId));
                }
                else
                {
                    foreach (var recordId in openResult.DeniedRecordIds)
                    {
                        var kinds = openResult.DenyingSubjectKinds.TryGetValue(recordId, out var k)
                            ? k
                            : NoAccessSubjectKinds.None;
                        if ((kinds & (NoAccessSubjectKinds.Contact | NoAccessSubjectKinds.Organization)) != 0)
                        {
                            contactSourced.Add(recordId);
                        }
                        else
                        {
                            // Denied with no provable contact or organization subject — treat as the strictest case.
                            removeWhole.Add(recordId);
                        }
                    }
                }
            }

            contactSourced.ExceptWith(removeWhole);
            return new SystemUserDenyVeto(removeWhole, contactSourced);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WF-AUTHZ] Systemuser deny veto FAILED for {SystemUserId} on {EntityType} ({Count} candidates). " +
                "Failing CLOSED — every candidate removed; the veto is never skipped (NFR-01).",
                systemUserId, entityType, candidateIds.Count);
            return SystemUserDenyVeto.All(candidateIds);
        }
    }

    /// <summary>
    /// Rows requested per membership round trip (unified-access-control-r2 task 015 / FR-14).
    /// <para>
    /// DELIBERATELY DECOUPLED from <see cref="MembershipResolveOptions.MaxLimit"/>, the
    /// completeness ceiling. Collapsing the two (page size == ceiling) would make the
    /// continuation loop unreachable in practice and turn any test of it into a tautology —
    /// the page would always "fill" exactly at the ceiling, so a cap check and a page-full
    /// check would be indistinguishable. Keeping page size (500) strictly below the ceiling
    /// (5,000) means a caller with 501..5,000 memberships genuinely pages, which is the case
    /// A-10 silently truncated.
    /// </para>
    /// <para>
    /// Round-trip cost: callers at or below one page (the overwhelming majority) still cost
    /// exactly ONE round trip, unchanged from the pre-fix behaviour. Extra round trips are
    /// incurred only by callers whose access was previously being silently discarded.
    /// </para>
    /// </summary>
    internal const int MembershipPageSize = MembershipResolveOptions.DefaultLimit;

    /// <summary>
    /// Hard ceiling on continuation round trips, independent of how many ids come back.
    /// A resolver that kept reporting "more" while returning nothing new (or the same page)
    /// would otherwise spin; the id-count ceiling alone cannot bound that, because a
    /// no-progress loop never grows the id count. +2 covers the partial first page and the
    /// zero-row confirmation page that <c>BuildNextContinuationToken</c>'s belt can produce.
    /// </summary>
    private const int MaxMembershipPages = (MembershipResolveOptions.MaxLimit / MembershipPageSize) + 2;

    private readonly IMembershipResolverService _membership;
    private readonly ExternalParticipationService _participations;
    private readonly ISubjectStandingGrantReader _standingGrant;
    private readonly INoAccessListReader _noAccessList;
    private readonly IContactIdentityStore _identityStore;
    private readonly ISystemUserIdentityResolver _systemUsers;
    private readonly ILogger<AccessibleRecordSetService> _logger;

    /// <param name="membership">ADR-034 membership.</param>
    /// <param name="participations">Grants, flags, organization reads.</param>
    /// <param name="standingGrant">The standing-grant reader.</param>
    /// <param name="noAccessList">The deny-list reader.</param>
    /// <param name="identityStore">The status-bearing systemuser↔contact link reads (task 143 r1): the systemuser-plane
    /// veto resolves a SECURE candidate's subjects through them, so a faulted link read removes the record instead of
    /// reading as "no contact".</param>
    /// <param name="systemUsers">The authoritative <c>sprk_isexternal</c> read (task 114 verifier K1): a systemuser flagged
    /// external keeps no membership-term access to a Restricted record on this plane.</param>
    /// <param name="logger">Logger.</param>
    public AccessibleRecordSetService(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        ISubjectStandingGrantReader standingGrant,
        INoAccessListReader noAccessList,
        IContactIdentityStore identityStore,
        ISystemUserIdentityResolver systemUsers,
        ILogger<AccessibleRecordSetService> logger)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(participations);
        ArgumentNullException.ThrowIfNull(standingGrant);
        ArgumentNullException.ThrowIfNull(noAccessList);
        ArgumentNullException.ThrowIfNull(identityStore);
        ArgumentNullException.ThrowIfNull(systemUsers);
        ArgumentNullException.ThrowIfNull(logger);
        _membership = membership;
        _participations = participations;
        _standingGrant = standingGrant;
        _noAccessList = noAccessList;
        _identityStore = identityStore;
        _systemUsers = systemUsers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccessibleRecordSet> ComposeAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("entityType must not be null/empty/whitespace.", nameof(entityType));
        }

        return principal.Kind switch
        {
            WorkforcePrincipalKind.SystemUser => await ComposeForSystemUserAsync(principal, entityType, ct)
                .ConfigureAwait(false),
            WorkforcePrincipalKind.ContactOnly => await ComposeForContactAsync(principal, entityType, ct)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(principal), principal.Kind, "Unknown workforce principal kind."),
        };
    }

    /// <inheritdoc />
    public Task<AccessibleRecordSet> ComposeForCiamContactAsync(
        Guid contactId, string entityType, CancellationToken ct)
    {
        if (contactId == Guid.Empty)
        {
            // An unresolved caller is denied by the strategy before it gets here; an empty id reaching this
            // point is a caller bug, and composing for it would read as "a contact with no grants".
            throw new ArgumentException("contactId must be a resolved contact id.", nameof(contactId));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("entityType must not be null/empty/whitespace.", nameof(entityType));
        }

        return ComposeContactPlaneAsync(contactId, entityType, includeDerivedMemberTerms: false, ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsRecordAccessibleAsync(
        WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct)
    {
        if (recordId == Guid.Empty)
        {
            // No record to evaluate — cannot prove access; deny (fail-closed).
            return false;
        }

        var set = await ComposeAsync(principal, entityType, ct).ConfigureAwait(false);
        return set.Contains(recordId);
    }

    /// <inheritdoc />
    public async Task<bool> IsOperationPermittedAsync(
        WorkforcePrincipal principal,
        string entityType,
        Guid recordId,
        AccessRights requiredRights,
        CancellationToken ct)
    {
        if (recordId == Guid.Empty)
        {
            // No record to evaluate — cannot prove the rights; deny (fail-closed).
            return false;
        }

        // ⚠️ `AccessRights.None` is NOT "no requirement" — it is a caller bug, and it must not pass.
        //
        // AccessRights is a [Flags] enum, so `anything.HasFlag(None)` is ALWAYS true (zero is a subset
        // of every set). Without this guard, a call site that computed its requirement dynamically and
        // arrived at None — an unmapped operation, a defaulted field, a mis-parsed config value —
        // would be granted permission on ANY record, including one the caller cannot see at all. That
        // is a fail-OPEN reachable purely by a caller mistake, on the one method whose entire job is to
        // deny. Asking "may I do nothing?" gets No.
        if (requiredRights == AccessRights.None)
        {
            _logger.LogError(
                "[WF-AUTHZ] IsOperationPermittedAsync called with requiredRights=None for {EntityType} " +
                "record {RecordId}; denying. This is a CALLER BUG — an operation must name the rights " +
                "it needs. (HasFlag(None) is always true, so permitting here would grant every record.)",
                entityType, recordId);
            return false;
        }

        var set = await ComposeAsync(principal, entityType, ct).ConfigureAwait(false);

        // RightsFor is None for an absent record, so out-of-set is denied by the same expression —
        // there is no separate membership branch that could drift from the rights branch.
        return set.RightsFor(recordId).HasFlag(requiredRights);
    }

    /// <inheritdoc />
    public async Task<NoAccessCheckAnswer> CheckGranteeNoAccessAsync(
        string entityType,
        Guid recordId,
        Guid? granteeContactId,
        IReadOnlyCollection<Guid> granteeOrganizationIds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType) || recordId == Guid.Empty)
        {
            // No record to check against is a caller bug: the check cannot be evaluated — refused, and reported as the
            // fault it is (never as an entry on a list nobody read).
            _logger.LogError(
                "[WF-AUTHZ] CheckGranteeNoAccessAsync called without a record ({EntityType} {RecordId}); UNVERIFIABLE — " +
                "refusing the grant (fail closed).", entityType, recordId);
            return NoAccessCheckAnswer.Unverifiable;
        }

        try
        {
            // The contact's OWN memberships, read once by the same reader the composition uses (statecode-only wall set,
            // owner D-2 part 2 / D-10). None when there is no contact — not a fault. A fault is ActiveOrgMemberships.Failed.
            var contactOrgs = await ReadActiveOrgMembershipsAsync(granteeContactId, ct).ConfigureAwait(false);

            // The caller-supplied organizations (an org-wide grant's organization, or the firm a contact grant names) join
            // the WALL set only — never the conferring set. Over-matching is the specified direction for a veto (B-10).
            var wall = contactOrgs.WallSubjectOrganizationIds
                .Concat(granteeOrganizationIds ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var subjectOrgs = contactOrgs with { WallSubjectOrganizationIds = wall };

            var veto = await ResolveDenyVetoAsync(entityType, new[] { recordId }, granteeContactId, subjectOrgs, ct)
                .ConfigureAwait(false);

            if (veto.Denied.Contains(recordId))
            {
                _logger.LogWarning(
                    "[WF-AUTHZ] Write-time No Access check DENIES {EntityType} {RecordId} to contact {ContactId} / " +
                    "organizations {OrganizationIds} (a matching entry).",
                    entityType, recordId, granteeContactId, string.Join(",", wall));
                return NoAccessCheckAnswer.Denied;
            }

            if (veto.Unverifiable.Contains(recordId))
            {
                _logger.LogError(
                    "[WF-AUTHZ] Write-time No Access check for {EntityType} {RecordId} (contact {ContactId} / " +
                    "organizations {OrganizationIds}) is UNVERIFIABLE: an input could not be read. Refusing (fail closed); " +
                    "reported as a fault, not as an entry.",
                    entityType, recordId, granteeContactId, string.Join(",", wall));
                return NoAccessCheckAnswer.Unverifiable;
            }

            return NoAccessCheckAnswer.Allowed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The veto code rethrows every OperationCanceledException so a read-path composition throws rather than
            // answer — an HttpClient timeout among them. Here the CALLER did not cancel: it is a read fault like any other.
            _logger.LogError(ex,
                "[WF-AUTHZ] Write-time No Access check for {EntityType} {RecordId} (contact {ContactId}) could not be " +
                "completed; UNVERIFIABLE — refusing (fail closed), reported as a fault.",
                entityType, recordId, granteeContactId);
            return NoAccessCheckAnswer.Unverifiable;
        }
    }

    /// <summary>
    /// The outcome of following a membership stream to its end (or to the ceiling).
    /// </summary>
    /// <param name="Ids">Every id read across all pages, de-duplicated.</param>
    /// <param name="Capped">
    /// <c>true</c> iff the loop stopped with more records still available — the set is KNOWN
    /// INCOMPLETE (NFR-03). Exhausting the stream leaves this <c>false</c> even if the id
    /// count lands exactly on the ceiling.
    /// </param>
    /// <param name="Pages">Round trips performed (observability + round-trip-cost assertions).</param>
    private readonly record struct MembershipPageWalk(HashSet<Guid> Ids, bool Capped, int Pages);

    /// <summary>
    /// Reads a membership stream to completion by following continuation tokens, instead of
    /// taking only the first page (unified-access-control-r2 task 015 · finding A-10 · FR-14).
    /// <para>
    /// The defect this replaces: both composers called the resolver with <c>options: null</c>,
    /// which clamps to a 500-row default, and then used <c>response.Ids</c> while DISCARDING
    /// <c>response.ContinuationToken</c>. A systemuser on 900 matters got 500 of them and was
    /// DENIED the other 400, with nothing anywhere reporting that a set had been cut. That is
    /// a fail-closed under-grant: availability/correctness, not disclosure — but silent, which
    /// is what NFR-03 forbids.
    /// </para>
    /// <para>
    /// Termination is over-determined ON PURPOSE (ADR-003: bounded, never unbounded):
    ///   (1) the resolver reports no further pages — the normal, complete exit;
    ///   (2) the id ceiling <see cref="MembershipResolveOptions.MaxLimit"/> is reached — one
    ///       bounded confirmation read decides complete-at-the-ceiling vs genuinely-capped;
    ///       never keep reading past it;
    ///   (3) a page adds no new ids yet claims more — a non-advancing cursor; stop and flag
    ///       rather than spin (the id ceiling alone cannot catch this, since a no-progress
    ///       loop never grows the count);
    ///   (4) <see cref="MaxMembershipPages"/> round trips — a blunt backstop that holds even
    ///       if (1)-(3) are all defeated.
    /// Only (1) yields a complete set; (2)-(4) all set <c>Capped</c>.
    /// </para>
    /// <para>
    /// Errors are NOT caught here. If a page throws, the exception propagates and the caller
    /// denies wholesale. Swallowing it would hand back the pages read so far as though they
    /// were the complete set — a partial set presented as authoritative, which is strictly
    /// worse than a loud failure.
    /// </para>
    /// </summary>
    /// <param name="accessConferringOnly">
    /// FR-24 (task 043) — narrow discovered descriptors to the access-conferring column registry.
    /// Passed <c>true</c> by the systemuser plane, which is making an ACCESS decision; ignored by
    /// <c>ResolveByContactAsync</c>, which applies the registry filter unconditionally.
    /// </param>
    /// <param name="identityTypes">
    /// Narrows which identity types may bind. Used by the org-expansion term to request org-typed
    /// descriptors ONLY (see <see cref="OrganizationIdentityTypeOnly"/>).
    /// </param>
    /// <param name="organizationIds">
    /// FR-24 + FR-25 (task 043) — the organizations to bind into the resolved identity, so org-typed
    /// descriptors emit conditions. Already resolved by the caller; see
    /// <see cref="ReadActiveOrgMembershipsAsync"/>.
    /// </param>
    private async Task<MembershipPageWalk> WalkMembershipPagesAsync(
        Func<MembershipResolveOptions, CancellationToken, Task<MembershipResponse>> readPage,
        string entityType,
        string principalDescription,
        CancellationToken ct,
        bool accessConferringOnly = false,
        IReadOnlyList<string>? identityTypes = null,
        IReadOnlyList<Guid>? organizationIds = null)
    {
        var ids = new HashSet<Guid>();
        string? token = null;
        var pages = 0;
        var capped = false;

        // ⚠️ Every page of a walk MUST carry identical options apart from the continuation token —
        // including these three. The resolver's cache id is keyed on the options hash, so a page that
        // dropped one of them would be looked up, and stored, under a DIFFERENT key than its siblings.
        MembershipResolveOptions PageOptions(string? continuation) => new(
            IdentityTypes: identityTypes,
            Limit: MembershipPageSize,
            ContinuationToken: continuation,
            AccessConferringOnly: accessConferringOnly,
            OrganizationIds: organizationIds);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await readPage(PageOptions(token), ct).ConfigureAwait(false);
            pages++;

            var before = ids.Count;
            foreach (var id in response.Ids)
            {
                ids.Add(id);
            }
            var added = ids.Count - before;

            token = response.ContinuationToken;

            // (1) Stream exhausted — the ONLY complete exit.
            if (token is null)
            {
                break;
            }

            // (2) Ceiling reached (NFR-03).
            if (ids.Count >= MembershipResolveOptions.MaxLimit)
            {
                // Holding a token at the ceiling does NOT by itself prove more records exist:
                // BuildNextContinuationToken deliberately emits one whenever a page came back
                // FULL, so that a provider under-reporting MoreRecords cannot truncate us
                // silently. A result set that is an exact multiple of the page size therefore
                // ends on a full page plus a token, and is nonetheless COMPLETE.
                //
                // Guessing is wrong in both directions: assume "capped" and every caller whose
                // membership count lands exactly on the ceiling is told records are hidden that
                // are not; assume "complete" and the silent truncation A-10 describes comes
                // straight back. So spend ONE bounded confirmation round trip and know.
                // Its rows are deliberately NOT merged — if more exists we are capped, and the
                // count must stay at the ceiling the NFR-03 message quotes.
                var confirmation = await readPage(PageOptions(token), ct).ConfigureAwait(false);
                pages++;

                capped = confirmation.ContinuationToken is not null
                         || confirmation.Ids.Any(id => !ids.Contains(id));

                if (capped)
                {
                    _logger.LogWarning(
                        "[WF-AUTHZ] Membership composition for {Principal} on {EntityType} hit the " +
                        "{CapLimit}-record ceiling after {Pages} page(s) with more records available. " +
                        "The accessible set is INCOMPLETE and is flagged capped (NFR-03).",
                        principalDescription, entityType, MembershipResolveOptions.MaxLimit, pages);
                }

                break;
            }

            // (3) Cursor claims more but produced nothing new — do not spin.
            if (added == 0)
            {
                capped = true;
                _logger.LogWarning(
                    "[WF-AUTHZ] Membership composition for {Principal} on {EntityType} stopped after " +
                    "{Pages} page(s): the resolver reported a further page but returned no new ids. " +
                    "Treating the set as INCOMPLETE (capped) rather than paging indefinitely.",
                    principalDescription, entityType, pages);
                break;
            }

            // (4) Blunt round-trip backstop.
            if (pages >= MaxMembershipPages)
            {
                capped = true;
                _logger.LogWarning(
                    "[WF-AUTHZ] Membership composition for {Principal} on {EntityType} reached the " +
                    "{MaxPages}-page round-trip backstop with more records available. The accessible " +
                    "set is INCOMPLETE and is flagged capped (NFR-03).",
                    principalDescription, entityType, MaxMembershipPages);
                break;
            }
        }

        return new MembershipPageWalk(ids, capped, pages);
    }

    // ── systemuser plane: ADR-034 membership ∪ the caller's own contact grants ───────────────────
    // (§6.5 Path-B amendment of design §5, spaarke-SPA-external-access-platform-r2 UAT 2026-08-07,
    //  owner directive — "parallel workforce/contact access"): an internal system-user who is ALSO a
    //  granted contact sees BOTH their ADR-034 membership AND their contact's project grants, so
    //  internal staff can sign in to the external SPA to "see what's there" and shepherd external
    //  users. Still strictly the person's OWN access on both planes — never "all projects" (NFR-08).
    private async Task<AccessibleRecordSet> ComposeForSystemUserAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        // A systemuser principal always carries a systemuserid (task 020 invariant).
        var systemUserId = principal.SystemUserId
            ?? throw new InvalidOperationException(
                "A SystemUser principal must carry a SystemUserId (task 020 invariant).");

        // FR-14: follow continuation tokens to the end of the stream. Passing `options: null`
        // here (the pre-fix shape) took only the first 500 rows and dropped the rest.
        //
        // ── accessConferringOnly: true — FR-24, task 043 ───────────────────────────────────────────
        // This is an ACCESS decision, so the membership term is narrowed to the registry-listed
        // conferring columns. Until now the systemuser plane consumed EVERY discovered descriptor as
        // an access answer, and the contact plane did not: an internal user whose linked contact
        // appears in an ADVERSE lookup (opposing counsel), or whose employer account / business unit
        // is referenced by an account- or BU-typed lookup, counted as "a member" of that record for
        // gate purposes (register A-8; investigation 02 §4.2, which names closing this asymmetry as
        // UAC-r2's job). The contact plane has had this guard since it existed.
        //
        // ⚠️ This SHRINKS the composed set. That is the intended direction, but it is a BEHAVIOUR
        // change for internal callers, not a refactor.
        //
        // NOTE on the task brief: the POML describes this as "the flag-off branch of 036". There is no
        // such branch — 036 is still open (and gated behind 034), so this single call IS the membership
        // path today. 036 will add the impersonated Dataverse answer alongside it.
        var walk = await WalkMembershipPagesAsync(
            (options, token) => _membership.ResolveAsync(systemUserId, entityType, options, token),
            entityType,
            $"systemuser {systemUserId}",
            ct,
            accessConferringOnly: true).ConfigureAwait(false);

        // ── ADDITIVE TERMS (design §4.5) ───────────────────────────────────────────────────────────
        // Each term contributes (recordId -> rights); AccumulateTerm merges them highest-wins.
        var composed = new Dictionary<Guid, AccessRights>();

        // Resolve the caller's contact + grants FIRST, so the candidate id set is complete before the
        // single batched flag read.
        //
        // ⚠️ The contact comes ONLY from the systemuser↔contact link (task 141). There used to be an EMAIL
        // fallback here — ResolveExternalContactAsync(oid: null, email) — that returned an UNBOUND contact on
        // an email match with $top=1, no ambiguity check and no binding, so a licensed user inherited the
        // grants of any unbound contact that carried their email. A user with no link gets their link from
        // ContactIdentityBinder (inline at first resolution, or the identity-link reconciliation job); until
        // then they have membership only — less access, never someone else's.
        var contactGrantsApplied = false;
        ExternalGrantSet? grants = null;
        // Hoisted out of the `if` below (task 039) so the SAME resolved contact identity that fed the
        // grant term also feeds the deny-veto subject — "who is checked against the wall" must never
        // diverge from "whose grants applied".
        Guid? grantContactId = null;
        if (IsGrantSupported(entityType))
        {
            grantContactId =
                principal.ContactId is { } cid && cid != Guid.Empty ? cid : null;

            if (grantContactId is { } resolved && resolved != Guid.Empty)
            {
                grants = await _participations.GetGrantSetAsync(resolved, ct).ConfigureAwait(false);
                contactGrantsApplied = true;

                // Task 137 · defect C5: the linked contact's grants confer nothing while that contact is not
                // Active — read LIVE, and only when the grants could contribute anything (NFR-02). The contact stays
                // the deny-veto subject below (a wall naming it keeps binding), and the systemuser's own membership
                // term is untouched: internal access is Dataverse's answer, not the contact's.
                if (GrantedIdsFor(grants, entityType).Any())
                {
                    var linkedState = await _participations.ReadContactStateAsync(resolved, ct).ConfigureAwait(false);
                    if (linkedState != ContactRecordState.Active)
                    {
                        _logger.LogWarning(
                            "[WF-AUTHZ] Systemuser {SystemUserId}'s linked contact {ContactId} is {State}: its grants on " +
                            "{EntityType} confer nothing; the systemuser's own membership is unaffected (task 137).",
                            systemUserId, resolved, linkedState, entityType);
                        grants = null;
                        contactGrantsApplied = false;
                    }
                }
            }
        }

        // ── FLAGS: ONE batched read over every candidate id (NFR-02) ───────────────────────────────
        var candidates = walk.Ids
            .Concat(grants is null ? Enumerable.Empty<Guid>() : GrantedIdsFor(grants, entityType))
            .ToList();
        var flags = await _participations
            .GetRootRecordFlagsAsync(entityType, candidates, ct).ConfigureAwait(false);
        var isDirectOnly = DirectOnlyPredicate(flags);

        // Term 1 — ADR-034 membership. NOT contact-sourced, so it survives BOTH vetoes: it is the
        // systemuser's own Dataverse-governed access, which is exactly what Restricted preserves
        // ("only system users may have access") and what Secure and Limited leave alone (the Secure BU covers
        // the Dataverse half; the suppression covers the grant half — design §5.1). Task 138: Limited governs
        // which CONTACT grant types count, never internal access, so this term never consults isDirectOnly.
        var membershipTerm = walk.Ids
            .Select(id => KeyValuePair.Create(id, MembershipTermRights))
            .ToList();
        AccumulateTerm(composed, membershipTerm);

        // Term 2 — contact grants, with direct-only suppression applied BEFORE the max: on a Secure or
        // Limited record only the caller's OWN grant rows contribute; org-inherited access is suppressed
        // (FR-22; Limited since task 138).
        //
        // ⚠️ This applies on the SYSTEMUSER plane too, deliberately. A Type 1 user whose linked contact
        // holds an org grant would otherwise derive access to a secure record through the contact term —
        // access Dataverse knows nothing about, so the Secure BU cannot catch it (design §5.1, register C-10).
        if (grants is not null)
        {
            AccumulateTerm(composed, GrantedRightsFor(grants, entityType, isDirectOnly));
        }

        // ── VETOES, after the max, in order: deny-list (task 039) → Restricted (task 037) ──────────
        // Deny-veto subject = this principal's OWN resolved contact identity (the SAME one the grant
        // term used above) + that contact's active organizations (task 039 / FR-23). The membership
        // term is what survives Restricted on this plane.
        //
        // The organization read is hoisted here (task 043) so both planes resolve it identically and
        // exactly once. Org EXPANSION itself is NOT applied on this plane: design §5 composes a
        // systemuser as ADR-034 membership ∪ the caller's own contact grants, and the org-derived
        // access a Type 1 user can reach through their linked contact is the org-INHERITED GRANT —
        // which term 2 above already suppresses on a Secure or Limited record via DirectAccessLevel (FR-22).
        // Adding a second org path here would invent access design §5 does not give.
        // ⚠️ Gated on there being candidates at all (NFR-02, corrected by task 043's code-review gate).
        // Hoisting this read moved it ABOVE ResolveDenyVetoAsync's `candidateIds.Count == 0`
        // early-return, so without this condition a composition with nothing to evaluate would perform
        // a junction read it previously skipped — and IsRecordAccessibleAsync / IsOperationPermittedAsync
        // call ComposeAsync once per authorization check, so that cost multiplies per decision.
        //
        // Skipping to `None` rather than `Failed` is correct here and is not a fail-open: the veto
        // itself returns EmptyDeniedSet for an empty candidate list, so the value is never consulted.
        //
        // ── Task 132 (defect C12): an UNKNOWN veto subject denies; an ABSENT one checks nothing ─────────────────
        // The subject here is the systemuser's linked contact. When the reads that decide it FAILED
        // (principal.ContactUnreadable — the systemuser row or the oid-binding lookup), the contact is unknown,
        // not absent: ResolveDenyVetoAsync would see no subject and check nothing, so a No Access entry naming
        // this person would stop applying to their membership-term access (traced, task 132 notes §4). That is
        // the ISS-019 shape on the contact axis, so it gets ISS-019's answer: deny every candidate, exactly as
        // ActiveOrgMemberships.Failed does. A user READ as having no linked contact keeps today's path (no
        // subject, nothing to check) — 7 of 8 dev systemusers are in that state and lose nothing.
        // Only on a grant-supported root type: elsewhere the contact is never the veto subject (grantContactId stays
        // null above even when the contact IS known), so an unknown contact cannot change the answer and denying on it
        // would refuse what a known contact keeps (verifier r1, item 11).
        //
        // Batch 4 integration (task 132 x task 143): the unknown-subject answer is task 143's three-subject veto's own
        // "remove whole" (SystemUserDenyVeto.All). Otherwise the organizations are read and task 143's veto decides.
        // Nothing here is cached: the composition is computed per call, so a fault-derived (fail-closed) veto is never
        // stored for a later request. Task 142 r4's tri-state: an unreadable contact is a FAULT, never a provable entry —
        // on this read path both remove the candidates (fail closed; ResolveDenyVetoAsync's DenyVetoResult.Removed does
        // the same). The write-time CheckGranteeNoAccessAsync never sees a principal's contact: its subject is the
        // caller-supplied grantee, and an unreadable read there (memberships, referenced organizations, the reader)
        // answers NoAccessCheckAnswer.Unverifiable, never Denied.
        SystemUserDenyVeto veto;
        if (principal.ContactUnreadable && IsGrantSupported(entityType) && candidates.Count > 0)
        {
            _logger.LogError(
                "[WF-AUTHZ] Deny-veto subject for systemuser {SystemUserId} on {EntityType} is UNREADABLE (the linked-" +
                "contact read failed): the veto is UNVERIFIABLE. Failing CLOSED — removing every queried candidate ({Count}); the veto is never " +
                "skipped (NFR-01, task 132).",
                systemUserId, entityType, candidates.Count);
            veto = SystemUserDenyVeto.All(candidates);
        }
        else
        {
            var activeOrgs = candidates.Count == 0
                ? ActiveOrgMemberships.None
                : await ReadActiveOrgMembershipsAsync(grantContactId, ct).ConfigureAwait(false);

            // Task 143 (owner Q4, N2, N3): three subjects — this systemuser, its linked contact, that contact's
            // organizations — split by whether the record is secure. See ResolveSystemUserDenyVetoAsync.
            veto = await ResolveSystemUserDenyVetoAsync(
                    entityType, candidates, systemUserId, grantContactId, activeOrgs, flags, ct)
                .ConfigureAwait(false);
        }

        ApplyVetoPipeline(
            composed,
            veto.RemoveWhole,
            flags,
            await SurvivesRestrictedForSystemUserAsync(systemUserId, entityType, membershipTerm, flags, ct).ConfigureAwait(false),
            veto.ContactSourcedOnly);

        // Last: no key without Read (task 136 · C2). On this plane the reachable case is the linked contact's
        // organization-only grant on a Secure or Limited root, which term 2 enters at None.
        RemoveEntriesWithoutRead(composed);

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for systemuser {SystemUserId} on {EntityType}: " +
            "{Count} records over {Pages} membership page(s) (ADR-034 membership; contact-grants " +
            "union applied: {ContactGrants}; capped: {Capped}).",
            systemUserId, entityType, composed.Count, walk.Pages, contactGrantsApplied, walk.Capped);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.SystemUser,
            EntityType = entityType,
            Rights = composed,
            Capped = walk.Capped,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: true, ContactGrants: contactGrantsApplied, StandingGrantMembership: false),
        };
    }

    // ── workforce contact plane: grants ∪ (standing membership IFF flag set) ∪ org expansion ─────
    private Task<AccessibleRecordSet> ComposeForContactAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        // A contact-only principal always carries a contactId anchor (task 020 invariant).
        var contactId = principal.ContactId
            ?? throw new InvalidOperationException(
                "A ContactOnly principal must carry a ContactId anchor (task 020 invariant).");

        return ComposeContactPlaneAsync(contactId, entityType, includeDerivedMemberTerms: true, ct);
    }

    /// <summary>
    /// The ONE contact-plane composition, shared by both contact sign-ins (task 135 · defect C1): the
    /// workforce contact-only principal (<see cref="ComposeForContactAsync"/>) and the CIAM contact
    /// (<see cref="ComposeForCiamContactAsync"/>).
    /// </summary>
    /// <remarks>
    /// Before task 135 only the workforce plane came through here; the CIAM strategy built its principal
    /// from the grant set alone, so a contact on the No Access List, holding a grant on a Restricted record,
    /// or inheriting an organization grant on a Secure record kept full access on a ciamlogin.com token.
    /// Both planes now run the same grant read, the same single junction read, the same batched flag read,
    /// the same Secure-suppressed grant term, the same deny resolution and the same
    /// <see cref="ApplyVetoPipeline"/> call. The only difference is
    /// <paramref name="includeDerivedMemberTerms"/>, named at its branch point below.
    /// </remarks>
    private async Task<AccessibleRecordSet> ComposeContactPlaneAsync(
        Guid contactId, string entityType, bool includeDerivedMemberTerms, CancellationToken ct)
    {
        var composed = new Dictionary<Guid, AccessRights>();

        // ── Task 137 · defect C5: an INACTIVE contact confers nothing — read LIVE, first ───────────
        // Every term on this plane is contact-sourced (grants, standing membership, organization expansion), so a
        // contact that is not Active composes to the EMPTY set, on both sign-ins. The state is read live on every
        // composition — not from the 60-second grant cache nor the identity cache (2 min, task 132) — so a contact
        // deactivated after sign-in loses access on its next request. Unreadable or missing is not Active (fail
        // closed, ADR-003). Read before anything else so a deactivated contact costs no grant read, no membership
        // walk and no flag read.
        var contactState = await _participations.ReadContactStateAsync(contactId, ct).ConfigureAwait(false);
        if (contactState != ContactRecordState.Active)
        {
            _logger.LogWarning(
                "[WF-AUTHZ] Contact {ContactId} is {State}: its {Plane} contact-plane composition on {EntityType} is " +
                "EMPTY — an inactive or unreadable contact confers nothing (task 137).",
                contactId, contactState, includeDerivedMemberTerms ? "workforce" : "CIAM", entityType);

            return new AccessibleRecordSet
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = entityType,
                Rights = composed,
                Capped = false,
                Sources = new AccessibleRecordSetSources(
                    SystemUserMembership: false, ContactGrants: false, StandingGrantMembership: false),
            };
        }

        // Read grants + standing membership + org expansion FIRST so the candidate id set is complete
        // before the single batched flag read (NFR-02).
        var grantsApplied = false;
        ExternalGrantSet? grants = null;
        if (IsGrantSupported(entityType))
        {
            grants = await _participations.GetGrantSetAsync(contactId, ct).ConfigureAwait(false);
            grantsApplied = true;
        }

        // ── THE ONE PERMITTED DIFFERENCE BETWEEN THE TWO CONTACT PLANES (task 135) ─────────────────
        // includeDerivedMemberTerms is true for the workforce contact plane and false for CIAM. It gates
        // the two DERIVED-MEMBER terms below — standing-grant membership and organization expansion —
        // and nothing else: the grant term, Secure suppression, the deny veto and Restricted are shared.
        //
        // Why the difference exists: CIAM has never had these terms, and the owner's model is that a
        // contact gets only what it is granted (C9). Retiring them from the workforce plane was put to the
        // owner as task 142 (C9, Assigned-To auto-grants) escalation (d). The owner answered in decision A2
        // (round 3, 2026-09-30, REVERSED): "Standing grants and organization access STAY, as they work
        // today, on both sign-in types. Assigned-To grants are ADDED alongside them. Nothing is retired."
        // So this difference is an owner-signed exception — task 135 escalation option (ii) — not a
        // pending retirement. Adding the terms to CIAM would widen CIAM beyond what it has today;
        // removing them from the workforce plane would retire access the owner said stays. Either change
        // is a new owner decision, not a refactor (notes/task-135-ciam-unified-veto-pipeline.md).
        //
        // ── ONE junction read, shared by the additive org term AND the deny-veto subject (NFR-02) ──
        // On the workforce plane it is hoisted above the membership walk because the contact's active
        // organizations are an INPUT to it: an org-typed descriptor can only emit a condition if the
        // identity carries org ids. The one read yields both NAMED sets (task 109): the org term below
        // reads ConferringOrganizationIds, the veto reads WallSubjectOrganizationIds. Without the derived
        // terms (CIAM) the veto is its only consumer, so it is read below, once there are candidates —
        // the systemuser plane's NFR-02 gate. Null here means "not read yet", never "no organizations".
        ActiveOrgMemberships? activeOrgs = includeDerivedMemberTerms
            ? await ReadActiveOrgMembershipsAsync(contactId, ct).ConfigureAwait(false)
            : null;

        // Standing-grant runtime membership, GATED on the subject-level policy flag. The negative case is
        // load-bearing: a contact WITHOUT a standing grant gets ONLY the explicit grants — NEVER automatic
        // membership. (task-051 seam: ISubjectStandingGrantReader.)
        var standingApplied = false;
        var capped = false;
        var membershipPages = 0;
        var standingIds = new HashSet<Guid>();

        // FR-25 (task 042): the standing grant is LEVEL-BEARING. One read returns both the flag and the
        // subject's sprk_accesspermissiongrant baseline; `Rights` routes it through task 032's single
        // ExternalAccessLevel→AccessRights mapping.
        //
        // 🔴 Rights == None short-circuits the whole term — the membership walk is not even performed.
        // Two reasons: a term that can contribute nothing has no records worth enumerating (NFR-02), and
        // per the owner's 2026-09-10 decision an EMPTY baseline contributes nothing, which must mean the
        // records are ABSENT from the accessible set rather than present with zero rights. AccumulateTerm
        // would have entered them at None, and "present but powerless" is a different — worse — answer
        // than "not accessible": it is exactly the shape that makes a UI render a row the caller cannot
        // act on. See notes/task-042-standing-grant-levels.md §4.
        //
        // Derived-member term — not composed, and the flag not even read, on CIAM (see the plane note above).
        var standingRights = AccessRights.None;
        if (includeDerivedMemberTerms)
        {
            var standing = await _standingGrant.ReadForContactAsync(contactId, ct).ConfigureAwait(false);
            standingRights = standing.Rights;
        }

        if (standingRights != AccessRights.None)
        {
            // FR-14: same continuation-following fix as the systemuser plane — the pre-fix
            // `options: null` call silently capped a standing-grant contact at 500 records.
            var walk = await WalkMembershipPagesAsync(
                (options, token) => _membership.ResolveByContactAsync(contactId, entityType, options, token),
                entityType,
                $"contact {contactId}",
                ct).ConfigureAwait(false);

            standingIds = walk.Ids;
            capped = walk.Capped;
            membershipPages = walk.Pages;
            standingApplied = true;
        }

        // ── ORG EXPANSION (design §4.5 term 4 / FR-24 + FR-25, task 043) ───────────────────────────
        //
        // A contact derives membership of records that reference — via a REGISTRY-LISTED org-typed
        // lookup — an organization the contact actively belongs to, at THAT ORGANIZATION's
        // standing-grant baseline.
        //
        // Resolved as one walk per DISTINCT baseline. Neither obvious alternative is right:
        //   • one walk per organization costs N queries for information that varies only by baseline,
        //     and FR-25 defines exactly three baselines, so N collapses to at most 3;
        //   • ONE walk for all organizations could credit only a single level, so a record reachable
        //     ONLY through a View Only firm would silently inherit an unrelated Full Access firm's
        //     rights. That over-grant is undetectable downstream — the record ids are identical
        //     either way, and only the level differs.
        // Each walk narrows to org-typed descriptors (OrganizationIdentityTypeOnly) so the
        // always-bound ContactId cannot drag contact-derived records in at an organization's level.
        //
        // Independent of the contact's OWN standing grant: this term is the ORGANIZATION's standing
        // arrangement, so it applies whether or not the contact personally holds one.
        //
        // An ADDITIVE term, so it reads the CONFERRING set only (task 109): a membership ended by date,
        // not yet started, or under an inactive organization derives nothing (owner D-2 part 1, D-10,
        // ISS-026) — while the veto below still treats it as a wall subject. On an unreadable junction
        // the conferring set is empty, so the fault contributes nothing.
        //
        // Derived-member term — on CIAM `activeOrgs` is still unread (null) here, so this never runs there
        // (see the plane note above).
        var orgTerms = new List<(AccessRights Rights, HashSet<Guid> RecordIds)>();
        var orgExpansionApplied = false;
        if (includeDerivedMemberTerms && activeOrgs is { ConferringOrganizationIds.Count: > 0 } expansionOrgs)
        {
            var orgsByRights = new Dictionary<AccessRights, List<Guid>>();
            foreach (var orgId in expansionOrgs.ConferringOrganizationIds.Distinct())
            {
                // The reader refuses Guid.Empty (it is a caller bug there, not a subject). Skipping it
                // here keeps a malformed junction row from turning the whole term into an exception.
                if (orgId == Guid.Empty)
                {
                    continue;
                }

                // STANDING-GATED, deliberately and provisionally. Register B-1 says derived access is
                // "default-on" with Secure as the veto, but no FR assigns a level to a NON-standing
                // derived contribution, and this task is instructed to encode rather than invent one
                // (POML escalation trigger 1). An organization with the flag unset, no baseline, or an
                // unreadable row yields Rights == None and contributes NOTHING — the same fail-closed
                // value task 042 established for the contact plane.
                var orgStanding = await _standingGrant
                    .ReadForOrganizationAsync(orgId, ct).ConfigureAwait(false);
                var orgRights = orgStanding.Rights;
                if (orgRights == AccessRights.None)
                {
                    continue;
                }

                if (!orgsByRights.TryGetValue(orgRights, out var bucket))
                {
                    orgsByRights[orgRights] = bucket = new List<Guid>();
                }
                bucket.Add(orgId);
            }

            foreach (var (orgRights, orgIds) in orgsByRights)
            {
                // Provenance is set because a term that CAN contribute ran — matching standingApplied
                // above. It stays false when every organization resolved to None, so Sources never
                // claims a term that contributed nothing (task 042 §6.2).
                orgExpansionApplied = true;

                var orgWalk = await WalkMembershipPagesAsync(
                    (options, token) => _membership.ResolveByContactAsync(contactId, entityType, options, token),
                    entityType,
                    $"contact {contactId} via {orgIds.Count} organization(s) at {orgRights}",
                    ct,
                    identityTypes: OrganizationIdentityTypeOnly,
                    organizationIds: orgIds).ConfigureAwait(false);

                orgTerms.Add((orgRights, orgWalk.Ids));
                capped |= orgWalk.Capped;
                membershipPages += orgWalk.Pages;
            }
        }

        // ── FLAGS: ONE batched read over every candidate id (NFR-02) ───────────────────────────────
        var candidates = standingIds
            .Concat(orgTerms.SelectMany(t => t.RecordIds))
            .Concat(grants is null ? Enumerable.Empty<Guid>() : GrantedIdsFor(grants, entityType))
            .ToList();
        var flags = await _participations
            .GetRootRecordFlagsAsync(entityType, candidates, ct).ConfigureAwait(false);
        var isDirectOnly = DirectOnlyPredicate(flags);

        // Term 1 — explicit sprk_externalrecordaccess grants, with direct-only suppression applied BEFORE the
        // max: on a Secure or Limited record only the contact's OWN grant rows contribute (FR-22; Limited
        // since task 138). Shared by both contact sign-ins, so CIAM inherits Limited with no CIAM code.
        if (grants is not null)
        {
            AccumulateTerm(composed, GrantedRightsFor(grants, entityType, isDirectOnly));
        }

        // Term 2 — standing-grant membership. This is a DERIVED-MEMBER term, so a direct-only (Secure or
        // Limited) record suppresses it entirely: the record simply never receives the contribution
        // (structural suppression, per FR-22 — not a post-hoc subtraction that the max would already have
        // absorbed).
        if (standingApplied)
        {
            AccumulateTerm(
                composed,
                standingIds
                    .Where(id => !isDirectOnly(id))
                    .Select(id => KeyValuePair.Create(id, standingRights)));
        }

        // Term 3 — ORG EXPANSION. A DERIVED-MEMBER term, so a direct-only (Secure or Limited) record
        // suppresses it ENTIRELY and STRUCTURALLY: the record never receives the contribution at all
        // (FR-22), exactly as the standing term above — not a post-hoc subtraction, which the max would
        // already have absorbed.
        // This covers every principal kind that can reach the term, which on this plane is the contact.
        foreach (var (orgRights, recordIds) in orgTerms)
        {
            AccumulateTerm(
                composed,
                recordIds
                    .Where(id => !isDirectOnly(id))
                    .Select(id => KeyValuePair.Create(id, orgRights)));
        }

        // ── VETOES, after the max, in order: deny-list (task 039) → Restricted (task 037) ──────────
        // Deny-veto subject = this contact's OWN id + its active organizations (task 039 / FR-23) —
        // the SAME single read the org-expansion term above consumed, so the additive term and the
        // ethical wall can never be computed from two different membership snapshots.
        // NOTHING survives Restricted on either contact plane: a contact principal's every term is
        // contact-sourced, which is precisely FR-21's "denies ALL contact principals regardless of grant
        // source".
        //
        // On CIAM the junction is read here, for the veto alone. With no candidates the veto returns
        // before it would consult the subject, so `None` is never a fail-open there (the systemuser
        // plane's identical gate); with candidates the read happens, and its Unreadable outcome — every
        // fault, query-level included since task 109 — denies every candidate.
        var subjectOrgs = activeOrgs
            ?? (candidates.Count == 0
                ? ActiveOrgMemberships.None
                : await ReadActiveOrgMembershipsAsync(contactId, ct).ConfigureAwait(false));

        // A provable denial and an unverifiable candidate are both removed here (fail closed) — task 142 r4 split them
        // only so the WRITE-time check can report the second as a fault.
        var veto = await ResolveDenyVetoAsync(entityType, candidates, contactId, subjectOrgs, ct)
            .ConfigureAwait(false);

        ApplyVetoPipeline(composed, veto.Removed, flags, EmptyRights);

        // Last: no key without Read (task 136 · C2), on both contact sign-ins. Reachable cases: an
        // organization-only grant on a Secure or Limited root, and a matter / work-assignment grant row with no level
        // (owner 2026-09-30: no level = not granted).
        RemoveEntriesWithoutRead(composed);

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for contact {ContactId} ({Plane} contact plane) on " +
            "{EntityType}: {Count} records (grants: {Grants}, standing-grant membership: {Standing}, org " +
            "expansion: {OrgExpansion} over {OrgTerms} baseline(s), {Pages} membership page(s) total, " +
            "capped: {Capped}).",
            contactId, includeDerivedMemberTerms ? "workforce" : "CIAM", entityType, composed.Count,
            grantsApplied, standingApplied, orgExpansionApplied, orgTerms.Count, membershipPages, capped);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entityType,
            Rights = composed,
            Capped = capped,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: false,
                ContactGrants: grantsApplied,
                StandingGrantMembership: standingApplied,
                OrgExpansionMembership: orgExpansionApplied),
        };
    }
}
