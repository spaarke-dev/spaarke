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
// Broker-only (ADR-028 A2 NFR-02): reads membership/grant/flag data APP-ONLY against the already-
// resolved principal (task 020). No caller-token exchange (no OBO), no Graph SDK types, no
// AI-internal types.

using Spaarke.Dataverse;               // AccessRights — the rights type (root CLAUDE.md §11: reuse, do not fork)
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Composes and evaluates the accessible-record set for a resolved <see cref="WorkforcePrincipal"/>
/// per design.md §5 / spec FR-06. The single enforcement primitive: given a principal + entity type
/// + record id, decide membership in the composed set (deny anything outside it).
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
    /// The enforcement decision: is <paramref name="recordId"/> in the principal's composed
    /// accessible set for <paramref name="entityType"/>? A <c>false</c> result MUST be enforced as a
    /// DENY (not merely an omission) by the caller.
    /// </summary>
    /// <remarks>
    /// Membership only — it answers "may the caller SEE this record", not "may the caller change it".
    /// A mutating route MUST use <see cref="IsOperationPermittedAsync"/> instead; treating membership
    /// as permission to write is the defect FR-19 removes.
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
    /// </summary>
    public required IReadOnlyDictionary<Guid, AccessRights> Rights { get; init; }

    private IReadOnlySet<Guid>? _recordIds;

    /// <summary>
    /// The de-duplicated record ids the principal may access for this entity type.
    /// <para>
    /// As of task 032 this is a DERIVED VIEW over <see cref="Rights"/>, not a stored second collection,
    /// so ids and rights cannot disagree. Kept at this exact shape so <c>Tier2ScopeFilterInjector</c>,
    /// the module scope predicates and <c>CallerPrincipalResolver</c> are unaffected.
    /// </para>
    /// </summary>
    public IReadOnlySet<Guid> RecordIds => _recordIds ??= Rights.Keys.ToHashSet();

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

    public int Count => Rights.Count;

    /// <summary>The enforcement check: <c>true</c> iff the record is in the composed set.</summary>
    public bool Contains(Guid recordId) => Rights.ContainsKey(recordId);
}

