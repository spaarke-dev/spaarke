using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// unified-access-control-r2 task 143 (GitHub #1066) — the No Access ENFORCER: removes the direct POA shares an
// active entry walls off, on SECURE records only (owner Q4), within minutes (round 3 R3/R4). Owner answers applied:
// N2 (access a wall cannot remove per user is REPORTED and hidden on Teams/SPA — never revoked), N5 (enforce only when
// the entry's author holds Write on the record), S5 (never remove the last person who can see a secure record).

/// <summary>Stable outcome values of <see cref="NoAccessEnforcementReport.Outcome"/>.</summary>
public static class NoAccessEnforcementOutcome
{
    /// <summary>The entry was evaluated; see the lists for what happened per user and record.</summary>
    public const string Evaluated = "evaluated";

    /// <summary>No entry has the id.</summary>
    public const string NotFound = "not-found";

    /// <summary>The entry is inactive: it walls nothing, so nothing was enforced.</summary>
    public const string Inactive = "inactive";

    /// <summary>The entry names no subject or object, or more than one: it walls nothing (schema Business Rule 1).</summary>
    public const string Malformed = "malformed";

    /// <summary>The entry itself could not be read.</summary>
    public const string Failed = "failed";
}

/// <summary>Stable reason / mechanism values used in the report lists.</summary>
public static class NoAccessEnforcementReason
{
    /// <summary>N5: the entry's last modifier does not hold Write on the record, so nothing was removed on it.</summary>
    public const string AuthorLacksWrite = "author-lacks-write";

    /// <summary>N5: the entry's last modifier is not an enabled person (an application user, disabled, or unknown).</summary>
    public const string AuthorNotAPerson = "author-not-a-person";

    /// <summary>S5: removing this share would leave the secure record with nobody who can open it.</summary>
    public const string LastPersonOnSecureRecord = "last-person-on-secure-record";

    /// <summary>Q4: the record is not secure; internal access on it is not the wall's to remove.</summary>
    public const string NotSecure = "not-secure";

    /// <summary>The object is not a secure ROOT record (a child, or another table): no direct POA share is removed here.</summary>
    public const string NotASecureRoot = "not-a-secure-root";

    /// <summary>N2 mechanism: a share held by a team the user belongs to.</summary>
    public const string TeamShare = "team-share";

    /// <summary>N2 mechanism: the record is owned by a team the user belongs to.</summary>
    public const string TeamOwnership = "team-ownership";

    /// <summary>N2 mechanism: Dataverse still admits the user with no share or team behind it — a role, its depth, or the business unit.</summary>
    public const string RoleOrBusinessUnit = "role-or-business-unit";
}

/// <summary>A direct share the enforcer removed and confirmed gone by read-back.</summary>
public sealed record NoAccessRemovedShare(Guid SystemUserId, string RecordType, Guid RecordId, int PreviousAccessRightsMask);

/// <summary>Access the wall cannot remove per user (owner N2): reported, hidden on Teams/SPA, never revoked.</summary>
public sealed record NoAccessNotEnforceable(Guid SystemUserId, string RecordType, Guid RecordId, string Mechanism, Guid? TeamId);

/// <summary>A record (and optionally a user) on which the entry was deliberately not enforced, and why.</summary>
public sealed record NoAccessNotEnforced(string RecordType, Guid RecordId, Guid? SystemUserId, string Reason);

/// <summary>Something the enforcer could not do or could not confirm. Never reported as "removed" or "clean".</summary>
public sealed record NoAccessEnforcementFailure(string? RecordType, Guid? RecordId, Guid? SystemUserId, string Kind, string Message);

/// <summary>What one enforcement of one entry did (task 143).</summary>
public sealed record NoAccessEnforcementReport(
    Guid EntryId,
    string Outcome,
    string? SubjectKind,
    IReadOnlyList<NoAccessRemovedShare> Removed,
    IReadOnlyList<NoAccessNotEnforceable> NotEnforceable,
    IReadOnlyList<NoAccessNotEnforced> NotEnforced,
    IReadOnlyList<NoAccessEnforcementFailure> Failures,
    int CoveredUsers,
    int CoveredRecords,
    bool Truncated)
{
    /// <summary>Evaluated with no failure and nothing truncated. Anything else is not "clean".</summary>
    public bool Complete => Outcome == NoAccessEnforcementOutcome.Evaluated && Failures.Count == 0 && !Truncated;

    internal static NoAccessEnforcementReport Terminal(Guid entryId, string outcome, NoAccessEnforcementFailure? failure = null)
        => new(entryId, outcome, null, Array.Empty<NoAccessRemovedShare>(), Array.Empty<NoAccessNotEnforceable>(),
            Array.Empty<NoAccessNotEnforced>(),
            failure is null ? Array.Empty<NoAccessEnforcementFailure>() : new[] { failure },
            0, 0, false);
}

/// <summary>One active entry covering a record, and the record it covers it through: the record itself, or a secure
/// record it is filed under (task 064).</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="CoveredRecordType">The FIRST path it was found on: the record itself when it names it or an organization
/// it references, otherwise the secure parent.</param>
/// <param name="CoveredRecordId">That record's id.</param>
/// <param name="AlsoViaSecureParent">Found on its first path AND (again) through a secure record the record is filed under
/// (task 064 verifier pass 2): e.g. both the record and its secure parent reference the walled organization. A parent path
/// binds whatever the record's own Secure flag says, so it must not be lost when the entry is listed once.</param>
public sealed record NoAccessCoveringEntry(
    Guid EntryId, string CoveredRecordType, Guid CoveredRecordId, bool AlsoViaSecureParent = false);

/// <summary>
/// The active No Access entries covering one record (<see cref="NoAccessShareEnforcer.ReadCoverageAsync"/>, task 064).
/// <see cref="Truncated"/>: more entries cover it than one read returns. Not <see cref="IsReadable"/>: the entries are
/// UNKNOWN (never "none") and <see cref="Fault"/> says why.
/// </summary>
public sealed record NoAccessCoverage(IReadOnlyList<NoAccessCoveringEntry> Entries, bool Truncated, string? Fault)
{
    /// <summary>The entries could be read; when false, <see cref="Entries"/> is empty and means nothing.</summary>
    public bool IsReadable => Fault is null;

    internal static NoAccessCoverage Unreadable(string fault) => new(Array.Empty<NoAccessCoveringEntry>(), false, fault);
}

