// unified-access-control-r2 task 158 (owner round 6, 2026-10-02): "a work assignment (or project) filed under a SECURE
// matter or project is itself secure — yes". Such a record becomes a real secure root: sprk_issecure, the named-team owner,
// its own container and the creator share — through provisioning's own steps — and the parent's sharees can see it.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — ProvisionProjectEndpoint secures ONE record a caller names; SecureChildReconciler (task 148) re-owns the
//       CHILDREN of a record and deliberately never touches a root filed under it; RecordOwnershipResolver decides a child's
//       owner and refuses a root under a secure parent (146: "a root under a secure parent is task 158"); the synchronizer
//       (149) mirrors onto Secure-team-owned CHILD tables only; RecordContainerResolver (155) only reads a record's filing to
//       place its files. Nothing answers "this work assignment / project is filed under a secure record, so make it secure".
//   (2) Extension — Not inside ProvisionProjectEndpoint: the question "which roots are filed under which, and is the parent
//       secure" is asked by five writers, the transition and a job; the endpoint stays the ONE place that secures a record
//       (this class calls it — ProvisionInheritedAsync — never re-implements a step). Not inside the reconciler: its walk
//       and its "never move a root" rule are load-bearing for 148 (a child pass re-owns, a root needs a container and a
//       creator share). Not inside the resolver: it decides owners and holds no provisioning dependencies.
//   (3) Cost-of-doing-nothing — a work assignment filed under a secure matter stays readable by its whole business unit,
//       its files go to the matter's container (155 interpretation iii) while its access follows its own business unit,
//       and a contact granted on it gets the access FR-22 forbids on the secure matter (task 155 round f3 finding).
//
// Placement (bff-extensions.md; ADR-052): in the BFF. Create and re-file are BFF write paths (the caller is told what
// happened), the transition runs inside provisioning's request, and the safety net is an in-process IScheduledJob
// (SecureRootInheritanceJob, ADR-036). BFF identity, BFF domain code, low volume. No package, no column, no plugin
// (ADR-002), no new endpoint.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>What became of one work assignment or project the inheritance looked at.</summary>
public enum SecureRootInheritOutcome
{
    /// <summary>The record does not exist (any more).</summary>
    NotFound,

    /// <summary>No matter or project it is filed under is secure: nothing to do.</summary>
    NotFiledUnderSecure,

    /// <summary>Filed under a secure record and already isolated (owned by the named team, with its own container).</summary>
    AlreadySecure,

    /// <summary>Filed under a secure record and made secure by this call (provisioning completed).</summary>
    Secured,

    /// <summary>Whether a record it is filed under is secure could not be determined (ADR-003): nothing was written.</summary>
    Unverifiable,

    /// <summary>Provisioning refused (a 4xx — e.g. no usable creator, the creator on the No Access list).</summary>
    Refused,

    /// <summary>Provisioning failed (a 5xx — a read or write failed; retried by the job).</summary>
    Failed,
}

/// <summary>A matter or project a work assignment or project is filed under, and whether it is isolated.</summary>
public sealed record SecureFilingParent(string Table, Guid Id, bool Isolated, string? Name = null);

/// <summary>What the inheritance did to one record.</summary>
public sealed record SecureRootInheritResult(
    string Table,
    Guid Id,
    SecureRootInheritOutcome Outcome,
    string? ReasonCode,
    string? Detail,
    IReadOnlyList<SecureFilingParent> SecureParents,
    SecureChildShareSyncResult? Shares)
{
    /// <summary>Secure after the call.</summary>
    public bool IsSecure => Outcome is SecureRootInheritOutcome.AlreadySecure or SecureRootInheritOutcome.Secured;

    /// <summary>Its parents' sharees were all given to it (or there was nothing to give).</summary>
    public bool SharesComplete => Shares is null || Shares.IsComplete;

    /// <summary>Nothing is left to do for this record.</summary>
    public bool IsComplete =>
        Outcome is SecureRootInheritOutcome.NotFound or SecureRootInheritOutcome.NotFiledUnderSecure
        || (IsSecure && SharesComplete);

    /// <summary>The call changed something (secured the record, or gave it a sharee).</summary>
    public bool WroteAnything =>
        Outcome == SecureRootInheritOutcome.Secured || (Shares is { } s && s.SharesGranted + s.SharesChanged > 0);

    /// <summary>The outcome as the response names it.</summary>
    public string WireOutcome => !SharesComplete && IsSecure
        ? "failed"
        : Outcome switch
        {
            SecureRootInheritOutcome.Secured => "secured",
            SecureRootInheritOutcome.AlreadySecure => "already-secure",
            SecureRootInheritOutcome.NotFiledUnderSecure => "not-filed-under-secure",
            SecureRootInheritOutcome.NotFound => "not-found",
            SecureRootInheritOutcome.Unverifiable => "unverifiable",
            SecureRootInheritOutcome.Refused => "refused",
            _ => "failed",
        };
}

/// <summary>How a pass over the records filed under one matter or project ended.</summary>
public enum SecureFiledRootsStatus
{
    /// <summary>Every filed record is secure (or turned out not to be filed under it).</summary>
    Completed,

    /// <summary>At least one filed record is not secure yet; each is listed with its reason.</summary>
    Incomplete,

    /// <summary>Nothing is filed under this record for the rule (a work assignment), or it is not secure.</summary>
    NotApplicable,

    /// <summary>The filed records could not be read: nothing was decided.</summary>
    Failed,
}

/// <summary>A pass over the work assignments and projects filed under one secure matter or project.</summary>
public sealed record SecureFiledRootsPass(SecureFiledRootsStatus Status, IReadOnlyList<SecureRootInheritResult> Results, string? Detail)
{
    /// <summary>True when nothing filed under the record is left to secure.</summary>
    public bool IsComplete => Status is SecureFiledRootsStatus.Completed or SecureFiledRootsStatus.NotApplicable;

    /// <summary>Records this pass made secure.</summary>
    public int Secured => Results.Count(r => r.Outcome == SecureRootInheritOutcome.Secured);

    /// <summary>The pass changed something.</summary>
    public bool WroteAnything => Results.Any(r => r.WroteAnything);