/// <summary>
/// Flags for which design §5 union terms contributed to an <see cref="AccessibleRecordSet"/>.
/// </summary>
/// <param name="SystemUserMembership">systemuser → ADR-034 membership (automatic) term applied.</param>
/// <param name="ContactGrants">contact → sprk_externalrecordaccess grants term applied.</param>
/// <param name="StandingGrantMembership">contact → standing-grant runtime membership term applied
/// (only when the contact held a standing grant).</param>
public readonly record struct AccessibleRecordSetSources(
    bool SystemUserMembership,
    bool ContactGrants,
    bool StandingGrantMembership);

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
        => GrantedRightsFor(grants, entityType, isSecure: _ => false);

    /// <summary>
    /// The grant term with <b>Secure pre-max suppression</b> applied (task 037 · FR-22).
    /// </summary>
    /// <param name="isSecure">Whether a given record id is flagged <c>sprk_issecure</c>.</param>
    /// <remarks>
    /// For a secure record the grant contributes only its <b>direct</b> level — the caller's own grant rows.
    /// Anything inherited through an organization grant is suppressed, per FR-22.
    /// <para>
    /// ⚠️ <b>This is suppression, not subtraction, and the difference is the whole point.</b> The org
    /// contribution is never added, so it cannot participate in the max. Subtracting afterwards would be
    /// wrong in a way that is easy to miss: with a ViewOnly direct grant and a Collaborate org grant, the max
    /// yields Collaborate, and there is no arithmetic that recovers "Read" from it — the direct level has
    /// already been absorbed. That is why <c>ExternalParticipation.DirectAccessLevel</c> exists.
    /// </para>
    /// <para>
    /// A secure record whose ONLY source was an org grant has a null direct level, which maps to
    /// <see cref="AccessRights.None"/>. The id still appears in the map with no rights; the Restricted veto
    /// or a downstream consumer sees an entry that permits nothing. It is not resurrectable by the max.
    /// </para>
    /// </remarks>
    private static IEnumerable<KeyValuePair<Guid, AccessRights>> GrantedRightsFor(
        ExternalGrantSet grants, string entityType, Func<Guid, bool> isSecure)
    {
        if (string.Equals(entityType, ProjectEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Projects.Select(p => KeyValuePair.Create(
                p.ProjectId,
                ExternalAccessLevels.ToAccessRights(
                    isSecure(p.ProjectId) ? p.DirectAccessLevel : p.AccessLevel)));

        if (string.Equals(entityType, MatterEntity, StringComparison.OrdinalIgnoreCase))
            return grants.MatterGrants.Select(g => KeyValuePair.Create(
                g.RecordId,
                ExternalAccessLevels.ToAccessRights(
                    isSecure(g.RecordId) ? g.DirectAccessLevel : g.AccessLevel)));

        if (string.Equals(entityType, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
            return grants.WorkAssignmentGrants.Select(g => KeyValuePair.Create(
                g.RecordId,
                ExternalAccessLevels.ToAccessRights(
                    isSecure(g.RecordId) ? g.DirectAccessLevel : g.AccessLevel)));

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
    /// The ordered veto pipeline seam (ADR-003 as amended by task 030 — design §4.5).
    /// <para>
    /// WIRED AS A NO-OP HERE, deliberately. Task 032 establishes the SHAPE and the ORDER; the terms that
    /// fill these slots arrive later — Secure suppression (task 037), deny list (038/039), Restricted
    /// (037). Reading flags or deny rows in this task is explicitly outside its envelope.
    /// </para>
    /// <para>
    /// The order is load-bearing and is asserted by the shape of this method rather than by a comment
    /// elsewhere:
    /// </para>
    /// <list type="number">
    /// <item><b>Pre-max suppression (Secure)</b> — must run BEFORE the max, on the TERMS. After the max
    /// the suppressed term has already won and the suppression is a no-op on the only inputs that
    /// mattered.</item>
    /// <item><b>Deny list</b> — post-max; removes the entry.</item>
    /// <item><b>Restricted</b> — post-max, after the deny list; removes the entry.</item>
    /// </list>
    /// <para>
    /// A veto REMOVES a key. It never writes a value, and there is no <c>AccessRights</c> value in this
    /// codebase that means "denied" — absence is the only representation of no access.
    /// </para>
    /// </summary>
    /// <param name="composed">The post-max map, mutated in place.</param>
    /// <param name="flags">Veto flags for every candidate record (fail-closed for unreadable ones).</param>
    /// <param name="survivesRestricted">
    /// The rights that are NOT contact-sourced and therefore survive the Restricted veto — the systemuser
    /// plane's own ADR-034 membership term. Empty on the contact plane, where every term is contact-sourced.
    /// </param>
    private static void ApplyVetoPipeline(
        Dictionary<Guid, AccessRights> composed,
        IReadOnlyDictionary<Guid, RootRecordFlags> flags,
        IReadOnlyDictionary<Guid, AccessRights> survivesRestricted)
    {
        // Slot 1 — deny list (ethical wall + per-child revocation). Task 038/039.
        //   foreach (var id in denyList) composed.Remove(id);
        // Still a no-op. It runs FIRST by construction: a record removed here is gone before Restricted
        // looks at it, so a deny can never be "downgraded" into a survivable Restricted outcome.

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
        foreach (var recordId in composed.Keys.ToList())
        {
            if (!flags.TryGetValue(recordId, out var f) || !f.IsRestricted)
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
    private readonly IContactStandingGrantReader _standingGrant;
    private readonly ILogger<AccessibleRecordSetService> _logger;

    public AccessibleRecordSetService(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        IContactStandingGrantReader standingGrant,
        ILogger<AccessibleRecordSetService> logger)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(participations);
        ArgumentNullException.ThrowIfNull(standingGrant);
        ArgumentNullException.ThrowIfNull(logger);
        _membership = membership;
        _participations = participations;
        _standingGrant = standingGrant;
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
    private async Task<MembershipPageWalk> WalkMembershipPagesAsync(
        Func<MembershipResolveOptions, CancellationToken, Task<MembershipResponse>> readPage,
        string entityType,
        string principalDescription,
        CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        string? token = null;
        var pages = 0;
        var capped = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await readPage(
                new MembershipResolveOptions(Limit: MembershipPageSize, ContinuationToken: token),
                ct).ConfigureAwait(false);
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
                var confirmation = await readPage(
                    new MembershipResolveOptions(Limit: MembershipPageSize, ContinuationToken: token),
                    ct).ConfigureAwait(false);
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
        var walk = await WalkMembershipPagesAsync(
            (options, token) => _membership.ResolveAsync(systemUserId, entityType, options, token),
            entityType,
            $"systemuser {systemUserId}",
            ct).ConfigureAwait(false);

        // ── ADDITIVE TERMS (design §4.5) ───────────────────────────────────────────────────────────
        // Each term contributes (recordId -> rights); AccumulateTerm merges them highest-wins.
        var composed = new Dictionary<Guid, AccessRights>();

        // Resolve the caller's contact + grants FIRST, so the candidate id set is complete before the
        // single batched flag read. Prefer the derived contact (sprk_primarycontact); fall back to a
        // verified-email match when the systemuser has no linked contact.
        var contactGrantsApplied = false;
        ExternalGrantSet? grants = null;
        if (IsGrantSupported(entityType))
        {
            Guid? grantContactId =
                principal.ContactId is { } cid && cid != Guid.Empty ? cid : null;

            if (grantContactId is null && !string.IsNullOrWhiteSpace(principal.Email))
            {
                grantContactId = await _participations
                    .ResolveExternalContactAsync(oid: null, email: principal.Email, ct)
                    .ConfigureAwait(false);
            }

            if (grantContactId is { } resolved && resolved != Guid.Empty)
            {
                grants = await _participations.GetGrantSetAsync(resolved, ct).ConfigureAwait(false);
                contactGrantsApplied = true;
            }
        }

        // ── FLAGS: ONE batched read over every candidate id (NFR-02) ───────────────────────────────
        var candidates = walk.Ids
            .Concat(grants is null ? Enumerable.Empty<Guid>() : GrantedIdsFor(grants, entityType))
            .ToList();
        var flags = await _participations
            .GetRootRecordFlagsAsync(entityType, candidates, ct).ConfigureAwait(false);
        bool IsSecure(Guid id) => flags.TryGetValue(id, out var f) && f.IsSecure;

        // Term 1 — ADR-034 membership. NOT contact-sourced, so it survives BOTH vetoes: it is the
        // systemuser's own Dataverse-governed access, which is exactly what Restricted preserves
        // ("only system users may have access") and what Secure leaves alone (the Secure BU covers the
        // Dataverse half; the veto covers the grant half — design §5.1).
        var membershipTerm = walk.Ids
            .Select(id => KeyValuePair.Create(id, MembershipTermRights))
            .ToList();
        AccumulateTerm(composed, membershipTerm);

        // Term 2 — contact grants, with Secure suppression applied BEFORE the max: on a secure record only
        // the caller's OWN grant rows contribute; org-inherited access is suppressed (FR-22).
        //
        // ⚠️ This applies on the SYSTEMUSER plane too, deliberately. A Type 1 user whose linked contact
        // holds an org grant would otherwise derive access to a secure record through the contact term —
        // access Dataverse knows nothing about, so the Secure BU cannot catch it (design §5.1, register C-10).
        if (grants is not null)
        {
            AccumulateTerm(composed, GrantedRightsFor(grants, entityType, IsSecure));
        }

        // ── VETOES, after the max, in order: deny-list (038/039) → Restricted (this task) ──────────
        // The membership term is what survives Restricted on this plane.
        ApplyVetoPipeline(
            composed,
            flags,
            membershipTerm.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

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

    // ── contact plane: grants ∪ (standing-grant membership IFF flag set) ─────────────────────────
    private async Task<AccessibleRecordSet> ComposeForContactAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        // A contact-only principal always carries a contactId anchor (task 020 invariant).
        var contactId = principal.ContactId
            ?? throw new InvalidOperationException(
                "A ContactOnly principal must carry a ContactId anchor (task 020 invariant).");

        var composed = new Dictionary<Guid, AccessRights>();

        // Read grants + standing membership FIRST so the candidate id set is complete before the single
        // batched flag read (NFR-02).
        var grantsApplied = false;
        ExternalGrantSet? grants = null;
        if (IsGrantSupported(entityType))
        {
            grants = await _participations.GetGrantSetAsync(contactId, ct).ConfigureAwait(false);
            grantsApplied = true;
        }

        // Standing-grant runtime membership, GATED on the subject-level policy flag. The negative case is
        // load-bearing: a contact WITHOUT a standing grant gets ONLY the explicit grants — NEVER automatic
        // membership. (task-051 seam: IContactStandingGrantReader.)
        var standingApplied = false;
        var capped = false;
        var membershipPages = 0;
        var standingIds = new HashSet<Guid>();
        if (await _standingGrant.HasStandingGrantAsync(contactId, ct).ConfigureAwait(false))
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

        // ── FLAGS: ONE batched read over every candidate id (NFR-02) ───────────────────────────────
        var candidates = standingIds
            .Concat(grants is null ? Enumerable.Empty<Guid>() : GrantedIdsFor(grants, entityType))
            .ToList();
        var flags = await _participations
            .GetRootRecordFlagsAsync(entityType, candidates, ct).ConfigureAwait(false);
        bool IsSecure(Guid id) => flags.TryGetValue(id, out var f) && f.IsSecure;

        // Term 1 — explicit sprk_externalrecordaccess grants, with Secure suppression applied BEFORE the
        // max: on a secure record only the contact's OWN grant rows contribute (FR-22).
        if (grants is not null)
        {
            AccumulateTerm(composed, GrantedRightsFor(grants, entityType, IsSecure));
        }

        // Term 2 — standing-grant membership. This is a DERIVED-MEMBER term, so Secure suppresses it
        // entirely: the record simply never receives the contribution (structural suppression, per FR-22 —
        // not a post-hoc subtraction that the max would already have absorbed).
        if (standingApplied)
        {
            AccumulateTerm(
                composed,
                standingIds
                    .Where(id => !IsSecure(id))
                    .Select(id => KeyValuePair.Create(id, MembershipTermRights)));
        }

        // ── VETOES, after the max, in order: deny-list (038/039) → Restricted (this task) ──────────
        // NOTHING survives Restricted on this plane: a contact principal's every term is contact-sourced,
        // which is precisely FR-21's "denies ALL contact principals regardless of grant source".
        ApplyVetoPipeline(composed, flags, EmptyRights);

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for contact {ContactId} on {EntityType}: {Count} records " +
            "(grants: {Grants}, standing-grant membership: {Standing} over {Pages} page(s), capped: {Capped}).",
            contactId, entityType, composed.Count, grantsApplied, standingApplied, membershipPages, capped);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entityType,
            Rights = composed,
            Capped = capped,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: false,
                ContactGrants: grantsApplied,
                StandingGrantMembership: standingApplied),
        };
    }
}
