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
    Task<bool> IsRecordAccessibleAsync(
        WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct);
}

/// <summary>
/// The composed accessible-record set for one principal + entity type, with source provenance for
/// auditability. <see cref="Contains"/> is the enforcement check.
/// </summary>
public sealed class AccessibleRecordSet
{
    public required WorkforcePrincipalKind PrincipalKind { get; init; }
    public required string EntityType { get; init; }

    /// <summary>The de-duplicated record ids the principal may access for this entity type.</summary>
    public required IReadOnlySet<Guid> RecordIds { get; init; }

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

    public int Count => RecordIds.Count;

    /// <summary>The enforcement check: <c>true</c> iff the record is in the composed set.</summary>
    public bool Contains(Guid recordId) => RecordIds.Contains(recordId);
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

    /// <summary>The granted record ids of <paramref name="entityType"/> within a grant set.</summary>
    private static IEnumerable<Guid> GrantedIdsFor(ExternalGrantSet grants, string entityType)
    {
        if (string.Equals(entityType, ProjectEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Projects.Select(p => p.ProjectId);
        if (string.Equals(entityType, MatterEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Matters;
        if (string.Equals(entityType, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
            return grants.WorkAssignments;
        return Enumerable.Empty<Guid>();
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

        var ids = walk.Ids;

        // Contact-grants union — grants now span project / matter / work-assignment root types
        // (task 028, closing R1 gap #2), so this term applies for any grant-supported entity. Prefer the
        // derived contact (sprk_primarycontact); fall back to a verified-email match when the systemuser
        // has no linked contact.
        var contactGrantsApplied = false;
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
                var grants = await _participations
                    .GetGrantSetAsync(resolved, ct)
                    .ConfigureAwait(false);
                foreach (var id in GrantedIdsFor(grants, entityType))
                {
                    ids.Add(id);
                }
                contactGrantsApplied = true;
            }
        }

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for systemuser {SystemUserId} on {EntityType}: " +
            "{Count} records over {Pages} membership page(s) (ADR-034 membership; contact-grants " +
            "union applied: {ContactGrants}; capped: {Capped}).",
            systemUserId, entityType, ids.Count, walk.Pages, contactGrantsApplied, walk.Capped);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.SystemUser,
            EntityType = entityType,
            RecordIds = ids,
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

        var ids = new HashSet<Guid>();

        // Term 1 — explicit sprk_externalrecordaccess grants (per-record, materialized). Grants now span
        // project / matter / work-assignment root types (task 028, closing R1 gap #2): they contribute
        // for any grant-supported entity, restricted to that entity's granted-id slice.
        var grantsApplied = false;
        if (IsGrantSupported(entityType))
        {
            var grants = await _participations
                .GetGrantSetAsync(contactId, ct)
                .ConfigureAwait(false);
            foreach (var id in GrantedIdsFor(grants, entityType))
            {
                ids.Add(id);
            }
            grantsApplied = true;
        }

        // Term 2 — standing-grant runtime membership, GATED on the subject-level policy flag. The
        // negative case is load-bearing: a contact WITHOUT a standing grant gets ONLY the explicit
        // grants above — NEVER automatic membership. (task-051 seam: IContactStandingGrantReader.)
        var standingApplied = false;
        var capped = false;
        var membershipPages = 0;
        if (await _standingGrant.HasStandingGrantAsync(contactId, ct).ConfigureAwait(false))
        {
            // FR-14: same continuation-following fix as the systemuser plane — the pre-fix
            // `options: null` call silently capped a standing-grant contact at 500 records.
            var walk = await WalkMembershipPagesAsync(
                (options, token) => _membership.ResolveByContactAsync(contactId, entityType, options, token),
                entityType,
                $"contact {contactId}",
                ct).ConfigureAwait(false);

            foreach (var id in walk.Ids)
            {
                ids.Add(id);
            }
            capped = walk.Capped;
            membershipPages = walk.Pages;
            standingApplied = true;
        }

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for contact {ContactId} on {EntityType}: {Count} records " +
            "(grants: {Grants}, standing-grant membership: {Standing} over {Pages} page(s), capped: {Capped}).",
            contactId, entityType, ids.Count, grantsApplied, standingApplied, membershipPages, capped);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entityType,
            RecordIds = ids,
            Capped = capped,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: false,
                ContactGrants: grantsApplied,
                StandingGrantMembership: standingApplied),
        };
    }
}