    /// <summary>The response shape.</summary>
    public SecureFiledRecordsSummary Summary() => new(
        Status.ToString(),
        Examined: Results.Count,
        Secured: Secured,
        AlreadySecure: Results.Count(r => r.Outcome == SecureRootInheritOutcome.AlreadySecure),
        Remaining: Results.Count(r => !r.IsComplete),
        Records: Results
            .Select(r => new SecureFiledRecordOutcome(
                SecureRootInheritance.WireTokenFor(r.Table), r.Id, r.WireOutcome,
                r.IsComplete ? null : r.ReasonCode ?? SecureRootInheritance.ReasonSharesIncomplete,
                r.IsComplete ? null : r.Detail ?? r.Shares?.Detail))
            .ToList());

    internal static SecureFiledRootsPass NotApplicable(string detail) =>
        new(SecureFiledRootsStatus.NotApplicable, Array.Empty<SecureRootInheritResult>(), detail);
}

/// <summary>
/// The secure matters / projects a record is filed under that COULD be read, and — when some other record it is filed under
/// could not be (an unreadable or empty flag, an unresolvable pair) — why. Secure-if-any: one readable secure parent is
/// enough to make the record secure, whatever the others say; an unreadable one only means "not provably not secure" (it
/// refuses a write that would otherwise proceed, and it holds the sharee mirror, never the securing).
/// </summary>
public sealed record SecureParentsAnswer(IReadOnlyList<SecureFilingParent> SecureParents, string? Unverifiable)
{
    /// <summary>Every record it is filed under was read.</summary>
    public bool IsKnown => Unverifiable is null;

    /// <summary>At least one record it is filed under is (readably) secure — the record is secure whatever the rest say.</summary>
    public bool HasSecureParent => SecureParents.Count > 0;
}

/// <summary>A work assignment or project filed under a given record.</summary>
/// <param name="Confirmed"><c>true</c> when it is PROVABLY filed under one of the records asked about: a typed lookup, or a
/// pair whose type was read and names that record's table. <c>false</c>: a pair naming the id whose type could not be read —
/// a candidate the per-record decision reports (unverifiable), never a record shown as "related".</param>
public sealed record FiledRootRef(string Table, Guid Id, string? Name, bool FlaggedSecure, bool Confirmed = true);

/// <summary>
/// unified-access-control-r2 task 158 — a <c>sprk_workassignment</c> or <c>sprk_project</c> FILED UNDER a secure
/// <c>sprk_matter</c> or <c>sprk_project</c> is itself secure (owner round 6), secured through
/// <see cref="ProvisionProjectEndpoint.ProvisionInheritedAsync"/> — provisioning's own steps, for the person who created it —
/// and given its parents' sharees by <see cref="SecureChildShareSynchronizer.SyncInheritedRootAsync"/> (task 149's mechanism).
/// </summary>
/// <remarks>
/// <para><b>"Filed under"</b> (the POML's goal, task 155's sweep): a work assignment's typed <c>sprk_regardingmatter</c> /
/// <c>sprk_regardingproject</c>, or the polymorphic pair (<c>sprk_regardingrecordid</c> + <c>sprk_regardingrecordtype</c>)
/// naming a matter or project; a project's pair (a project has no typed regarding lookup). Secure-if-ANY: one secure parent is
/// enough. A pair naming any other type (an invoice, a contact, a work assignment) is not this rule.</para>
/// <para><b>Fail closed (ADR-003).</b> A pair that cannot be resolved (an id that is not a GUID, no type, an unreadable type)
/// or a parent whose flag cannot be read — or reads EMPTY (owner round 17 item 3) — is never "not secure". When no OTHER
/// parent is readably secure the record is <see cref="SecureRootInheritOutcome.Unverifiable"/>: nothing is written, a BFF
/// create or re-file is refused (<see cref="CheckRefileAsync"/>), and the job reports it. When another parent IS readably
/// secure, the record is secured (secure-if-any; securing is the closed direction) and only its parents' sharees are held
/// (the intersection rule cannot be evaluated against a parent that cannot be read). A parent that does not exist is not a
/// secure parent (it confers nothing).</para>
/// <para><b>Never auto-unsecure</b> (owner round 6 item 4): nothing here ever takes a record OUT of isolation. A record re-filed
/// away from its secure parent, or whose parent is unsecured, stays secure; unsecuring it is the unsecure endpoint's act
/// (F3).</para>
/// <para><b>Triggers.</b> (1) create and (2) re-file: the BFF writers call <see cref="CheckRefileAsync"/> before the write and
/// <see cref="SecureAfterWriteAsync"/> after it; (3) a parent becoming secure: provisioning Step 8 calls
/// <see cref="SecureFiledRootsUnderAsync"/>; (4) writes outside the BFF: <see cref="SecureRootInheritanceJob"/> (≤ 5 min).
/// A parent's sharees reach its secure filed records through <see cref="PassSharesOnAsync"/> — from <c>/share-user</c>, and
/// from a project that was just given sharees itself — and through the job.</para>
/// </remarks>
public sealed class SecureRootInheritance
{
    internal const string WorkAssignment = "sprk_workassignment";
    internal const string Project = "sprk_project";
    internal const string Matter = "sprk_matter";

    internal const string PairIdColumn = "sprk_regardingrecordid";
    internal const string PairTypeColumn = "sprk_regardingrecordtype";
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string RecordTypeLogicalNameColumn = "sprk_recordlogicalname";
    private const string IsSecureColumn = "sprk_issecure";
    private const string OwningTeamColumn = "owningteam";
    private const string ContainerColumn = "sprk_containerid";

    /// <summary>A pair, a type or another filing value could not be resolved to a record: nothing was written.</summary>
    internal const string ReasonParentUndetermined = "sdap.inherit.parent_undetermined";

    /// <summary>Whether a matter or project the record is filed under is secure could not be read (or read empty).</summary>
    internal const string ReasonParentUnverifiable = "sdap.inherit.parent_unverifiable";

    /// <summary>The record itself could not be read.</summary>
    internal const string ReasonRecordUnreadable = "sdap.inherit.record_unreadable";