/// <summary>
/// Removes the direct POA shares an active No Access entry walls off, on secure records (task 143 · owner Q4).
/// </summary>
/// <remarks>
/// <para><b>What an entry covers.</b> Its SECURE root records — the record object itself, or every secure project,
/// matter and work assignment that references the object organization in ANY org-typed lookup (B-10) — times its
/// SYSTEMUSERS: the named systemuser; or every systemuser a subject contact represents (its task-141 link
/// <c>sprk_primarycontact</c>, or its oid binding); or every systemuser represented by an ACTIVE member contact of the
/// subject organization (owner N4). Recomputed from current data on every call, so the same call enforces a new entry,
/// a re-activated one, a share made out of band after the entry, a record that became secure after it, and a link that
/// appeared after it (criterion 12).</para>
///
/// <para><b>What is removed.</b> Only a user's DIRECT share on a covered record, through the one share seam
/// (<see cref="IDataverseRecordShareService"/>): a strict read before (a failed read is never "no share"), the revoke,
/// a strict read-back after (a share still there is a failure naming the user and record, never "removed"), and the
/// user's impersonated root-set cache cleared under the tenant key that user's reads write (finally — a revoke that
/// threw may have applied). The <c>InternalShareEndpoints.UnshareAsync</c> discipline.</para>
///
/// <para><b>What is never removed</b> (owner N2). A team share, team ownership, or access Dataverse confers through a
/// role or the business unit: removing a team share would strip every other member. Each is REPORTED as
/// <see cref="NoAccessNotEnforceable"/> with its mechanism; the systemuser-plane veto hides the record on Teams/SPA
/// while MDA admits the user — a §6.5 path-A exception the owner accepted (round 3b, N2).</para>
///
/// <para><b>Who may cause a removal</b> (owner N5). Nothing is removed on a record unless the entry's last modifier is
/// an enabled person holding Write on THAT record (Dataverse's own answer, read app-only for that principal). Otherwise
/// <see cref="NoAccessEnforcementReason.AuthorLacksWrite"/> is reported and nothing on that record is touched; the
/// read-time veto still applies.</para>
///
/// <para><b>Never the last person</b> (owner S5). A secure record keeps at least one enabled person with a share that can
/// read it: a removal that would leave none is refused and reported
/// (<see cref="NoAccessEnforcementReason.LastPersonOnSecureRecord"/>), naming the record. The read, the check and the
/// revoke run under a per-record lease (task 143 r1): the shares are re-read under it, and it is renewed immediately
/// before the revoke (r2), so concurrent enforcements cannot each remove "the other" last reader.</para>
///
/// <para><b>Removal is not reversible by design.</b> Deactivating the entry lifts the veto but re-creates no share; access
/// comes back only through a deliberate re-grant. A task-142 auto share removed here is task 142's to record as
/// <c>Skipped(no-access)</c> (142 not landed: its ledger must treat a share this enforcer removed that way).</para>
///
/// <para><b>Fails closed</b> (ADR-003). Every read that fails is a <see cref="NoAccessEnforcementFailure"/>, and a report
/// with any failure or truncation is not <see cref="NoAccessEnforcementReport.Complete"/>. Nothing here ever grants on a
/// root.</para>
///
/// <para><b>The record's children follow</b> (task 149, merged after this task — AC6). After a removal on a secure record,
/// <see cref="SecureChildShareSynchronizer.SyncRootAsync"/> brings its children into line with the root's REMAINING share
/// set at once, so the walled user leaves every child in the same call rather than at the next reconcile tick. That fan-out
/// never exceeds the root's shares and never gives a walled user anything (it asks the same guard before any grant); a fan-out
/// that could not finish is reported as a failure (<c>children-incomplete</c>), and the 2-minute reconcile completes it.</para>
///
/// <para><b>The records filed under it follow</b> (task 158 final round — main-session round 58 item 1; round 61 item 1;
/// owner round 82). A work assignment or project FILED UNDER a secure matter or project honours that parent's No Access list
/// for every share (round 39 item 2), so an entry the enforcer applies to a covered matter or project also removes the walled
/// person's DIRECT share on each work assignment and project filed BELOW it, at any depth — found through the ONE
/// child-direction walk (<c>SecureRootInheritance.ListFiledRootsBelowAsync</c>, bounded and cycle-safe, as the share-time
/// check climbs every secure ancestor) — and removed by the same per-record steps as any covered record: the author's Write
/// on that record (N5), the record lock, never its last reader (S5), the read-back, its own children after a removal. Only
/// after the author passed N5 on the parent. A CONFIRMED filed record is enforced whatever its own flag reads — secure, not
/// secure yet, or left unsecured by a Refused or Failed inheritance ("the parent permissions control", owner round 82,
/// GitHub #1410); a filing whose type could not be confirmed is left alone. Anything that could not be listed, confirmed or
/// finished there is reported as <c>children-incomplete</c> on the parent — never "complete".</para>
/// </remarks>
public sealed class NoAccessShareEnforcer
{
    /// <summary>Most secure records one entry is enforced on per call; past it the report is truncated.</summary>
    internal const int MaxCoveredRecords = 500;

    /// <summary>Most systemusers one entry is enforced for per call; past it the report is truncated.</summary>
    internal const int MaxCoveredUsers = 200;

    /// <summary>Most member contacts an organization subject is expanded to per call.</summary>
    internal const int MaxMemberContacts = 200;

    private static readonly string[] SecureRootTypes = { "sprk_project", "sprk_matter", "sprk_workassignment" };

    private readonly NoAccessEnforcementStore _store;
    private readonly ExternalParticipationService _participations;
    private readonly IContactIdentityStore _identityStore;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly ITenantCache _cache;
    private readonly IScheduledJobLease _recordLock;
    private readonly SecureChildShareSynchronizer _secureChildShares;
    private readonly IGenericEntityService _dataverse;
    private readonly ILogger<NoAccessShareEnforcer> _logger;

    /// <param name="store">The enforcer's Dataverse reads.</param>
    /// <param name="participations">Flags and organization reads.</param>
    /// <param name="identityStore">The systemuser↔contact link reads.</param>
    /// <param name="recordShare">The one POA share seam.</param>
    /// <param name="cache">The tenant cache (root-set invalidation).</param>
    /// <param name="recordLock">The atomic lease (task 143 r1) that serializes removals per record, so owner S5 holds
    /// across concurrent enforcements. The scheduler's lease store, reused as a keyed mutex under its own key family
    /// (<c>no-access-enforce:{table}:{id}</c>) — never a job's dispatch key. Reusing it from a service that a scheduled
    /// job resolves is the ADR-036 A1-7 / ADR-052 §5 tension recorded as a project-scoped §6.5 path-A exception in
    /// <c>projects/unified-access-control-r2/design.md</c> §9 (task 143 r2).</param>
    /// <param name="secureChildShares">Task 149's synchronizer (merged after this task): after a root share is removed,
    /// the record's children follow at once.</param>
    /// <param name="dataverse">Task 158 final round (round 58 item 1): the app-only reads of what is FILED UNDER a covered
    /// matter or project — through the one child-direction walk, never a second copy. Registered unconditionally
    /// (GraphModule), so the enforcer gains no asymmetric dependency.</param>
    /// <param name="logger">Logger.</param>
    public NoAccessShareEnforcer(
        NoAccessEnforcementStore store,
        ExternalParticipationService participations,
        IContactIdentityStore identityStore,
        IDataverseRecordShareService recordShare,
        ITenantCache cache,
        IScheduledJobLease recordLock,
        SecureChildShareSynchronizer secureChildShares,
        IGenericEntityService dataverse,
        ILogger<NoAccessShareEnforcer> logger)
    {
        _store = store;
        _participations = participations;
        _identityStore = identityStore;
        _recordShare = recordShare;
        _cache = cache;
        _recordLock = recordLock;
        _secureChildShares = secureChildShares;
        _dataverse = dataverse;
        _logger = logger;
    }