    /// <summary>Records filed under records filed under … deeper than <see cref="MaxNestedProvisioning"/>: left to the job.</summary>
    internal const string ReasonTooDeep = "sdap.inherit.too_deep";

    /// <summary>The record is secure but its parents' sharees could not all be given to it.</summary>
    internal const string ReasonSharesIncomplete = "sdap.inherit.shares_incomplete";

    /// <summary>Provisioning answered an unexpected result.</summary>
    internal const string ReasonUnexpectedResult = "sdap.inherit.unexpected_result";

    /// <summary>
    /// A sharee-only pass (a parent's share changed) found a filed record that is not secure yet: securing it is not that
    /// request's act — the job (or the next provisioning of the parent) does it.
    /// </summary>
    internal const string ReasonNotYetSecure = "sdap.inherit.not_yet_secure";

    /// <summary>
    /// How many inherited passes may run inside one another (a project secured inside a matter's provisioning, whose own
    /// Step 8 secures the records filed under IT …; or a project's new sharees passed on to the records filed under it).
    /// Past it, the record is left to the job, which reaches it on its next run as a record filed under a now-secure parent.
    /// </summary>
    internal const int MaxNestedProvisioning = 3;

    /// <summary>Rows per page of a filed-records read; past one page the read fails rather than decide on part of it.</summary>
    internal const int FiledPageSize = 5000;

    /// <summary>Parent ids per IN condition.</summary>
    internal const int IdsPerQuery = 200;

    /// <summary>The typed regarding lookups that file each inheriting table under a parent (live metadata, task 155 sweep).</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string ParentTable)>> TypedFilingColumns =
        new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkAssignment] = new[] { ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project) },
            [Project] = Array.Empty<(string, string)>(),
        };

    private static readonly AsyncLocal<int> NestedProvisioning = new();

    private readonly IGenericEntityService _dataverse;
    private readonly DataverseWebApiClient _webApi;
    private readonly SpeFileStore _speFileStore;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly SecureChildReconciler _secureChildren;
    private readonly SecureChildShareSynchronizer _synchronizer;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureRootInheritance> _logger;

    public SecureRootInheritance(
        IGenericEntityService dataverse,
        DataverseWebApiClient webApi,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureChildShareSynchronizer synchronizer,
        SecureShareNoAccessGuard noAccessGuard,
        IConfiguration configuration,
        ILogger<SecureRootInheritance> logger)
    {
        _dataverse = dataverse;
        _webApi = webApi;
        _speFileStore = speFileStore;
        _recordShare = recordShare;
        _secureChildren = secureChildren;
        _synchronizer = synchronizer;
        _noAccessGuard = noAccessGuard;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>True for a table whose rows inherit security from what they are filed under.</summary>
    public static bool Inherits(string? table) =>
        string.Equals(table?.Trim(), WorkAssignment, StringComparison.OrdinalIgnoreCase)
        || string.Equals(table?.Trim(), Project, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a table whose rows pass security on to what is filed under them.</summary>
    public static bool IsParent(string? table) =>
        string.Equals(table?.Trim(), Matter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(table?.Trim(), Project, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every column that decides what a row of <paramref name="table"/> is filed under.</summary>
    internal static IReadOnlySet<string> FilingColumnsOf(string table) =>
        new HashSet<string>(
            (TypedFilingColumns.TryGetValue(table, out var typed) ? typed.Select(t => t.Column) : Enumerable.Empty<string>())
                .Append(PairIdColumn).Append(PairTypeColumn),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The wire token for an inheriting table.</summary>
    internal static string WireTokenFor(string table) =>
        string.Equals(table, Project, StringComparison.OrdinalIgnoreCase) ? "project"
        : string.Equals(table, Matter, StringComparison.OrdinalIgnoreCase) ? "matter"
        : "workassignment";

    // ── (1) + (2): create and re-file ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BEFORE a BFF create or re-file of a work assignment or project: when the write sets or clears what the row is filed
    /// under, the records it will be filed under AFTER the write are resolved and their flags read. If one cannot be read
    /// and none is readably secure, the write is refused — <see cref="RecordOwnerRefusal.ParentUndetermined"/>, the code
    /// every writer already refuses in its own contract — and nothing is written (owner: "if the parent's flag cannot be
    /// read, refuse"). A readably secure parent lets the write proceed: the record is secured after it.
    /// <c>null</c>: proceed (the write then calls <see cref="SecureAfterWriteAsync"/>).
    /// </summary>
    /// <param name="table">The row's table.</param>
    /// <param name="recordId">The row, or <c>null</c> for a create.</param>
    /// <param name="writes">The columns the write sets, by any spelling a BFF writer uses: the logical name, or the
    /// navigation property with <c>@odata.bind</c>; values as <see cref="EntityReference"/>, <see cref="Guid"/>, a bind path
    /// (<c>/sprk_matters(…)</c>), a GUID string, or <c>null</c> for a clear.</param>
    public async Task<RecordOwnerResolution?> CheckRefileAsync(
        string table, Guid? recordId, IEnumerable<KeyValuePair<string, object?>> writes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (!Inherits(table))
            return null;

        List<FilingWrite> filingWrites;
        try
        {
            filingWrites = FilingWritesIn(table, writes);
        }
        catch (FilingValueException ex)
        {
            // A value no writer spelling resolves to a record (neither a reference, an id nor a bind path) is never read as
            // "filed under nothing" (ADR-003): refused, nothing written.
            return Refused(table, ex.Message);
        }

        if (filingWrites.Count == 0)
            return null;

        FilingFacts after;
        try
        {
            var before = recordId is { } id ? await ReadFactsAsync(table, id, ct).ConfigureAwait(false) : null;
            after = Overlay(table, before, filingWrites);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read before a re-file; refusing.", table, recordId);
            return Refused(table, "the record could not be read, so what it would be filed under cannot be checked");
        }

        var answer = await DecideParentsAsync(after, ct).ConfigureAwait(false);

        // Secure-if-any: a readable secure parent means the record is secured after the write whatever another parent says
        // (securing is the closed direction). Only when no parent is readably secure does an unreadable one refuse.
        if (!answer.HasSecureParent && !answer.IsKnown)
            return Refused(table, answer.Unverifiable!);

        if (answer.HasSecureParent)
        {
            _logger.LogInformation(
                "[SECURE-INHERIT] {Table} {RecordId} is being filed under secure record(s) {Parents}; it is secured after the write" +
                "{Held}.", table, recordId, string.Join(", ", answer.SecureParents.Select(p => $"{p.Table}:{p.Id:D}")),
                answer.IsKnown ? string.Empty : $" (its parents' sharees are held: {answer.Unverifiable})");
        }

        return null;
    }

    /// <summary>
    /// AFTER a BFF create or re-file: when <paramref name="writtenColumns"/> (the create's or the update's own columns)
    /// touched what the row is filed under — or <paramref name="writtenColumns"/> is <c>null</c>, "unknown, check" — secures
    /// it if it is now filed under a secure record. A write that files nothing costs no read. Never
    /// throws: a failure is logged, returned, and left to <see cref="SecureRootInheritanceJob"/> (≤ 5 minutes).
    /// </summary>
    public async Task<SecureRootInheritResult?> SecureAfterWriteAsync(
        string table, Guid recordId, IEnumerable<string>? writtenColumns, string? traceId, CancellationToken ct)
    {
        if (!Inherits(table) || recordId == Guid.Empty)
            return null;

        if (writtenColumns is not null)
        {
            var filing = FilingColumnsOf(table);
            if (!writtenColumns.Select(NormalizeColumn).Any(filing.Contains))
                return null;
        }

        try
        {
            var result = await SecureIfFiledUnderSecureAsync(table, recordId, traceId ?? Guid.NewGuid().ToString("N"), ct)
                .ConfigureAwait(false);
            if (!result.IsComplete)
            {
                _logger.LogError(
                    "[SECURE-INHERIT] {Table} {RecordId} was written but is not secured yet ({Outcome}, {Code}: {Detail}); the " +
                    "secure-root inheritance job retries it.", table, recordId, result.Outcome, result.ReasonCode, result.Detail);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-INHERIT] Securing {Table} {RecordId} after its write failed; the secure-root inheritance job retries it.",
                table, recordId);
            return new SecureRootInheritResult(
                table, recordId, SecureRootInheritOutcome.Failed, ReasonUnexpectedResult, "the securing step threw",
                Array.Empty<SecureFilingParent>(), null);
        }
    }

    // ── The one decision ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Secures ONE work assignment or project when it is filed under a secure matter or project, and gives it its secure
    /// parents' sharees. Idempotent: an isolated record is only given any sharee it is missing.
    /// </summary>
    public Task<SecureRootInheritResult> SecureIfFiledUnderSecureAsync(
        string table, Guid recordId, string traceId, CancellationToken ct) =>
        SecureIfFiledUnderSecureCoreAsync(table, recordId, traceId, allowProvisioning: true, ct);

    private async Task<SecureRootInheritResult> SecureIfFiledUnderSecureCoreAsync(
        string table, Guid recordId, string traceId, bool allowProvisioning, CancellationToken ct)
    {
        if (!Inherits(table))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Only work assignments and projects inherit security.");

        var logical = table.Trim().ToLowerInvariant();
        FilingFacts? facts;
        try
        {
            facts = await ReadFactsAsync(logical, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read.", logical, recordId);
            return Result(logical, recordId, SecureRootInheritOutcome.Unverifiable, ReasonRecordUnreadable, "the record could not be read");
        }

        if (facts is null)
            return Result(logical, recordId, SecureRootInheritOutcome.NotFound, null, null);

        var answer = await DecideParentsAsync(facts, ct).ConfigureAwait(false);
        if (!answer.HasSecureParent)
        {
            if (answer.IsKnown)
                return Result(logical, recordId, SecureRootInheritOutcome.NotFiledUnderSecure, null, null);

            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId}: whether a record it is filed under is secure could not be determined ({Why}); " +
                "nothing was written.", logical, recordId, answer.Unverifiable);
            return Result(logical, recordId, SecureRootInheritOutcome.Unverifiable, ReasonParentUnverifiable, answer.Unverifiable);
        }

        var isolated = await IsIsolatedAsync(facts, ct).ConfigureAwait(false);
        SecureRootInheritOutcome outcome;
        if (isolated)
        {
            outcome = SecureRootInheritOutcome.AlreadySecure;
        }
        else if (!allowProvisioning)
        {
            return Result(logical, recordId, SecureRootInheritOutcome.Failed, ReasonNotYetSecure,
                "it is not secure yet; the secure-root inheritance job secures it", answer.SecureParents);
        }
        else if (NestedProvisioning.Value >= MaxNestedProvisioning)
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record, {Depth} inherited provisionings deep; left to " +
                "the secure-root inheritance job.", logical, recordId, NestedProvisioning.Value);
            return Result(logical, recordId, SecureRootInheritOutcome.Failed, ReasonTooDeep,
                "it is filed under records that were themselves being secured, more levels deep than one call secures",
                answer.SecureParents);
        }
        else
        {
            var provisioned = await ProvisionAsync(logical, recordId, traceId, ct).ConfigureAwait(false);
            if (provisioned.Outcome is not (SecureRootInheritOutcome.Secured or SecureRootInheritOutcome.AlreadySecure))
                return provisioned with { SecureParents = answer.SecureParents };
            outcome = provisioned.Outcome;
        }

        // The parents' sharees (task 149's mechanism) — every isolated secure parent; a flagged-but-not-isolated one holds.
        // When another record it is filed under could not be read, NONE is given: a record under two parents gets only
        // the principals shared on both (the intersection rule), and the unread one may be secure (fail closed — held, the
        // job retries; the record itself is secure either way).
        var shares = answer.IsKnown
            ? await _synchronizer.SyncInheritedRootAsync(
                logical, recordId, answer.SecureParents.Select(p => (p.Table, p.Id)).ToArray(), ct).ConfigureAwait(false)
            : new SecureChildShareSyncResult(
                SecureChildShareSyncStatus.Incomplete, 1, 0, 0, 0, 1, 0, 0, 0, 0,
                $"its parents' sharees are held: {answer.Unverifiable}");

        // A project that was just GIVEN sharees passes them on to the secure records filed under IT (which may have been
        // secured, inside its own provisioning, before it had them): a sharee-only pass, never a provisioning. Bounded like
        // the provisionings; an incomplete pass is logged and completed by the job.
        if (IsParent(logical) && shares.SharesGranted + shares.SharesChanged > 0)
            await PassSharesOnAsync(logical, recordId, traceId, ct).ConfigureAwait(false);

        return new SecureRootInheritResult(
            logical, recordId, outcome,
            shares.IsComplete ? null : answer.IsKnown ? ReasonSharesIncomplete : ReasonParentUnverifiable,
            shares.IsComplete ? null : shares.Detail ?? "its parents' sharees could not all be given to it",
            answer.SecureParents, shares);
    }

    /// <summary>
    /// A matter's or project's sharees changed (a <c>/share-user</c> on it, or it was just given its own parents'):
    /// every SECURE work assignment / project filed under it is given its secure parents' sharees NOW (add-only), rather
    /// than at the job's next run (owner R3/R4: immediate on save, the job only a safety net). A filed record that is not
    /// secure yet is left to the job (<see cref="ReasonNotYetSecure"/>): a share is not the act that secures it. Never
    /// throws.
    /// </summary>
    public async Task<SecureFiledRootsPass> PassSharesOnAsync(string parentTable, Guid parentId, string traceId, CancellationToken ct)
    {
        if (!IsParent(parentTable))
            return SecureFiledRootsPass.NotApplicable($"nothing is filed under a {parentTable} for this rule");
        if (NestedProvisioning.Value >= MaxNestedProvisioning)
        {
            _logger.LogWarning(
                "[SECURE-INHERIT] Passing {Parent} {ParentId}'s sharees on is {Depth} passes deep; left to the secure-root " +
                "inheritance job.", parentTable, parentId, NestedProvisioning.Value);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Incomplete, Array.Empty<SecureRootInheritResult>(),
                "more levels deep than one call passes sharees on");
        }

        NestedProvisioning.Value++;
        try
        {
            var pass = await FiledRootsPassAsync(parentTable.Trim().ToLowerInvariant(), parentId, traceId, allowProvisioning: false, ct)
                .ConfigureAwait(false);
            if (!pass.IsComplete)
            {
                _logger.LogWarning(
                    "[SECURE-INHERIT] {Parent} {ParentId}'s sharees were not passed on to every secure record filed under it " +
                    "({Detail}); the secure-root inheritance job completes it.", parentTable, parentId, pass.Detail);
            }

            return pass;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] Passing {Parent} {ParentId}'s sharees on failed; the job completes it.",
                parentTable, parentId);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Failed, Array.Empty<SecureRootInheritResult>(),
                "passing the sharees on failed");
        }
        finally
        {
            NestedProvisioning.Value--;
        }
    }

    /// <summary>
    /// (3) A matter or project has become (or is) secure: every work assignment and project FILED UNDER it is secured and
    /// given its sharees. Called by provisioning's Step 8 after the record's own children. A work assignment, or a record
    /// that is not flagged secure, has nothing to pass on (<see cref="SecureFiledRootsStatus.NotApplicable"/>).
    /// </summary>
    public Task<SecureFiledRootsPass> SecureFiledRootsUnderAsync(
        string parentTable, Guid parentId, string traceId, CancellationToken ct) =>
        IsParent(parentTable)
            ? FiledRootsPassAsync(parentTable.Trim().ToLowerInvariant(), parentId, traceId, allowProvisioning: true, ct)
            : Task.FromResult(SecureFiledRootsPass.NotApplicable($"nothing is filed under a {parentTable} for this rule"));

    private async Task<SecureFiledRootsPass> FiledRootsPassAsync(
        string parent, Guid parentId, string traceId, bool allowProvisioning, CancellationToken ct)
    {
        IReadOnlyList<FiledRootRef> filed;
        try
        {
            var flag = await ReadParentAsync(parent, parentId, ct).ConfigureAwait(false);
            if (flag is null || flag.Value.Flag != true)
                return SecureFiledRootsPass.NotApplicable($"{parent} {parentId:D} is not secure");

            filed = await ListFiledRootsAsync(new[] { (parent, parentId) }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-INHERIT] The records filed under {Parent} {ParentId} could not be read.", parent, parentId);
            return new SecureFiledRootsPass(SecureFiledRootsStatus.Failed, Array.Empty<SecureRootInheritResult>(),
                "the work assignments and projects filed under it could not be read");
        }

        var results = new List<SecureRootInheritResult>();
        foreach (var root in filed)
        {
            if (root.Table == parent && root.Id == parentId)
                continue; // a project whose pair names itself is not filed under anything new

            results.Add(await SecureIfFiledUnderSecureCoreAsync(root.Table, root.Id, traceId, allowProvisioning, ct)
                .ConfigureAwait(false));
        }

        var complete = results.All(r => r.IsComplete);
        _logger.LogInformation(
            "[SECURE-INHERIT] Records filed under {Parent} {ParentId}: examined={Examined} secured={Secured} complete={Complete}.",
            parent, parentId, results.Count, results.Count(r => r.Outcome == SecureRootInheritOutcome.Secured), complete);

        return new SecureFiledRootsPass(
            complete ? SecureFiledRootsStatus.Completed : SecureFiledRootsStatus.Incomplete, results,
            complete ? null : $"{results.Count(r => !r.IsComplete)} of {results.Count} filed record(s) are not secure yet");
    }

    /// <summary>
    /// The secure matters / projects <paramref name="table"/> <paramref name="recordId"/> is filed under now — the unsecure
    /// endpoint's question ("a related record whose parent is STILL secure cannot be unsecured", owner round 6). A record
    /// that is not a work assignment or project, or that does not exist, has none.
    /// </summary>
    public async Task<SecureParentsAnswer> FindSecureParentsAsync(string table, Guid recordId, CancellationToken ct)
    {
        if (!Inherits(table))
            return new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), null);

        FilingFacts? facts;
        try
        {
            facts = await ReadFactsAsync(table.Trim().ToLowerInvariant(), recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {RecordId} could not be read.", table, recordId);
            return new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), "the record could not be read");
        }

        return facts is null
            ? new SecureParentsAnswer(Array.Empty<SecureFilingParent>(), null)
            : await DecideParentsAsync(facts, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The work assignments and projects filed under any of <paramref name="parents"/> (typed lookups, and the pair), with
    /// their flag. A pair match counts only when its TYPE names the matched record's table (a pair naming the id of a matter
    /// with an invoice type is not filed under the matter); one whose type cannot be read is returned as an unconfirmed
    /// candidate (<see cref="FiledRootRef.Confirmed"/> false). One page per query; a read that cannot complete THROWS
    /// (nothing is decided on part of it).
    /// </summary>
    public async Task<IReadOnlyList<FiledRootRef>> ListFiledRootsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var found = new Dictionary<(string, Guid), FiledRootRef>();

        foreach (var table in new[] { Project, WorkAssignment })
        {
            var nameColumn = table == Project ? "sprk_projectname" : "sprk_name";
            var columns = new[] { nameColumn, IsSecureColumn };

            // Typed lookups.
            foreach (var (column, parentTable) in TypedFilingColumns[table])
            {
                var ids = parents.Where(p => string.Equals(p.Table, parentTable, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Id).Distinct().ToArray();
                foreach (var chunk in ids.Chunk(IdsPerQuery))
                {
                    var query = Query(table, columns);
                    query.Criteria.AddCondition(column, ConditionOperator.In, chunk.Cast<object>().ToArray());
                    foreach (var row in await ReadOnePageAsync(query, ct).ConfigureAwait(false))
                        found[(table, row.Id)] = Ref(table, row, nameColumn);
                }
            }

            // The pair: its id is TEXT, so both GUID spellings a writer may store are asked for (Dataverse compares text
            // case-insensitively). Its TYPE decides whether the row is filed under the record with that id.
            var pairColumns = columns.Append(PairIdColumn).Append(PairTypeColumn).ToArray();
            var pairIds = parents.Select(p => p.Id).Distinct()
                .SelectMany(id => new object[] { id.ToString("D"), id.ToString("B") }).ToArray();
            foreach (var chunk in pairIds.Chunk(IdsPerQuery))
            {
                var query = Query(table, pairColumns);
                query.Criteria.AddCondition(PairIdColumn, ConditionOperator.In, chunk);
                foreach (var row in await ReadOnePageAsync(query, ct).ConfigureAwait(false))
                {
                    if (found.TryGetValue((table, row.Id), out var typed) && typed.Confirmed)
                        continue; // already filed by a typed lookup

                    var pairTable = await PairTableOfAsync(row, ct).ConfigureAwait(false);
                    var pairId = Guid.TryParse(row.GetAttributeValue<string>(PairIdColumn)?.Trim(), out var parsed) ? parsed : Guid.Empty;
                    if (pairTable is null)
                    {
                        found[(table, row.Id)] = Ref(table, row, nameColumn) with { Confirmed = false };
                    }
                    else if (parents.Any(p => p.Id == pairId && string.Equals(p.Table, pairTable, StringComparison.OrdinalIgnoreCase)))
                    {
                        found[(table, row.Id)] = Ref(table, row, nameColumn);
                    }
                }
            }
        }

        return found.Values.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();

        static FiledRootRef Ref(string table, Entity row, string nameColumn) =>
            new(table, row.Id, row.GetAttributeValue<string>(nameColumn), row.GetAttributeValue<bool?>(IsSecureColumn) == true);
    }

    /// <summary>The table a listed row's pair type names, or <c>null</c> when it has none or it cannot be read.</summary>
    private async Task<string?> PairTableOfAsync(Entity row, CancellationToken ct)
    {
        if (row.GetAttributeValue<EntityReference>(PairTypeColumn)?.Id is not { } typeRef || typeRef == Guid.Empty)
            return null;

        try
        {
            return await ReadRecordTypeAsync(typeRef, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-INHERIT] The regarding type {TypeRef} of {Table} {RecordId} could not be read.",
                typeRef, row.LogicalName, row.Id);
            return null;
        }
    }

    // ── Provisioning (the endpoint's own steps) ──────────────────────────────────────────────────────────────────────

    private async Task<SecureRootInheritResult> ProvisionAsync(string table, Guid recordId, string traceId, CancellationToken ct)
    {
        var root = SecureRecordRoot.For(table == Project ? ExternalGrantRootType.Project : ExternalGrantRootType.WorkAssignment);
        IResult result;
        NestedProvisioning.Value++;
        try
        {
            result = await ProvisionProjectEndpoint.ProvisionInheritedAsync(
                root, recordId, traceId, _webApi, _speFileStore, _recordShare, _secureChildren, this, _configuration,
                _noAccessGuard, _logger, ct).ConfigureAwait(false);
        }
        finally
        {
            NestedProvisioning.Value--;
        }

        switch (result)
        {
            case Ok<ProvisionProjectResponse>:
                _logger.LogInformation(
                    "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record and is now secure (provisioned for its " +
                    "creator). TraceId={TraceId}", table, recordId, traceId);
                return Result(table, recordId, SecureRootInheritOutcome.Secured, null, null);

            case ProblemHttpResult problem:
                var code = problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var value) ? value?.ToString() : null;
                if (problem.StatusCode == StatusCodes.Status409Conflict
                    && string.Equals(code, ProvisionProjectEndpoint.ReasonAlreadyProvisioned, StringComparison.Ordinal))
                {
                    return Result(table, recordId, SecureRootInheritOutcome.AlreadySecure, null, null);
                }

                var outcome = problem.StatusCode is >= 400 and < 500
                    ? SecureRootInheritOutcome.Refused
                    : SecureRootInheritOutcome.Failed;
                _logger.LogWarning(
                    "[SECURE-INHERIT] {Table} {RecordId} is filed under a secure record, but provisioning it answered {Status} " +
                    "{Code}: {Detail}. TraceId={TraceId}", table, recordId, problem.StatusCode, code, problem.ProblemDetails.Detail,
                    traceId);
                return Result(table, recordId, outcome, code ?? ReasonUnexpectedResult, problem.ProblemDetails.Detail);

            default:
                _logger.LogError(
                    "[SECURE-INHERIT] Provisioning {Table} {RecordId} answered an unexpected result ({Type}). TraceId={TraceId}",
                    table, recordId, result.GetType().Name, traceId);
                return Result(table, recordId, SecureRootInheritOutcome.Failed, ReasonUnexpectedResult,
                    "provisioning answered an unexpected result");
        }
    }

    private async Task<bool> IsIsolatedAsync(FilingFacts facts, CancellationToken ct)
    {
        if (facts.IsSecure != true || facts.OwningTeam is null || string.IsNullOrWhiteSpace(facts.ContainerId))
            return false;

        var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
        return team.TeamId is { } secureTeam && facts.OwningTeam == secureTeam;
    }

    // ── Reads ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What a row says it is filed under, and its own isolation facts.</summary>
    private sealed record FilingFacts(
        string Table,
        Guid Id,
        bool? IsSecure,
        Guid? OwningTeam,
        string? ContainerId,
        IReadOnlyDictionary<string, Guid?> Typed,
        string? PairId,
        Guid? PairType);

    /// <summary>The row's filing, or <c>null</c> when it does not exist. A fault propagates.</summary>
    private async Task<FilingFacts?> ReadFactsAsync(string table, Guid id, CancellationToken ct)
    {
        var typed = TypedFilingColumns[table];
        var query = Query(table,
            typed.Select(t => t.Column).Concat(new[] { IsSecureColumn, OwningTeamColumn, ContainerColumn, PairIdColumn, PairTypeColumn })
                .ToArray());
        query.TopCount = 1;
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
            return null;

        return new FilingFacts(
            table,
            id,
            row.GetAttributeValue<bool?>(IsSecureColumn),
            row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty ? team : null,
            row.GetAttributeValue<string>(ContainerColumn),
            typed.ToDictionary(
                t => t.Column,
                t => row.GetAttributeValue<EntityReference>(t.Column)?.Id is { } p && p != Guid.Empty ? p : (Guid?)null,
                StringComparer.OrdinalIgnoreCase),
            row.GetAttributeValue<string>(PairIdColumn),
            row.GetAttributeValue<EntityReference>(PairTypeColumn)?.Id is { } type && type != Guid.Empty ? type : null);
    }

    /// <summary>
    /// The secure matters / projects <paramref name="facts"/> names, and why any other could not be read. Typed lookups
    /// first; then the pair (agreement by identity with a typed lookup adds nothing; otherwise its type decides — a matter or
    /// project is a parent, any other type is not this rule). Every named record is read even after one fails, so a readable
    /// secure parent is never hidden behind an unreadable sibling (secure-if-any).
    /// </summary>
    private async Task<SecureParentsAnswer> DecideParentsAsync(FilingFacts facts, CancellationToken ct)
    {
        var named = new List<(string Table, Guid Id)>();
        string? unknown = null;
        foreach (var (column, parentTable) in TypedFilingColumns[facts.Table])
        {
            if (facts.Typed.TryGetValue(column, out var id) && id is { } parentId)
                named.Add((parentTable, parentId));
        }

        if (!string.IsNullOrWhiteSpace(facts.PairId))
        {
            if (!Guid.TryParse(facts.PairId.Trim(), out var pairId) || pairId == Guid.Empty)
            {
                unknown ??= $"its {PairIdColumn} does not hold a record identifier";
            }
            else if (!named.Any(n => n.Id == pairId))
            {
                if (facts.PairType is not { } typeRef)
                {
                    unknown ??= $"it names a record in {PairIdColumn} without that record's type";
                }
                else
                {
                    try
                    {
                        var pairTable = await ReadRecordTypeAsync(typeRef, ct).ConfigureAwait(false);
                        if (pairTable is null)
                            unknown ??= "the type of the record it is filed under could not be determined";
                        else if (IsParent(pairTable))
                            named.Add((pairTable, pairId));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "[SECURE-INHERIT] The regarding type {TypeRef} could not be read.", typeRef);
                        unknown ??= "the type of the record it is filed under could not be read";
                    }
                }
            }
        }

        var secure = new List<SecureFilingParent>();
        foreach (var (table, id) in named.Distinct())
        {
            (bool? Flag, bool Isolated, string? Name)? parent;
            try
            {
                parent = await ReadParentAsync(table, id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[SECURE-INHERIT] {Table} {Id} could not be read.", table, id);
                unknown ??= $"whether the {table} it is filed under is secure could not be read";
                continue;
            }

            if (parent is null)
                continue; // a record that does not exist confers nothing
            if (parent.Value.Flag is null)
            {
                unknown ??= $"the {table} it is filed under has no secure flag value (empty is never read as not secure)";
                continue;
            }

            if (parent.Value.Flag == true)
                secure.Add(new SecureFilingParent(table, id, parent.Value.Isolated, parent.Value.Name));
        }

        return new SecureParentsAnswer(secure, unknown);
    }

    /// <summary>A matter's or project's flag, whether it is isolated and its name, or <c>null</c> when it does not exist.</summary>
    private async Task<(bool? Flag, bool Isolated, string? Name)?> ReadParentAsync(string table, Guid id, CancellationToken ct)
    {
        var nameColumn = string.Equals(table, Matter, StringComparison.OrdinalIgnoreCase) ? "sprk_mattername" : "sprk_projectname";
        var query = Query(table, new[] { IsSecureColumn, OwningTeamColumn, nameColumn });
        query.TopCount = 1;
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
            return null;

        var flag = row.GetAttributeValue<bool?>(IsSecureColumn);
        var owner = row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;
        var isolated = false;
        if (flag == true && owner is { } team)
        {
            var secureTeam = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            isolated = secureTeam.TeamId == team;
        }

        return (flag, isolated, row.GetAttributeValue<string>(nameColumn));
    }

    private readonly ConcurrentDictionary<Guid, string?> _recordTypes = new();

    /// <summary>The logical name a <c>sprk_recordtype_ref</c> row stands for, or <c>null</c> when it has none / is gone.</summary>
    private async Task<string?> ReadRecordTypeAsync(Guid typeRef, CancellationToken ct)
    {
        if (_recordTypes.TryGetValue(typeRef, out var cached))
            return cached;

        var query = Query(RecordTypeRefEntity, new[] { RecordTypeLogicalNameColumn });
        query.TopCount = 1;
        query.Criteria.AddCondition(RecordTypeRefEntity + "id", ConditionOperator.Equal, typeRef);
        var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        var logical = row?.GetAttributeValue<string>(RecordTypeLogicalNameColumn)?.Trim().ToLowerInvariant();
        var answer = string.IsNullOrWhiteSpace(logical) ? null : logical;
        _recordTypes[typeRef] = answer;
        return answer;
    }

    private async Task<IReadOnlyList<Entity>> ReadOnePageAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = FiledPageSize, PageNumber = 1 };
        var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.MoreRecords)
        {
            throw new InvalidOperationException(
                $"More than {FiledPageSize} {query.EntityName} rows are filed under the records asked about; nothing is decided " +
                "on part of them.");
        }

        return result.Entities;
    }

    private static QueryExpression Query(string table, string[] columns) =>
        new(table) { ColumnSet = new ColumnSet(columns), NoLock = true };

    // ── Writes as the writers spell them ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A write's value for one filing column, normalized: a parent id, a clear, or (the pair) a text id / type.</summary>
    private sealed record FilingWrite(string Column, Guid? Id, string? Text, bool IsClear);

    private sealed class FilingValueException(string message) : Exception(message);

    /// <summary>The writes that touch what a row of <paramref name="table"/> is filed under.</summary>
    private static List<FilingWrite> FilingWritesIn(string table, IEnumerable<KeyValuePair<string, object?>> writes)
    {
        var filing = FilingColumnsOf(table);
        var found = new List<FilingWrite>();
        foreach (var (key, written) in writes)
        {
            var column = NormalizeColumn(key);
            if (!filing.Contains(column))
                continue;

            var value = written;
            if (value is System.Text.Json.JsonElement json)
            {
                // A JSON value (a payload read back from the Web API): a string is read like a string, null is a clear.
                value = json.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
                    System.Text.Json.JsonValueKind.String => json.GetString(),
                    _ => json,
                };
            }

            if (value is null or DBNull)
            {
                found.Add(new FilingWrite(column, null, null, IsClear: true));
                continue;
            }

            if (string.Equals(column, PairIdColumn, StringComparison.OrdinalIgnoreCase))
            {
                var text = value switch
                {
                    string s => s,
                    Guid g => g.ToString("D"),
                    _ => throw new FilingValueException($"the value written to {PairIdColumn} could not be interpreted"),
                };
                found.Add(string.IsNullOrWhiteSpace(text)
                    ? new FilingWrite(column, null, null, IsClear: true)
                    : new FilingWrite(column, null, text, IsClear: false));
                continue;
            }

            var id = value switch
            {
                EntityReference reference => reference.Id,
                Guid g => g,
                string s when TryParseBindOrGuid(s, out var parsed) => parsed,
                _ => throw new FilingValueException($"the value written to {column} could not be interpreted"),
            };
            found.Add(id == Guid.Empty
                ? new FilingWrite(column, null, null, IsClear: true)
                : new FilingWrite(column, id, null, IsClear: false));
        }

        return found;
    }

    /// <summary>The row as the write will leave it: <paramref name="before"/> (or an empty row for a create) with the writes.</summary>
    private static FilingFacts Overlay(string table, FilingFacts? before, IReadOnlyList<FilingWrite> writes)
    {
        var logical = table.Trim().ToLowerInvariant();
        var typed = new Dictionary<string, Guid?>(
            before?.Typed ?? TypedFilingColumns[logical].ToDictionary(t => t.Column, _ => (Guid?)null),
            StringComparer.OrdinalIgnoreCase);
        var pairId = before?.PairId;
        var pairType = before?.PairType;

        foreach (var write in writes)
        {
            if (string.Equals(write.Column, PairIdColumn, StringComparison.OrdinalIgnoreCase))
                pairId = write.IsClear ? null : write.Text;
            else if (string.Equals(write.Column, PairTypeColumn, StringComparison.OrdinalIgnoreCase))
                pairType = write.IsClear ? null : write.Id;
            else
                typed[write.Column] = write.IsClear ? null : write.Id;
        }

        return new FilingFacts(logical, before?.Id ?? Guid.Empty, before?.IsSecure, before?.OwningTeam, before?.ContainerId,
            typed, pairId, pairType);
    }

    /// <summary>
    /// A write key's logical column: <c>sprk_RegardingMatter@odata.bind</c> → <c>sprk_regardingmatter</c>,
    /// <c>_sprk_regardingmatter_value</c> → <c>sprk_regardingmatter</c>, any case → lower.
    /// </summary>
    internal static string NormalizeColumn(string key)
    {
        var name = (key ?? string.Empty).Trim();
        var at = name.IndexOf('@');
        if (at >= 0)
            name = name[..at];
        if (name.StartsWith('_') && name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
            name = name[1..^"_value".Length];
        return name.ToLowerInvariant();
    }

    /// <summary>A bind path (<c>/sprk_matters(…)</c> or <c>sprk_matters(…)</c>) or a bare GUID.</summary>
    private static bool TryParseBindOrGuid(string value, out Guid id)
    {
        var text = value.Trim();
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        if (open >= 0 && close > open)
            text = text[(open + 1)..close];
        return Guid.TryParse(text, out id);
    }

    private RecordOwnerResolution Refused(string table, string why)
    {
        _logger.LogWarning("[SECURE-INHERIT] Refusing a create / re-file of a {Table}: {Why}. Nothing was written.", table, why);
        return RecordOwnerResolution.Refused(
            RecordOwnerRefusal.ParentUndetermined,
            $"whether the matter or project this {(table == Project ? "project" : "work assignment")} would be filed under is " +
            $"secure could not be determined — {why} — so it was not written (a record filed under a secure record must be " +
            "secured itself)");
    }

    private static SecureRootInheritResult Result(
        string table, Guid id, SecureRootInheritOutcome outcome, string? code, string? detail,
        IReadOnlyList<SecureFilingParent>? parents = null) =>
        new(table, id, outcome, code, detail, parents ?? Array.Empty<SecureFilingParent>(), null);
}