    /// <summary>
    /// The ONE well-formedness rule for an entry (schema Business Rule 1, plus task 154's canonical record id), shared by
    /// the enforcer and the per-record read (task 064): exactly one subject, and EITHER an object organization alone OR an
    /// object record type with a record id in the form the read-time veto matches
    /// (<see cref="NoAccessListReader.TryParseObjectRecordId"/>). A row that fails it walls nothing, so neither the
    /// enforcer nor the read counts it as a restriction.
    /// </summary>
    /// <param name="row">The entry's subject and object columns.</param>
    /// <param name="subjectKind">The one subject's kind, when well-formed.</param>
    /// <param name="isOrgObject">True when the object is an organization (an ethical wall); false for a record.</param>
    internal static bool TryClassify(NoAccessEntryRow row, out NoAccessSubjectKinds subjectKind, out bool isOrgObject)
    {
        subjectKind = NoAccessListReader.SubjectKindOf(row) ?? NoAccessSubjectKinds.None;
        isOrgObject = row._sprk_objectorganization_value.HasValue
                      && !row._sprk_objectrecordtype_value.HasValue && string.IsNullOrEmpty(row.sprk_objectrecordid);
        // Task 154: the record id must be in the form the read-time veto matches. A braced or otherwise non-canonical id is
        // malformed here too. Before, it was enforced (shares removed) while the veto never matched it, so the entry looked
        // enforced and walled nothing on Teams/SPA.
        var isRecordObject = !row._sprk_objectorganization_value.HasValue
                             && row._sprk_objectrecordtype_value.HasValue
                             && NoAccessListReader.TryParseObjectRecordId(row.sprk_objectrecordid, out _);
        return subjectKind != NoAccessSubjectKinds.None && (isOrgObject || isRecordObject);
    }

    /// <summary>Enforces one entry now.</summary>
    /// <param name="entryId">The <c>sprk_noaccessentry</c> id. Everything else is read from the entry.</param>
    /// <param name="cacheTenants">The tenant namespaces a walled user's root-set cache is cleared under (the caller's
    /// <c>tid</c> and/or the deployment's tenant — never "anonymous"). Empty: no cache is cleared, and the entry lapses
    /// within the cache TTL.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<NoAccessEnforcementReport> EnforceEntryAsync(
        Guid entryId, IReadOnlyCollection<string> cacheTenants, CancellationToken ct)
    {
        // ── The entry ─────────────────────────────────────────────────────────
        NoAccessEntrySnapshot? entry;
        try
        {
            entry = await _store.ReadEntryAsync(entryId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId} could not be read; nothing was enforced.", entryId);
            return NoAccessEnforcementReport.Terminal(entryId, NoAccessEnforcementOutcome.Failed,
                new NoAccessEnforcementFailure(null, null, null, "entry-unreadable",
                    "The No Access entry could not be read, so nothing was enforced. Try again."));
        }

        if (entry is null)
        {
            return NoAccessEnforcementReport.Terminal(entryId, NoAccessEnforcementOutcome.NotFound);
        }

        if (!entry.IsActive)
        {
            return NoAccessEnforcementReport.Terminal(entryId, NoAccessEnforcementOutcome.Inactive);
        }

        var row = entry.Row;
        if (!TryClassify(row, out var subjectKind, out var isOrgObject))
        {
            _logger.LogWarning(
                "[NO-ACCESS-ENFORCE] Entry {EntryId} is malformed (subject kind {SubjectKind}, org object {OrgObject}, " +
                "record type {HasType}, record id '{RecordId}'); it walls nothing and nothing was enforced.",
                entryId, NoAccessListReader.SubjectKindOf(row), row._sprk_objectorganization_value.HasValue,
                row._sprk_objectrecordtype_value.HasValue, row.sprk_objectrecordid);
            return NoAccessEnforcementReport.Terminal(entryId, NoAccessEnforcementOutcome.Malformed);
        }

        var run = new Run(entryId, subjectKind);

        // ── Covered secure records, covered users ─────────────────────────────
        var records = await CoveredRecordsAsync(row, isOrgObject, run, ct).ConfigureAwait(false);
        var users = await CoveredUsersAsync(row, subjectKind, run, ct).ConfigureAwait(false);
        run.CoveredRecords = records.Count;
        run.CoveredUsers = users.Count;

        if (records.Count > 0 && users.Count > 0)
        {
            var authorOk = await AuthorIsPersonAsync(entry.ModifiedBy, records, run, ct).ConfigureAwait(false);
            if (authorOk)
            {
                var authorised = new List<(string LogicalName, Guid Id)>();
                foreach (var record in records)
                {
                    if (await EnforceOnRecordAsync(record, users, entry.ModifiedBy!.Value, cacheTenants, run, ct)
                            .ConfigureAwait(false))
                    {
                        authorised.Add(record);
                    }
                }

                // Task 158 final round (round 58 item 1; round 61 item 1; round 82): the records filed below each covered
                // matter or project the author may act on, at any depth, secure or not yet; a record already covered, or
                // filed under two covered parents, once.
                var reached = new HashSet<(string, Guid)>(records);
                foreach (var parent in authorised.Where(r => SecureRootInheritance.IsParent(r.LogicalName)))
                {
                    await EnforceOnFiledRecordsAsync(parent, users, entry.ModifiedBy!.Value, cacheTenants, reached, run, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        var report = run.ToReport();
        _logger.LogInformation(
            "[NO-ACCESS-ENFORCE] Entry {EntryId} ({SubjectKind}): {Users} user(s) x {Records} secure record(s); removed " +
            "{Removed}, not enforceable {NotEnforceable}, not enforced {NotEnforced}, failures {Failures}, truncated {Truncated}.",
            entryId, subjectKind, users.Count, records.Count, report.Removed.Count, report.NotEnforceable.Count,
            report.NotEnforced.Count, report.Failures.Count, report.Truncated);
        return report;
    }

    /// <summary>Most entries one record-scoped call re-applies.</summary>
    internal const int MaxEntriesPerRecord = 100;

    /// <summary>
    /// Re-applies No Access for ONE record: every active entry whose object is the record or an organization it references,
    /// each enforced as <see cref="EnforceEntryAsync"/> does (task 143 · owner R3: the task-142 "Update Access" command
    /// "also re-applies No Access for that record"). Every rule of the entry path holds, because it IS the entry path.
    /// Task 158 final round (main-session round 58 item 1): a work assignment or project filed under secure records honours
    /// their lists too (round 39 item 2), so the entries covering each secure matter or project the ONE parent walk
    /// (<see cref="SecureRootInheritance.ReadSecureParentsAsync"/>) finds are re-applied as well — each in full, as every
    /// entry here is (that parent, and what is filed under it). A filing that cannot be read re-applies nothing (reported).
    /// </summary>
    /// <returns>One report per covering entry; a single <see cref="NoAccessEnforcementOutcome.Failed"/> report (entry id
    /// <see cref="Guid.Empty"/>) when the covering entries could not be found.</returns>
    public async Task<IReadOnlyList<NoAccessEnforcementReport>> EnforceForRecordAsync(
        string entityLogicalName, Guid recordId, IReadOnlyCollection<string> cacheTenants, CancellationToken ct)
    {
        try
        {
            var coverage = await ReadCoverageAsync(entityLogicalName, recordId, ct).ConfigureAwait(false);
            if (!coverage.IsReadable)
            {
                return new[] { CoveringEntriesUnreadable(entityLogicalName, recordId) };
            }

            var reports = new List<NoAccessEnforcementReport>();
            foreach (var entryId in coverage.Entries.Select(e => e.EntryId).Distinct())
            {
                reports.Add(await EnforceEntryAsync(entryId, cacheTenants, ct).ConfigureAwait(false));
            }

            if (coverage.Truncated)
            {
                reports.Add(NoAccessEnforcementReport.Terminal(Guid.Empty, NoAccessEnforcementOutcome.Failed,
                    new NoAccessEnforcementFailure(entityLogicalName, recordId, null, "covering-entries-truncated",
                        $"More than {MaxEntriesPerRecord} entries cover this record; the rest are enforced within 5 minutes.")));
            }

            return reports;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] The entries covering {Type} {RecordId} could not be enforced.",
                entityLogicalName, recordId);
            return new[] { CoveringEntriesUnreadable(entityLogicalName, recordId) };
        }
    }

    private static NoAccessEnforcementReport CoveringEntriesUnreadable(string entityLogicalName, Guid recordId)
        => NoAccessEnforcementReport.Terminal(Guid.Empty, NoAccessEnforcementOutcome.Failed,
            new NoAccessEnforcementFailure(entityLogicalName, recordId, null, "covering-entries-unreadable",
                "The No Access entries that cover this record could not be read, so nothing was enforced. Try again."));

    /// <summary>
    /// The ONE answer to "which ACTIVE No Access entries cover this record" (task 064, owner round 59 item 3: lifted out of
    /// <see cref="EnforceForRecordAsync"/> so the enforcer and the per-record read share it). Every entry whose object is the
    /// record or an organization it references (ANY org-typed lookup, B-10), and the same for every secure record it is filed
    /// under, up the chain (round 61 item 1, through the ONE parent walk
    /// <see cref="SecureRootInheritance.ReadSecureParentsAsync"/>). Each entry carries the record it covers through, and
    /// whether it ALSO reaches the record through a secure parent.
    /// </summary>
    /// <remarks>
    /// Never throws a read fault: an unreadable referenced-organization set, filing or entry query comes back
    /// <see cref="NoAccessCoverage.IsReadable"/> = false with the reason, never "no entries" (NFR-01). The record's own
    /// entries come first, so an entry reached both directly and through a parent is reported once, on its direct path,
    /// with <see cref="NoAccessCoveringEntry.AlsoViaSecureParent"/> set. The
    /// query asks for ACTIVE entries; whether each is well-formed (<see cref="TryClassify"/>) is the caller's to judge.
    /// </remarks>
    /// <param name="entityLogicalName">The record's table: <c>sprk_project</c>, <c>sprk_matter</c> or <c>sprk_workassignment</c>.</param>
    /// <param name="recordId">The record.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<NoAccessCoverage> ReadCoverageAsync(string entityLogicalName, Guid recordId, CancellationToken ct)
    {
        try
        {
            var covering = new List<NoAccessCoveringEntry>();
            var (entryIds, truncated) = await EntriesCoveringAsync(entityLogicalName, recordId, ct).ConfigureAwait(false);
            covering.AddRange(entryIds.Select(id => new NoAccessCoveringEntry(id, entityLogicalName, recordId)));

            // Never throws a read fault: an unreadable record, filing or parent comes back Unverifiable.
            // Round 61 item 1: every secure ANCESTOR's entries (a work assignment under a project under the matter).
            var parents = await SecureRootInheritance
                .ReadSecureParentsAsync(_dataverse, _logger, entityLogicalName, recordId, ct,
                    maxDepth: SecureRootInheritance.MaxFilingDepth)
                .ConfigureAwait(false);
            if (!parents.IsKnown)
            {
                _logger.LogError(
                    "[NO-ACCESS-ENFORCE] What {Type} {RecordId} is filed under could not be read ({Reason}); the entries " +
                    "covering it are unknown.", entityLogicalName, recordId, parents.Unverifiable);
                return NoAccessCoverage.Unreadable($"What the record is filed under could not be read: {parents.Unverifiable}.");
            }

            foreach (var parent in parents.SecureParents)
            {
                var (parentEntryIds, parentTruncated) = await EntriesCoveringAsync(parent.Table, parent.Id, ct).ConfigureAwait(false);
                foreach (var id in parentEntryIds)
                {
                    // Listed once per entry, but a parent path is never dropped: an entry already found on the record's
                    // own path is MARKED as also reaching it through this secure parent (task 064 verifier pass 2).
                    var index = covering.FindIndex(c => c.EntryId == id);
                    if (index < 0)
                    {
                        covering.Add(new NoAccessCoveringEntry(id, parent.Table, parent.Id));
                    }
                    else
                    {
                        covering[index] = covering[index] with { AlsoViaSecureParent = true };
                    }
                }

                truncated |= parentTruncated;
            }

            return new NoAccessCoverage(covering, truncated, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] The entries covering {Type} {RecordId} could not be read.",
                entityLogicalName, recordId);
            return NoAccessCoverage.Unreadable("The No Access entries that cover this record could not be read.");
        }
    }

    /// <summary>The active entries whose object is the record or an organization it references. Throws on a failed read.</summary>
    private async Task<(IReadOnlyList<Guid> EntryIds, bool Truncated)> EntriesCoveringAsync(
        string entityLogicalName, Guid recordId, CancellationToken ct)
    {
        var referenced = await _participations
            .GetReferencedOrganizationIdsAsync(entityLogicalName, new[] { recordId }, ct).ConfigureAwait(false);
        if (referenced.TryGetValue(recordId, out var refs) && refs.Unreadable)
        {
            throw new InvalidOperationException($"The organizations {entityLogicalName} {recordId} references could not be read.");
        }

        var orgIds = referenced.TryGetValue(recordId, out var r) ? r.OrganizationIds : Array.Empty<Guid>();
        return await _store.ReadActiveEntryIdsCoveringAsync(recordId, orgIds, MaxEntriesPerRecord, ct).ConfigureAwait(false);
    }

    // ── Covered records ───────────────────────────────────────────────────────

    private async Task<List<(string LogicalName, Guid Id)>> CoveredRecordsAsync(
        NoAccessEntryRow row, bool isOrgObject, Run run, CancellationToken ct)
    {
        var records = new List<(string, Guid)>();
        if (isOrgObject)
        {
            var orgId = row._sprk_objectorganization_value!.Value;
            foreach (var type in SecureRootTypes)
            {
                try
                {
                    var (ids, truncated) = await _participations
                        .FindSecureRootsReferencingOrganizationAsync(type, orgId, MaxCoveredRecords, ct).ConfigureAwait(false);
                    run.Truncated |= truncated;
                    records.AddRange(ids.Select(id => (type, id)));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError(ex,
                        "[NO-ACCESS-ENFORCE] Entry {EntryId}: the secure {Type} records referencing organization {OrgId} " +
                        "could not be read.", run.EntryId, type, orgId);
                    run.Fail(type, null, null, "covered-records-unreadable",
                        $"The secure {type} records this entry covers could not be read, so nothing was enforced on them.");
                }
            }

            if (records.Count > MaxCoveredRecords)
            {
                run.Truncated = true;
                records = records.Take(MaxCoveredRecords).ToList();
            }

            return records;
        }

        if (!NoAccessListReader.TryParseObjectRecordId(row.sprk_objectrecordid, out var recordId))
        {
            run.Fail(null, null, null, "object-record-unparseable",
                "The entry's object record id is not a record id, so nothing was enforced.");
            return records;
        }

        string? logicalName;
        try
        {
            logicalName = await _store.ReadRecordTypeLogicalNameAsync(row._sprk_objectrecordtype_value!.Value, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: the object record type could not be read.", run.EntryId);
            run.Fail(null, recordId, null, "object-type-unreadable",
                "The type of the entry's record could not be read, so nothing was enforced on it.");
            return records;
        }

        if (string.IsNullOrWhiteSpace(logicalName) || !ExternalParticipationService.IsFlagBearingRootType(logicalName))
        {
            // A child record, or another table: the read-time veto hides it; no direct POA share on it is a
            // secure-record share until C10 part 2 (tasks 146/149) — whose child-share fan-out must call the guard and
            // whose child shares this enforcer must then remove through the root.
            run.NotEnforced.Add(new NoAccessNotEnforced(logicalName ?? "unknown", recordId, null,
                NoAccessEnforcementReason.NotASecureRoot));
            return records;
        }

        IReadOnlyDictionary<Guid, RootRecordFlags> flags;
        try
        {
            flags = await _participations.GetRootRecordFlagsAsync(logicalName, new[] { recordId }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: flags of {Type} {RecordId} unreadable.",
                run.EntryId, logicalName, recordId);
            flags = new Dictionary<Guid, RootRecordFlags>();
        }

        if (!flags.TryGetValue(recordId, out var f) || f.IsUnreadable)
        {
            run.Fail(logicalName, recordId, null, "flags-unreadable",
                "Whether the record is secure could not be read, so nothing was enforced on it. Try again.");
            return records;
        }

        if (!f.IsSecure)
        {
            run.NotEnforced.Add(new NoAccessNotEnforced(logicalName, recordId, null, NoAccessEnforcementReason.NotSecure));
            return records;
        }

        records.Add((logicalName, recordId));
        return records;
    }

    // ── Covered users ─────────────────────────────────────────────────────────

    private async Task<List<Guid>> CoveredUsersAsync(
        NoAccessEntryRow row, NoAccessSubjectKinds kind, Run run, CancellationToken ct)
    {
        try
        {
            IReadOnlyCollection<Guid> users = kind switch
            {
                NoAccessSubjectKinds.SystemUser => new[] { row._sprk_subjectsystemuser_value!.Value },
                NoAccessSubjectKinds.Contact => await UsersRepresentingAsync(
                    new[] { row._sprk_subjectcontact_value!.Value }, ct).ConfigureAwait(false),
                _ => await UsersOfOrganizationAsync(row._sprk_subjectorganization_value!.Value, run, ct).ConfigureAwait(false),
            };

            var list = users.Where(u => u != Guid.Empty).Distinct().ToList();
            if (list.Count > MaxCoveredUsers)
            {
                run.Truncated = true;
                list = list.Take(MaxCoveredUsers).ToList();
            }

            return list;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: the users it covers could not be read.", run.EntryId);
            run.Fail(null, null, null, "covered-users-unreadable",
                "The people this entry covers could not be read, so nothing was enforced. Try again.");
            return new List<Guid>();
        }
    }

    private async Task<IReadOnlyCollection<Guid>> UsersOfOrganizationAsync(Guid organizationId, Run run, CancellationToken ct)
    {
        var (contacts, truncated) = await _participations
            .FindWallMemberContactsAsync(organizationId, MaxMemberContacts, ct).ConfigureAwait(false);
        run.Truncated |= truncated;
        return contacts.Count == 0 ? Array.Empty<Guid>() : await UsersRepresentingAsync(contacts, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The systemusers these contacts represent: linked by <c>sprk_primarycontact</c> (task 141), or whose Entra oid a
    /// contact's binding carries. The reverse of <see cref="SecureShareNoAccessGuard.ResolveSubjectsAsync"/>. Throws on a
    /// failed read.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> UsersRepresentingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        var users = new HashSet<Guid>();

        var linking = await _identityStore.FindSystemUsersLinkingAsync(contactIds, ct).ConfigureAwait(false);
        if (linking.Status != LookupStatus.Read)
        {
            throw new InvalidOperationException("The systemusers linking these contacts could not be read.");
        }

        users.UnionWith(linking.References.Select(r => r.SystemUserId));

        var oids = new HashSet<Guid>();
        foreach (var contactId in contactIds)
        {
            var contact = await _identityStore.GetContactAsync(contactId, ct).ConfigureAwait(false);
            if (contact.Status == LookupStatus.ColumnMissing)
            {
                continue; // no binding column here, so no contact can carry an oid
            }

            if (contact.Status != LookupStatus.Read)
            {
                throw new InvalidOperationException($"Contact {contactId}'s binding could not be read.");
            }

            foreach (var c in contact.Rows)
            {
                if (Guid.TryParse(c.RawOid, out var oid) && oid != Guid.Empty)
                {
                    oids.Add(oid);
                }
            }
        }

        if (oids.Count > 0)
        {
            users.UnionWith(await _store.FindSystemUsersByOidsAsync(oids, ct).ConfigureAwait(false));
        }

        return users;
    }

    // ── N5: the author ────────────────────────────────────────────────────────

    private async Task<bool> AuthorIsPersonAsync(
        Guid? author, IReadOnlyList<(string LogicalName, Guid Id)> records, Run run, CancellationToken ct)
    {
        if (author is { } a && a != Guid.Empty)
        {
            try
            {
                var people = await _store.ReadSystemUsersAsync(new[] { a }, ct).ConfigureAwait(false);
                if (people.TryGetValue(a, out var person) && person.IsEnabledPerson)
                {
                    return true;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: its author {Author} could not be read.", run.EntryId, a);
                foreach (var (type, id) in records)
                {
                    run.Fail(type, id, null, "author-unreadable",
                        "Who last changed this No Access entry could not be read, so nothing was removed. Try again.");
                }

                return false;
            }
        }

        foreach (var (type, id) in records)
        {
            run.NotEnforced.Add(new NoAccessNotEnforced(type, id, null, NoAccessEnforcementReason.AuthorNotAPerson));
        }

        return false;
    }

    // ── One record ────────────────────────────────────────────────────────────

    /// <returns>Whether the entry's author holds Write on the record (N5) — only then is anything removed on it, and only
    /// then are the secure records filed under it reached.</returns>
    private async Task<bool> EnforceOnRecordAsync(
        (string LogicalName, Guid Id) record,
        IReadOnlyList<Guid> users,
        Guid author,
        IReadOnlyCollection<string> cacheTenants,
        Run run,
        CancellationToken ct)
    {
        var (logicalName, recordId) = record;
        var binding = RootTable(logicalName);

        // ── N5: the author must hold Write on THIS record ──
        try
        {
            var authorRights = await _store.GetPrincipalRightsAsync(author, binding.EntitySet, recordId, ct).ConfigureAwait(false);
            if (!authorRights.HasFlag(AccessRights.Write))
            {
                run.NotEnforced.Add(new NoAccessNotEnforced(logicalName, recordId, null, NoAccessEnforcementReason.AuthorLacksWrite));
                return false;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: the author's rights on {Type} {RecordId} unreadable.",
                run.EntryId, logicalName, recordId);
            run.Fail(logicalName, recordId, null, "author-rights-unreadable",
                "Whether the entry's author holds Write on this record could not be read, so nothing was removed on it. Try again.");
            return false;
        }

        Guid? owningTeam = null;
        var owningTeamRead = true;
        try
        {
            owningTeam = await _store.ReadOwningTeamAsync(binding.EntitySet, binding.IdColumn, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            owningTeamRead = false;
            _logger.LogWarning(ex, "[NO-ACCESS-ENFORCE] Owner of {Type} {RecordId} unreadable.", logicalName, recordId);
        }

        var removedBefore = run.Removed.Count;
        foreach (var userId in users)
        {
            await EnforceForUserAsync(logicalName, binding.EntitySet, recordId, userId, owningTeam, owningTeamRead,
                cacheTenants, run, ct).ConfigureAwait(false);
        }

        if (run.Removed.Count > removedBefore)
        {
            await SyncChildrenAsync(logicalName, recordId, run, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Task 158 final round (main-session round 58 item 1): the walled users' DIRECT shares on the work assignments and
    /// projects FILED UNDER a covered matter or project the author holds Write on — each removed exactly as on a covered record
    /// (<see cref="EnforceOnRecordAsync"/>: N5 on that record, the lock, S5, the read-back, its children). The filed records
    /// come from the ONE child-direction walk (<c>SecureRootInheritance.ListFiledRootsAsync</c>).
    /// GitHub #1410 / owner round 82 ("the parent permissions control"): a CONFIRMED filed record is enforced whatever its own
    /// <c>sprk_issecure</c> reads — secure, not secure yet (inheritance pending), or left unsecured because inheritance ended
    /// Refused or Failed, when "the next enforcement reaches it" would never come. Before, such a record was reported
    /// <see cref="NoAccessEnforcementReason.NotSecure"/> and kept the walled user's share. Anything not finished — the walk
    /// could not be read, a record whose filing type could not be read, a failure on a filed record — is a
    /// <c>children-incomplete</c> failure naming the parent.
    /// </summary>
    private async Task EnforceOnFiledRecordsAsync(
        (string LogicalName, Guid Id) parent,
        IReadOnlyList<Guid> users,
        Guid author,
        IReadOnlyCollection<string> cacheTenants,
        HashSet<(string, Guid)> reached,
        Run run,
        CancellationToken ct)
    {
        var (parentTable, parentId) = parent;
        IReadOnlyList<FiledRootRef> filed;
        bool depthBoundReached;
        try
        {
            // Round 61 item 1: every secure record filed BELOW the parent, at any depth (bounded, cycle-safe).
            var walk = await SecureRootInheritance.ListFiledRootsBelowAsync(_dataverse, _logger, new[] { parent }, ct)
                .ConfigureAwait(false);
            (filed, depthBoundReached) = (walk.Roots, walk.DepthBoundReached);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: the records filed under {Type} {RecordId} could not be read.",
                run.EntryId, parentTable, parentId);
            FiledIncomplete(parentTable, parentId, run);
            return;
        }

        var failuresBefore = run.Failures.Count;
        var undecided = 0;
        foreach (var root in filed)
        {
            if (!reached.Add((root.Table, root.Id)))
                continue; // covered in its own right, or already reached through another parent (or a pair naming itself)

            if (!root.Confirmed)
            {
                undecided++; // whether it is filed under this record could not be read: nothing removed on a guess
                continue;
            }

            if (run.CoveredRecords >= MaxCoveredRecords)
            {
                run.Truncated = true;
                break;
            }

            run.CoveredRecords++;
            await EnforceOnRecordAsync((root.Table, root.Id), users, author, cacheTenants, run, ct).ConfigureAwait(false);
        }

        // A chain filed deeper than the walk follows is not "everything reached": fail closed (children-incomplete).
        if (undecided > 0 || depthBoundReached || run.Failures.Count > failuresBefore)
        {
            FiledIncomplete(parentTable, parentId, run);
        }
    }

    private void FiledIncomplete(string parentTable, Guid parentId, Run run)
    {
        _logger.LogWarning(
            "[NO-ACCESS-ENFORCE] Entry {EntryId}: not every secure record filed under {Type} {RecordId} could be enforced yet.",
            run.EntryId, parentTable, parentId);
        run.Fail(parentTable, parentId, null, "children-incomplete",
            $"The No Access entry could not yet be applied to every secure work assignment or project filed under {parentTable} " +
            $"{parentId}. The scheduled safety net finishes it within a few minutes; open those records' Manage Access to check.");
    }

    /// <summary>
    /// Task 149 (merged after this task, AC6): a root share this run removed leaves the secure record's CHILDREN at once —
    /// <see cref="SecureChildShareSynchronizer.SyncRootAsync"/> mirrors the root's remaining share set onto them — rather
    /// than at the next reconcile tick. The root removal stands whatever this answers; a fan-out that could not finish is a
    /// failure naming the record (the 2-minute reconcile completes it), never "complete".
    /// </summary>
    private async Task SyncChildrenAsync(string logicalName, Guid recordId, Run run, CancellationToken ct)
    {
        SecureChildShareSyncResult children;
        try
        {
            children = await _secureChildShares.SyncRootAsync(logicalName, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Entry {EntryId}: the children of {Type} {RecordId} could not be updated.",
                run.EntryId, logicalName, recordId);
            children = SecureChildShareSyncResult.Failed("the children could not be updated");
        }

        if (children.IsComplete)
        {
            return;
        }

        _logger.LogWarning(
            "[NO-ACCESS-ENFORCE] Entry {EntryId}: removed on {Type} {RecordId}, but its children are {Status} " +
            "({NotUpdated} not updated, {Held} held of {InScope}; {Detail}).",
            run.EntryId, logicalName, recordId, children.Status, children.ChildrenNotUpdated, children.ChildrenHeld,
            children.ChildrenInScope, children.Detail);
        run.Fail(logicalName, recordId, null, "children-incomplete",
            $"Access to {logicalName} {recordId} was removed, but not every related record (documents, events, to-dos, " +
            "communications) could be updated yet. The scheduled safety net finishes it within a few minutes; open the " +
            "record's Manage Access to check.");
    }

    private async Task EnforceForUserAsync(
        string logicalName,
        string entitySet,
        Guid recordId,
        Guid userId,
        Guid? owningTeam,
        bool owningTeamRead,
        IReadOnlyCollection<string> cacheTenants,
        Run run,
        CancellationToken ct)
    {
        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await _recordShare.GetPrincipalAccessOrThrowAsync(logicalName, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Shares on {Type} {RecordId} unreadable.", logicalName, recordId);
            run.Fail(logicalName, recordId, userId, "shares-unreadable",
                $"The shares on this record could not be read, so user {userId}'s access was not removed. Try again.");
            return;
        }

        var principal = DataversePrincipalRef.User(userId);
        var direct = MaskOf(shares, principal);
        var directRemains = false;

        if (direct != 0)
        {
            var removal = await RemoveUnderRecordLockAsync(logicalName, entitySet, recordId, userId, cacheTenants, run, ct)
                .ConfigureAwait(false);
            if (removal is null)
            {
                return; // a failure was recorded; residual cannot be judged while the share may remain
            }

            directRemains = !removal.Value.Removed;
            shares = removal.Value.SharesAfter;
        }

        if (directRemains)
        {
            return; // S5 kept the share on purpose; residual access through it is the share itself
        }

        await ReportResidualAccessAsync(logicalName, entitySet, recordId, userId, shares, owningTeam, owningTeamRead, run, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Owner S5 under concurrency (task 143 r1): the removal of a direct share on a secure record runs under a
    /// per-RECORD lease, so two enforcements — the save-time endpoint, a concurrent endpoint call for another entry, and
    /// the job — can never each see the other's user as "someone else keeps access" and both remove. Under the lease the
    /// shares are READ AGAIN, and the S5 check, the revoke and the read-back are decided from that fresh read.
    /// </summary>
    /// <remarks>
    /// <para>The lease is the existing atomic primitive (<see cref="IScheduledJobLease"/>: Redis <c>SET NX PX</c> across
    /// every instance and slot, or process-local when Redis is off), keyed per record. It fails CLOSED: a record whose
    /// lease is held by another enforcement, or a lease store that cannot be reached, removes nothing and records a
    /// failure (the caller is told to try again; the job retries within 5 minutes). It serializes the enforcer with
    /// itself only — a user's own unshare (task 139) and the model-driven app's Share dialog do not take it.</para>
    /// <para><b>The lease is proven held at the revoke</b> (task 143 r2). The reads before the revoke — the re-read, the
    /// other readers' person state — can be slowed by throttling past any fixed duration, and a lease that expired in
    /// between would let a second enforcement take it, read this user's share as "someone else keeps access", and remove
    /// its own. So the lease is RENEWED immediately before the revoke: a renewal that fails (the lease expired, or
    /// another holder has it) or cannot be made removes nothing and records a failure. A successful renewal proves the
    /// lease was never lost since it was taken, and gives the revoke a full <see cref="RecordLockDuration"/>.</para>
    /// </remarks>
    private async Task<(bool Removed, IReadOnlyList<DataversePrincipalAccess> SharesAfter)?> RemoveUnderRecordLockAsync(
        string logicalName,
        string entitySet,
        Guid recordId,
        Guid userId,
        IReadOnlyCollection<string> cacheTenants,
        Run run,
        CancellationToken ct)
    {
        var lockId = RecordLockId(logicalName, recordId);
        ScheduledJobLeaseGrant grant;
        try
        {
            grant = await _recordLock.TryAcquireAsync(lockId, occurrenceUtc: null, RecordLockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] The removal lock for {Type} {RecordId} could not be taken.",
                logicalName, recordId);
            run.Fail(logicalName, recordId, userId, "record-lock-unavailable",
                $"User {userId}'s share was not removed: the lock that keeps a secure record from losing its last reader " +
                "could not be taken. Try again.");
            return null;
        }

        if (grant.Status != ScheduledJobLeaseStatus.Granted || grant.Token is null)
        {
            run.Fail(logicalName, recordId, userId, "record-busy",
                $"User {userId}'s share was not removed: another No Access enforcement is changing this record's access " +
                "right now. Try again in a moment; the 5-minute safety net retries too.");
            return null;
        }

        try
        {
            IReadOnlyList<DataversePrincipalAccess> shares;
            try
            {
                shares = await _recordShare.GetPrincipalAccessOrThrowAsync(logicalName, recordId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Shares on {Type} {RecordId} unreadable under the lock.", logicalName, recordId);
                run.Fail(logicalName, recordId, userId, "shares-unreadable",
                    $"The shares on this record could not be read, so user {userId}'s access was not removed. Try again.");
                return null;
            }

            var direct = MaskOf(shares, DataversePrincipalRef.User(userId));
            if (direct == 0)
            {
                return (true, shares); // gone meanwhile (another enforcement or an unshare): nothing left to remove
            }

            return await RemoveDirectShareAsync(
                    logicalName, entitySet, recordId, userId, direct, shares, (lockId, grant.Token), cacheTenants, run, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _recordLock.ReleaseAsync(lockId, grant.Token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lease expires on its own (RecordLockDuration).
                _logger.LogWarning(ex, "[NO-ACCESS-ENFORCE] The removal lock for {Type} {RecordId} could not be released.",
                    logicalName, recordId);
            }
        }
    }

    /// <summary>The per-record removal lock id (task 143 r1, S5).</summary>
    internal static string RecordLockId(string logicalName, Guid recordId) => $"no-access-enforce:{logicalName}:{recordId:D}";

    /// <summary>
    /// How long a removal lock lives from its last take or renewal (task 143 r2: was 2 minutes). It must outlast the ONE
    /// call made after the renewal that matters — the revoke, bounded by the Dataverse Web API client's 100-second
    /// timeout plus its up-to-30-second token-refresh wait — with margin. The cost: a holder that dies leaves the record's
    /// removals refused ("record-busy") for up to this long, which the 5-minute job absorbs.
    /// </summary>
    internal static readonly TimeSpan RecordLockDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// S5 check, lease renewal, revoke, read-back, cache clear. <c>null</c> when a failure was recorded; otherwise whether
    /// the share was removed (false = S5 kept it) and the shares as read back.
    /// </summary>
    private async Task<(bool Removed, IReadOnlyList<DataversePrincipalAccess> SharesAfter)?> RemoveDirectShareAsync(
        string logicalName,
        string entitySet,
        Guid recordId,
        Guid userId,
        int direct,
        IReadOnlyList<DataversePrincipalAccess> shares,
        (string Id, string Token) recordLock,
        IReadOnlyCollection<string> cacheTenants,
        Run run,
        CancellationToken ct)
    {
        // ── S5: never the last person who can see a secure record ──
        if (RecordShareLevels.CanRead(direct))
        {
            var otherReaders = shares
                .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && s.Principal.Id != userId
                            && RecordShareLevels.CanRead(s.AccessRightsMask))
                .Select(s => s.Principal.Id)
                .Distinct()
                .ToList();

            bool someoneElse;
            try
            {
                var people = otherReaders.Count == 0
                    ? new Dictionary<Guid, EnforcementSystemUser>()
                    : await _store.ReadSystemUsersAsync(otherReaders, ct).ConfigureAwait(false);
                someoneElse = people.Values.Any(p => p.IsEnabledPerson);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Other readers of {Type} {RecordId} unreadable.", logicalName, recordId);
                run.Fail(logicalName, recordId, userId, "last-reader-unverifiable",
                    $"Could not confirm that someone else keeps access to this secure record, so user {userId}'s share was " +
                    "not removed. Try again.");
                return null;
            }

            if (!someoneElse)
            {
                _logger.LogWarning(
                    "[NO-ACCESS-ENFORCE] Entry {EntryId}: {UserId} is the last person who can open secure {Type} {RecordId}; " +
                    "the share was kept (S5).", run.EntryId, userId, logicalName, recordId);
                run.NotEnforced.Add(new NoAccessNotEnforced(logicalName, recordId, userId,
                    NoAccessEnforcementReason.LastPersonOnSecureRecord));
                return (false, shares);
            }
        }

        // ── The lease must still be ours at the revoke (task 143 r2) ──
        if (!await RenewRecordLockAsync(recordLock, logicalName, recordId, userId, run, ct).ConfigureAwait(false))
        {
            return null;
        }

        // ── Revoke, then read back ──
        IReadOnlyList<DataversePrincipalAccess>? after = null;
        Exception? failure = null;
        try
        {
            await _recordShare.RevokeAccessAsync(entitySet, recordId, DataversePrincipalRef.User(userId), ct).ConfigureAwait(false);
            after = await _recordShare.GetPrincipalAccessOrThrowAsync(logicalName, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            foreach (var tenant in cacheTenants)
            {
                await ImpersonatedRootSetSource.InvalidateForTenantAsync(_cache, tenant, userId, logicalName, _logger)
                    .ConfigureAwait(false);
            }
        }

        var remaining = after is null ? (int?)null : MaskOf(after, DataversePrincipalRef.User(userId));
        if (failure is not null || remaining != 0)
        {
            _logger.LogError(failure,
                "[NO-ACCESS-ENFORCE] Entry {EntryId}: removing {UserId}'s share on {Type} {RecordId} was NOT confirmed " +
                "(held {Previous}; read-back {Remaining}).",
                run.EntryId, userId, logicalName, recordId, direct, remaining?.ToString() ?? "unreadable");
            run.Fail(logicalName, recordId, userId, "revoke-not-confirmed",
                $"Removing user {userId}'s share on {logicalName} {recordId} could not be confirmed. Open the record's " +
                "Manage Access to see whether they still have access, then try again.");
            return null;
        }

        run.Removed.Add(new NoAccessRemovedShare(userId, logicalName, recordId, direct));
        return (true, after!);
    }

    /// <summary>
    /// Renews the removal lease immediately before a revoke (task 143 r2). <c>false</c> — with a failure recorded and
    /// nothing removed — when the lease is no longer this enforcement's (it expired; another may hold it) or the store
    /// cannot be reached.
    /// </summary>
    private async Task<bool> RenewRecordLockAsync(
        (string Id, string Token) recordLock, string logicalName, Guid recordId, Guid userId, Run run, CancellationToken ct)
    {
        bool held;
        try
        {
            held = await _recordLock.RenewAsync(recordLock.Id, recordLock.Token, RecordLockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] The removal lock for {Type} {RecordId} could not be renewed.",
                logicalName, recordId);
            run.Fail(logicalName, recordId, userId, "record-lock-unavailable",
                $"User {userId}'s share was not removed: the lock that keeps a secure record from losing its last reader " +
                "could not be confirmed before the removal. Try again.");
            return false;
        }

        if (!held)
        {
            _logger.LogWarning(
                "[NO-ACCESS-ENFORCE] Entry {EntryId}: the removal lock for {Type} {RecordId} expired before {UserId}'s share " +
                "was removed; nothing was removed.", run.EntryId, logicalName, recordId, userId);
            run.Fail(logicalName, recordId, userId, "record-lock-lost",
                $"User {userId}'s share was not removed: this enforcement took too long and its hold on the record expired, " +
                "so another change may be under way. Try again; the 5-minute safety net retries too.");
        }

        return held;
    }

    /// <summary>
    /// Owner N2 / criterion 7: what the user still reaches the record through after its direct share is gone — a team
    /// share or team ownership of a team it belongs to, or Dataverse's own answer with nothing behind it (a role or the
    /// business unit). Reported, never revoked. A check that cannot be completed is a failure, never "nothing left".
    /// </summary>
    private async Task ReportResidualAccessAsync(
        string logicalName,
        string entitySet,
        Guid recordId,
        Guid userId,
        IReadOnlyList<DataversePrincipalAccess> shares,
        Guid? owningTeam,
        bool owningTeamRead,
        Run run,
        CancellationToken ct)
    {
        var shareTeams = shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.Team && s.AccessRightsMask != 0)
            .Select(s => s.Principal.Id)
            .Distinct()
            .ToList();
        var teams = owningTeam is { } ot ? shareTeams.Append(ot).Distinct().ToList() : shareTeams;

        var attributed = false;
        try
        {
            if (teams.Count > 0)
            {
                var memberOf = await _store.ReadTeamMembershipsAsync(userId, teams, ct).ConfigureAwait(false);
                foreach (var teamId in memberOf)
                {
                    attributed = true;
                    run.NotEnforceable.Add(new NoAccessNotEnforceable(userId, logicalName, recordId,
                        teamId == owningTeam ? NoAccessEnforcementReason.TeamOwnership : NoAccessEnforcementReason.TeamShare,
                        teamId));
                }
            }

            if (!attributed)
            {
                var rights = await _store.GetPrincipalRightsAsync(userId, entitySet, recordId, ct).ConfigureAwait(false);
                if (rights.HasFlag(AccessRights.Read))
                {
                    run.NotEnforceable.Add(new NoAccessNotEnforceable(userId, logicalName, recordId,
                        NoAccessEnforcementReason.RoleOrBusinessUnit, null));
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[NO-ACCESS-ENFORCE] Residual access of {UserId} on {Type} {RecordId} unverifiable.",
                userId, logicalName, recordId);
            run.Fail(logicalName, recordId, userId, "residual-access-unverifiable",
                $"Whether user {userId} still reaches this record through a team or a role could not be checked. Try again.");
            return;
        }

        if (!owningTeamRead)
        {
            run.Fail(logicalName, recordId, userId, "residual-access-unverifiable",
                "The record's owner could not be read, so access through team ownership could not be checked. Try again.");
            return;
        }

        foreach (var item in run.NotEnforceable.Where(n => n.SystemUserId == userId && n.RecordId == recordId))
        {
            _logger.LogWarning(
                "[NO-ACCESS-ENFORCE] Wall not enforceable in Dataverse for user {UserId} on {Type} {RecordId} via " +
                "{Mechanism} {TeamId} (entry {EntryId}). Teams/SPA hides the record; MDA still admits the user (owner N2).",
                userId, logicalName, recordId, item.Mechanism, item.TeamId, run.EntryId);
        }
    }

    /// <summary>The entity set and primary key of a secure root table, by logical name.</summary>
    private static (string EntitySet, string IdColumn) RootTable(string logicalName)
    {
        var type = logicalName switch
        {
            "sprk_project" => ExternalGrantRootType.Project,
            "sprk_matter" => ExternalGrantRootType.Matter,
            "sprk_workassignment" => ExternalGrantRootType.WorkAssignment,
            _ => throw new ArgumentOutOfRangeException(nameof(logicalName), logicalName, "Not a secure root table."),
        };
        return (ExternalGrantRoot.BindFor(type).EntitySet, $"{ExternalGrantRoot.LogicalNameFor(type)}id");
    }

    private static int MaskOf(IReadOnlyList<DataversePrincipalAccess> shares, DataversePrincipalRef principal)
        => shares.Where(s => s.Principal == principal).Aggregate(0, (mask, s) => mask | s.AccessRightsMask);

    /// <summary>The mutable accumulator of one enforcement.</summary>
    private sealed class Run
    {
        public Run(Guid entryId, NoAccessSubjectKinds kind)
        {
            EntryId = entryId;
            Kind = kind;
        }

        public Guid EntryId { get; }

        public NoAccessSubjectKinds Kind { get; }

        public List<NoAccessRemovedShare> Removed { get; } = new();

        public List<NoAccessNotEnforceable> NotEnforceable { get; } = new();

        public List<NoAccessNotEnforced> NotEnforced { get; } = new();

        public List<NoAccessEnforcementFailure> Failures { get; } = new();

        public bool Truncated { get; set; }

        public int CoveredUsers { get; set; }

        public int CoveredRecords { get; set; }

        public void Fail(string? type, Guid? recordId, Guid? userId, string kind, string message)
            => Failures.Add(new NoAccessEnforcementFailure(type, recordId, userId, kind, message));

        public NoAccessEnforcementReport ToReport() => new(
            EntryId, NoAccessEnforcementOutcome.Evaluated, SubjectKindName(Kind), Removed, NotEnforceable, NotEnforced,
            Failures, CoveredUsers, CoveredRecords, Truncated);

        private static string SubjectKindName(NoAccessSubjectKinds kind) => kind switch
        {
            NoAccessSubjectKinds.SystemUser => "systemuser",
            NoAccessSubjectKinds.Contact => "contact",
            _ => "organization",
        };
    }
}
