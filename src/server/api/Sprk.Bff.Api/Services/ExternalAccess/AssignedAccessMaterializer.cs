using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>Which trigger asked for a materialization (it decides only whether revoke-on-change may write).</summary>
public enum AssignedAccessTrigger
{
    /// <summary>The sync endpoint: the MDA form's post-save call, a client writer after its create, "Update Access".</summary>
    Sync,

    /// <summary>An L1 BFF writer, inline, after its own write committed.</summary>
    Inline,

    /// <summary>The scheduled reconciliation job (revoke-on-change gated by its switch — owner answer R3/(g)).</summary>
    Job,
}

/// <summary>One materialization: which root, who triggered it, and how.</summary>
/// <param name="RootType">The root's type.</param>
/// <param name="RootId">The root's id.</param>
/// <param name="Trigger">Which trigger.</param>
/// <param name="GrantorOid">The Entra oid of the person whose save triggered this, recorded as the grant's
/// <c>sprk_grantedby</c> so FR-33 reminders reach a person (owner answer A1); <c>null</c> for the job (reminders then
/// go to the record owner).</param>
/// <param name="RevokeOnChange">Whether an ended assignment's unmodified auto access may be REMOVED this pass. Always
/// true for the sync and L1 triggers (owner answer A4); the job's switch for the job (owner answer R3).</param>
/// <param name="CacheTenants">The tenant namespaces a shared user's impersonated root-set cache is cleared under — the
/// namespace that user's own reads write (task 143's rule; never "anonymous").</param>
public sealed record AssignedAccessRequest(
    ExternalGrantRootType RootType,
    Guid RootId,
    AssignedAccessTrigger Trigger,
    string? GrantorOid,
    bool RevokeOnChange,
    IReadOnlyCollection<string> CacheTenants);

/// <summary>Stable outcome statuses.</summary>
public static class AssignedAccessStatus
{
    /// <summary>Every subject was evaluated; see the entries (and failures).</summary>
    public const string Evaluated = "evaluated";

    /// <summary>No root has the id.</summary>
    public const string NotFound = "not-found";

    /// <summary>The root's access flags could not be read: nothing was written (ADR-003).</summary>
    public const string FlagsUnreadable = "flags-unreadable";

    /// <summary>The root or the ledger could not be read: nothing was written.</summary>
    public const string Failed = "failed";

    /// <summary>The bound registry names no access-conferring column for this root type: nothing to do.</summary>
    public const string NoRegistry = "no-registry";
}

/// <summary>What a pass did for one subject.</summary>
public static class AssignedAccessAction
{
    public const string None = "none";
    public const string Granted = "granted";
    public const string Raised = "raised";
    public const string Renewed = "renewed";
    public const string Shared = "shared";
    public const string Converted = "converted";
    public const string Restored = "restored";
    public const string Revoked = "revoked";
    public const string WouldRevoke = "would-revoke";
    public const string Declined = "declined";
    public const string Ledger = "ledger";
}

/// <summary>One subject's result.</summary>
public sealed record AssignedAccessEntryOutcome(
    string SubjectKind,
    Guid SubjectId,
    IReadOnlyList<string> SourceFields,
    Guid? SystemUserId,
    string State,
    string? Reason,
    string Action);

/// <summary>Something a pass could not do or could not confirm. Never reported as done.</summary>
public sealed record AssignedAccessFailure(string? SubjectKind, Guid? SubjectId, string Kind, string Message);

/// <summary>What one materialization did.</summary>
public sealed record AssignedAccessOutcome(
    string RecordType,
    Guid RecordId,
    string Status,
    IReadOnlyList<AssignedAccessEntryOutcome> Entries,
    IReadOnlyList<AssignedAccessFailure> Failures,
    int Writes)
{
    /// <summary>Evaluated with no failure. Anything else is not "clean".</summary>
    public bool Complete => Status == AssignedAccessStatus.Evaluated && Failures.Count == 0;
}

/// <summary>One ledger entry as Manage Access shows it (suggestions and provenance).</summary>
public sealed record AssignedAccessListEntry(
    Guid EntryId,
    string SourceField,
    string SourceFieldLabel,
    string SubjectKind,
    Guid SubjectId,
    string? SubjectName,
    Guid? SystemUserId,
    Guid? AccessRecordId,
    string State,
    string? Reason,
    IReadOnlyList<string> ResidualAccessTerms);

/// <summary>
/// The ONE invariant owner of the Assigned-To auto-grants (unified-access-control-r2 task 142 · owner round 2 item 5 +
/// Q5; round 3 A1/A2/R3; round 3 A3–A6/A8 accepted as recommended): every registry-listed "Assigned *" contact or
/// organization on a project, matter or work assignment holds Collaborate access — a removable grant on the record's
/// grant-access list, or a POA share when the contact is linked (task 141) to an eligible user (an enabled person —
/// <see cref="InternalShareEndpoints.ClassifyEligibility"/>, the rule <c>/share-user</c> applies) — unless an
/// operator declined it, a veto forbids it, or the record's policy says otherwise.
/// </summary>
/// <remarks>
/// <para><b>Three callers, one class</b> (DATAVERSE-WRITE-PATH-ARCHITECTURE §3, no plugins — D-1 / ADR-002): every BFF
/// writer of the columns calls it inline after its write commits (L1); the sync endpoint calls it for the MDA form's
/// post-save script, the client wizards after their creates and the "Update Access" ribbon command; and
/// <see cref="AssignedAccessReconciliationJob"/> sweeps every root every few minutes (L4).</para>
///
/// <para><b>The decisions</b> (owner answers, binding):
/// <list type="bullet">
/// <item>Level: Collaborate, uncapped (A1 / rule 5) — <see cref="GrantCeiling.AssignedToRule"/>; the share mask comes from
/// <see cref="RecordShareLevels"/>, never a new constant. An absent expiry is today + 90 through the core's
/// <c>DefaultExpiry</c>.</item>
/// <item>Restricted: no contact or organization grant; a linked user's share is unaffected (round 2 item 3) — unless the
/// user is flagged external (<c>sprk_isexternal = true</c>): then no share either, and one the Restricted remover took
/// away is recorded Skipped(restricted), never Declined, so it comes back when the record stops being Restricted (owner
/// round 67).
/// Secure or Limited: no organization grant. Secure: contact grants and shares are SUGGESTED, not written
/// (A3 = prompt: <see cref="AssignedAccessState.PendingConfirmation"/>); an auto grant that existed before the record
/// became secure is kept (A3). Limited: contact grants are written (A8).</item>
/// <item>No Access: a denied contact or organization gets nothing (the core's FR-23 check); a walled internal user on a
/// secure record gets no share (task 143's guard, reused) — on a work assignment or project filed under secure records,
/// walled by the record's own list or any secure parent's (task 158 r1c-v2, round 39 item 2). An unreadable list or flag
/// set writes nothing.</item>
/// <item>Never lower: an existing grant that CONFERS access today (not merely statecode 0 — an expired row confers
/// nothing) at Collaborate or above, or a share already carrying Collaborate's rights, is left untouched
/// (<see cref="AssignedAccessState.CoveredByExisting"/>) — not raised, not renewed. A lower conferring one is raised; the
/// rule renews it like its own while the assignment lasts (A5), and when the assignment ends puts back its earlier level
/// AND date, both recorded in the ledger reason (<see cref="AssignedAccessReason.RaisedFromLevel"/>). A covering grant
/// that LAPSES is a known cause (owner (e)), not an operator's removal: the still-assigned subject is given its access
/// again.</item>
/// <item>Operator removal sticks (item 5): /revoke, /unshare-user, Dismiss, and a removal outside the BFF all record
/// <see cref="AssignedAccessState.Declined"/>, which no trigger re-creates while the assignment persists. A KNOWN cause
/// is not a decline: a closed record, an inactive organization (R2), task 143's enforcer.</item>
/// <item>Changed or cleared (A4): the previous subject's UNMODIFIED auto access is removed — unless another registry
/// column still names it, it was adopted by a manual grant, or it was declined.</item>
/// <item>Renewal (A5): the materializer renews its own unmodified grant inside the FR-33 reminder window (and a lapsed
/// one) to today + 90.</item>
/// <item>Child-entity registry entries (event, invoice, to-do, analysis) create no root grant (A6): only the three
/// roots are ever materialized.</item>
/// <item>Standing and organization access STAY (A2 reversed); this adds alongside them.</item>
/// </list></para>
///
/// <para><b>Idempotent</b>: an unchanged root costs reads and ZERO writes — every ledger write is skipped when the row
/// already says it, and no grant or share is written over access that is already there.</para>
///
/// <para><b>Fails closed</b> (ADR-003 / WP-6): an unreadable root, ledger or flag set writes nothing; an unreadable link,
/// wall or deny list skips the subject; a failed write is a <see cref="AssignedAccessFailure"/>, never "done". A No Access
/// check that could not be completed — the deny-veto check's <see cref="NoAccessCheckAnswer.Unverifiable"/> (task 142 r4),
/// a throw (r3), or task 143's wall guard's <see cref="SecureShareWallOutcome.Unverifiable"/> on the share path (r4) — is
/// also a failure of its own kind (<see cref="DenyListUnreadableFailure"/>): never read as an entry or a policy hold, so
/// the run fails and the job reports it (owner round 13 items 4 and 5).</para>
/// </remarks>
public sealed class AssignedAccessMaterializer
{
    /// <summary>
    /// Renew inside this many days of expiry — the FR-33 reminder window (<see cref="GrantExpiryReminderJob.ReminderDays"/>),
    /// so a still-assigned auto grant is renewed before its first reminder would be sent.
    /// </summary>
    internal static int RenewalWindowDays => GrantExpiryReminderJob.ReminderDays.Max();

    /// <summary>
    /// The failure kind of a No Access check that could not be completed (an Unverifiable answer or a throw — task 142 r3,
    /// r4; the deny-veto check and task 143's wall guard alike): never an entry, never a policy hold. Counted by
    /// <see cref="AssignedAccessReconciliationJob"/> so monitoring sees a No Access read fault.
    /// </summary>
    internal const string DenyListUnreadableFailure = "deny-list-unreadable";

    private static readonly int CollaborateMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.CollaborateRights);

    private readonly AssignedAccessStore _store;
    private readonly DataverseWebApiClient _dataverse;
    private readonly ExternalParticipationService _participations;
    private readonly IAccessibleRecordSetService _accessibleRecords;
    private readonly IContactIdentityStore _identities;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly IDataverseRecordShareService _recordShare;

    /// <summary>
    /// Task 149 (the BINDING 142 x 149 merge-order obligation, task 149 note §14): a confirmed system-user share write on a
    /// secure root reaches that root's secure CHILDREN in the same run — never only at the next reconcile tick.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer _secureChildShares;
    private readonly ITenantCache _cache;
    private readonly ISubjectStandingGrantReader _standingGrants;
    private readonly IOptions<MembershipOptions> _membership;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AssignedAccessMaterializer> _logger;
    private readonly IServiceScopeFactory? _scopes;

    /// <param name="scopes">Task 158 r1c-v2 (main-session round 47 item 1 (3)): reaches the secure-root inheritance's
    /// sharee-only pass after an assignment ended on a work assignment or project — through a scope, because the inheritance
    /// depends on this class (round 47 item 1 (2)) and a constructor dependency back would be a cycle. Always provided by the
    /// host (an unconditional framework service); <c>null</c> only for a materializer built without a host (unit harnesses),
    /// which then leaves the parents' sharees to the secure-root inheritance job.</param>
    public AssignedAccessMaterializer(
        AssignedAccessStore store,
        DataverseWebApiClient dataverse,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        IContactIdentityStore identities,
        SecureShareNoAccessGuard noAccessGuard,
        IDataverseRecordShareService recordShare,
        Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer secureChildShares,
        ITenantCache cache,
        ISubjectStandingGrantReader standingGrants,
        IOptions<MembershipOptions> membership,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<AssignedAccessMaterializer> logger,
        IServiceScopeFactory? scopes = null)
    {
        _scopes = scopes;
        _store = store;
        _dataverse = dataverse;
        _participations = participations;
        _accessibleRecords = accessibleRecords;
        _identities = identities;
        _noAccessGuard = noAccessGuard;
        _recordShare = recordShare;
        // Both Scoped and unconditionally registered (ExternalAccessModule); no cycle — the synchronizer never depends on
        // the materializer (task 149 note §14, step 1).
        _secureChildShares = secureChildShares ?? throw new ArgumentNullException(nameof(secureChildShares));
        _cache = cache;
        _standingGrants = standingGrants;
        _membership = membership;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // =========================================================================================
    // The registry
    // =========================================================================================

    /// <summary>
    /// The access-conferring columns of one ROOT type, from the BOUND registry (<c>MembershipOptions.AccessConferringRoles</c>
    /// — never a second hardcoded list). Contact- and Organization-typed entries only; a malformed entry is ignored, never
    /// widened (ADR-034 A1). A non-root entity returns nothing: child entries confer no root grant (owner answer A6).
    /// </summary>
    internal IReadOnlyList<(string Field, AssignedSubjectKind Kind)> RegistryFor(ExternalGrantRootType rootType)
    {
        var logical = ExternalGrantRoot.LogicalNameFor(rootType);
        if (!_membership.Value.AccessConferringRoles.Entities.TryGetValue(logical, out var columns) || columns is null)
            return Array.Empty<(string, AssignedSubjectKind)>();

        var result = new List<(string, AssignedSubjectKind)>();
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column?.Field))
                continue;

            AssignedSubjectKind? kind =
                string.Equals(column.IdentityType, "Contact", StringComparison.OrdinalIgnoreCase) ? AssignedSubjectKind.Contact
                : string.Equals(column.IdentityType, "Organization", StringComparison.OrdinalIgnoreCase) ? AssignedSubjectKind.Organization
                : null;
            if (kind is null)
            {
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS] Registry entry {Entity}.{Field} declares identity type {IdentityType}; ignored.",
                    logical, column.Field, column.IdentityType);
                continue;
            }

            var field = column.Field.Trim().ToLowerInvariant();
            if (!result.Any(r => r.Item1 == field))
                result.Add((field, kind.Value));
        }

        return result;
    }

    /// <summary>Whether a write touching <paramref name="writtenColumns"/> on <paramref name="entityLogicalName"/> can change
    /// what this owner decides — a root whose registry column was written (<c>sprk_x</c> or <c>sprk_X@odata.bind</c>).</summary>
    public bool TouchesRegistry(string entityLogicalName, IEnumerable<string>? writtenColumns)
    {
        if (!TryParseRootLogicalName(entityLogicalName, out var rootType))
            return false;

        if (writtenColumns is null)
            return true; // a create, or a caller that cannot say: evaluate

        var fields = RegistryFor(rootType).Select(r => r.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return writtenColumns.Any(c =>
        {
            var name = c.Split('@')[0].Trim();
            return fields.Contains(name);
        });
    }

    /// <summary>A root LOGICAL name (<c>sprk_project</c>) → its type. The three roots only.</summary>
    public static bool TryParseRootLogicalName(string? logicalName, out ExternalGrantRootType rootType)
    {
        rootType = default;
        switch (logicalName?.Trim().ToLowerInvariant())
        {
            case "sprk_project":
                rootType = ExternalGrantRootType.Project;
                return true;
            case "sprk_matter":
                rootType = ExternalGrantRootType.Matter;
                return true;
            case "sprk_workassignment":
                rootType = ExternalGrantRootType.WorkAssignment;
                return true;
            default:
                return false;
        }
    }

    // =========================================================================================
    // L1 — after a BFF writer's own write committed
    // =========================================================================================

    /// <summary>
    /// The L1 trigger for a BFF writer that does not hold this service: when the write touched a root's registry column
    /// (or created a root), resolve the materializer in a fresh scope and run it. NEVER throws and never fails the
    /// writer's own create/update — a fault is logged and left to the reconciliation job (≤ 5 minutes).
    /// </summary>
    /// <param name="scopes">The writer's scope factory.</param>
    /// <param name="entityLogicalName">The written table.</param>
    /// <param name="recordId">The written record.</param>
    /// <param name="writtenColumns">The columns the write set; <c>null</c> for a create.</param>
    /// <param name="grantorOid">The person behind the write, when known.</param>
    /// <param name="logger">The writer's logger.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<AssignedAccessOutcome?> RunAfterWriteAsync(
        IServiceScopeFactory? scopes,
        string entityLogicalName,
        Guid recordId,
        IEnumerable<string>? writtenColumns,
        string? grantorOid,
        ILogger logger,
        CancellationToken ct)
    {
        if (scopes is null || recordId == Guid.Empty || !TryParseRootLogicalName(entityLogicalName, out _))
            return null;

        try
        {
            using var scope = scopes.CreateScope();
            var materializer = scope.ServiceProvider.GetService<AssignedAccessMaterializer>();
            if (materializer is null)
            {
                logger.LogWarning(
                    "[ASSIGNED-ACCESS] No materializer is registered in this host; {Entity} {RecordId} waits for the job.",
                    entityLogicalName, recordId);
                return null;
            }

            return await materializer.AfterWriteAsync(entityLogicalName, recordId, writtenColumns, grantorOid, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[ASSIGNED-ACCESS] Inline materialization after the write of {Entity} {RecordId} failed; the write stands " +
                "and the reconciliation job repairs access within minutes.", entityLogicalName, recordId);
            return null;
        }
    }

    /// <summary>
    /// The L1 trigger for a writer that holds this service. Never throws (see <see cref="RunAfterWriteAsync"/>).
    /// </summary>
    public async Task<AssignedAccessOutcome?> AfterWriteAsync(
        string entityLogicalName, Guid recordId, IEnumerable<string>? writtenColumns, string? grantorOid, CancellationToken ct)
    {
        if (!TryParseRootLogicalName(entityLogicalName, out var rootType) || recordId == Guid.Empty)
            return null;

        if (!TouchesRegistry(entityLogicalName, writtenColumns))
            return null;

        try
        {
            var outcome = await MaterializeAsync(
                new AssignedAccessRequest(rootType, recordId, AssignedAccessTrigger.Inline, grantorOid, RevokeOnChange: true,
                    CacheTenants: DeploymentTenants()),
                ct).ConfigureAwait(false);

            if (!outcome.Complete)
            {
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS] Inline materialization of {Entity} {RecordId} ended {Status} with {Failures} failure(s); " +
                    "the job retries.", entityLogicalName, recordId, outcome.Status, outcome.Failures.Count);
            }

            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[ASSIGNED-ACCESS] Inline materialization of {Entity} {RecordId} threw; the write stands and the job repairs it.",
                entityLogicalName, recordId);
            return null;
        }
    }

    /// <summary>
    /// The tenant a shared user's own reads write their root-set cache under when there is no caller to take it from —
    /// the deployment's (task 143's <see cref="ImpersonatedRootSetSource.DeploymentCacheTenant"/>), never "anonymous".
    /// </summary>
    public IReadOnlyCollection<string> DeploymentTenants()
    {
        var tenant = ImpersonatedRootSetSource.DeploymentCacheTenant(_configuration);
        return tenant is null ? Array.Empty<string>() : new[] { tenant };
    }

    // =========================================================================================
    // The materialization
    // =========================================================================================

    /// <summary>Materializes one root now. Never throws a read or write fault: the outcome carries it.</summary>
    public async Task<AssignedAccessOutcome> MaterializeAsync(AssignedAccessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var run = new Run(request, ExternalGrantLifecycle.TodayUtc(_timeProvider));

        var registry = RegistryFor(request.RootType);
        if (registry.Count == 0)
            return run.ToOutcome(AssignedAccessStatus.NoRegistry);

        // ── The root's registry columns ───────────────────────────────────────────────
        AssignedRootSnapshot? root;
        try
        {
            root = await _store.ReadRootAsync(request.RootType, request.RootId, registry.Select(r => r.Field).ToList(), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: the record could not be read; nothing was written.",
                run.Logical, request.RootId);
            run.Fail(null, "root-unreadable", "The record could not be read, so its assigned access was not changed. Try again.");
            return run.ToOutcome(AssignedAccessStatus.Failed);
        }

        if (root is null)
            return run.ToOutcome(AssignedAccessStatus.NotFound);

        // ── The ledger ────────────────────────────────────────────────────────────────
        IReadOnlyList<AssignedAccessLedgerRow> ledger;
        try
        {
            ledger = await _store.ReadLedgerAsync(request.RootType, request.RootId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Without the ledger a Declined subject could be re-created — the one thing the owner said must not happen.
            _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: the ledger could not be read; nothing was written.",
                run.Logical, request.RootId);
            run.Fail(null, "ledger-unreadable",
                "The record's assigned-access history could not be read, so nothing was changed. Try again.");
            return run.ToOutcome(AssignedAccessStatus.Failed);
        }

        var desired = Desired(registry, root);

        // ── The record's policy flags (ADR-003: unreadable → no write) ──────────────────
        var flags = await ReadFlagsAsync(run, ct).ConfigureAwait(false);
        if (flags is null)
        {
            foreach (var (subject, fields) in desired)
            {
                run.Entry(subject, fields, null, AssignedAccessState.Skipped, AssignedAccessReason.FlagsUnreadable,
                    AssignedAccessAction.None);
            }

            return run.ToOutcome(AssignedAccessStatus.FlagsUnreadable);
        }

        var rowsBySubject = ledger
            .Where(r => r.Subject is not null && !string.IsNullOrWhiteSpace(r.SourceField))
            .GroupBy(r => r.Subject!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AssignedAccessLedgerRow>)g.ToList());

        // ── Each assigned subject ─────────────────────────────────────────────────────
        foreach (var (subject, fields) in desired)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DecideSubjectAsync(
                    run, subject, fields,
                    rowsBySubject.TryGetValue(subject, out var rows) ? rows : Array.Empty<AssignedAccessLedgerRow>(),
                    flags.Value, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: subject {Subject} could not be materialized.",
                    run.Logical, request.RootId, subject);
                run.Fail(subject, "subject-failed",
                    $"Access for {subject} on this record could not be updated: {ex.Message}");
            }
        }

        // ── Assignments that ended (field cleared or changed — owner answer A4) ────────
        var assignmentEnded = false;
        foreach (var row in ledger)
        {
            ct.ThrowIfCancellationRequested();
            if (row.State == AssignedAccessState.Revoked || row.Subject is not { } subject || string.IsNullOrWhiteSpace(row.SourceField))
                continue;

            var stillAssignedHere = desired.TryGetValue(subject, out var fields)
                                    && fields.Contains(row.SourceField!.Trim().ToLowerInvariant());
            if (stillAssignedHere)
                continue;

            assignmentEnded = true;
            try
            {
                await EndAssignmentAsync(run, row, subject, desired.ContainsKey(subject), flags.Value, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: ended assignment {Field}/{Subject} could not be settled.",
                    run.Logical, request.RootId, row.SourceField, subject);
                run.Fail(subject, "end-failed",
                    $"The ended assignment of {subject} ({row.SourceField}) could not be settled: {ex.Message}");
            }
        }

        // ── Task 149: a confirmed root share write reaches the secure children now (once per root, not per subject) ──
        if (run.RootShareWritten)
        {
            await SyncChildrenAsync(run, ct).ConfigureAwait(false);
        }

        // Task 158 r1c-v2 (main-session round 47 item 1 (3), E-158-v1-1's reverse direction): an assignment that ended on a
        // work assignment or project may have taken away (or recorded as covered) access its secure parents pass on — the
        // secure-root inheritance's SHAREE-ONLY pass gives the record what its secure parents pass on at once (never a
        // provisioning; nothing for a record filed under no secure parent), rather than at that job's next run.
        if (assignmentEnded && SecureRootInheritance.Inherits(run.Logical))
            await PassParentShareesOnAsync(run, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[ASSIGNED-ACCESS] {Trigger} {Type} {RootId}: {Subjects} assigned subject(s), {Writes} write(s), {Failures} failure(s).",
            request.Trigger, run.Logical, request.RootId, desired.Count, run.Writes, run.Failures.Count);
        return run.ToOutcome(AssignedAccessStatus.Evaluated);
    }

    /// <summary>
    /// Task 149 (the BINDING 142 x 149 merge-order obligation, task 149 note §14 steps 2-3): after this run CONFIRMED at
    /// least one system-user share write on the root (a grant or widening read back, or a removal / restore read back),
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer.SyncRootAsync"/> mirrors the root's share set
    /// onto its secure children — once for the root. On an ordinary root it answers "not applicable" after reading the
    /// root's own row. The root write STANDS whatever this answers (never rolled back); a fan-out that is not complete, or
    /// that threw, is a run failure (<c>children-incomplete</c>), so the job reports <c>Success = false</c> and the
    /// two-minute reconcile completes the children — the shape of <c>NoAccessShareEnforcer.SyncChildrenAsync</c>.
    /// </summary>
    private async Task SyncChildrenAsync(Run run, CancellationToken ct)
    {
        Sprk.Bff.Api.Services.Access.SecureChildShareSyncResult children;
        try
        {
            children = await _secureChildShares.SyncRootAsync(run.Logical, run.RootId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: the secure children could not be updated after the share write.",
                run.Logical, run.RootId);
            children = Sprk.Bff.Api.Services.Access.SecureChildShareSyncResult.Failed("the children could not be updated");
        }

        if (children.IsComplete)
        {
            return;
        }

        _logger.LogWarning(
            "[ASSIGNED-ACCESS] {Type} {RootId}: the share write stands, but its secure children are {Status} " +
            "({NotUpdated} not updated, {Held} held of {InScope}; {Detail}).",
            run.Logical, run.RootId, children.Status, children.ChildrenNotUpdated, children.ChildrenHeld,
            children.ChildrenInScope, children.Detail);
        run.Fail(null, "children-incomplete",
            $"This record's access was updated, but {children.ChildrenLeftOutOfLine} of its {children.ChildrenInScope} related " +
            "records (documents, events, to-dos, communications) could not be updated yet " +
            $"({children.Status}). The scheduled safety net finishes it within a few minutes.");
    }

    /// <summary>
    /// Round 47 item 1 (3): the secure-root inheritance's sharee-only pass for this record, resolved in its own scope (the
    /// inheritance depends on this class, so it cannot be a constructor dependency). What it cannot give is the secure-root
    /// inheritance job's to give (≤ 5 minutes) and is logged — never a failure of THIS rule's run.
    /// </summary>
    private async Task PassParentShareesOnAsync(Run run, CancellationToken ct)
    {
        if (_scopes is null)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] {Type} {RootId}: an assignment ended; no host scope to give its secure parents' sharees now — " +
                "the secure-root inheritance job gives them.", run.Logical, run.RootId);
            return;
        }

        using var scope = _scopes.CreateScope();
        var pass = await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .PassShareesToFiledRecordAsync(run.Logical, run.RootId, $"assigned-access:{run.RootId:N}", ct).ConfigureAwait(false);
        if (!pass.IsComplete)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] {Type} {RootId}: an assignment ended, and its secure parents' sharees were not all given to it " +
                "({Outcome}, {Code}); the secure-root inheritance job completes it.", run.Logical, run.RootId, pass.WireOutcome,
                pass.ReasonCode);
        }
    }

    /// <summary>The assigned subjects (registry order) and the fields that name each.</summary>
    private static Dictionary<AssignedSubject, List<string>> Desired(
        IReadOnlyList<(string Field, AssignedSubjectKind Kind)> registry, AssignedRootSnapshot root)
    {
        var desired = new Dictionary<AssignedSubject, List<string>>();
        foreach (var (field, kind) in registry)
        {
            if (!root.Values.TryGetValue(field, out var id) || id is not { } subjectId || subjectId == Guid.Empty)
                continue;

            var subject = new AssignedSubject(kind, subjectId);
            if (!desired.TryGetValue(subject, out var fields))
                desired[subject] = fields = new List<string>();
            fields.Add(field);
        }

        return desired;
    }

    private async Task<RootRecordFlags?> ReadFlagsAsync(Run run, CancellationToken ct)
    {
        try
        {
            var flags = await _participations
                .GetRootRecordFlagsAsync(run.Logical, new[] { run.RootId }, ct).ConfigureAwait(false);
            // Absent = unreadable at write time (task 138's rule), never "no veto".
            return flags.TryGetValue(run.RootId, out var f) && !f.IsUnreadable ? f : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[ASSIGNED-ACCESS] {Type} {RootId}: the access flags could not be read; nothing was written.",
                run.Logical, run.RootId);
            return null;
        }
    }

    // =========================================================================================
    // One subject
    // =========================================================================================

    private async Task DecideSubjectAsync(
        Run run, AssignedSubject subject, IReadOnlyList<string> fields, IReadOnlyList<AssignedAccessLedgerRow> rows,
        RootRecordFlags flags, CancellationToken ct)
    {
        var byField = RowsByField(fields, rows);
        var live = rows.Where(r => r.State != AssignedAccessState.Revoked).ToList();

        // (1) Operator removal sticks while the subject stays assigned (owner item 5). Not a No Access entry: a manual
        //     grant still succeeds and turns it Adopted.
        if (live.FirstOrDefault(r => r.State == AssignedAccessState.Declined) is { } declined)
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Declined, declined.Reason ?? AssignedAccessReason.RemovedByOperator,
                    declined.GrantId, declined.SystemUserId), ct).ConfigureAwait(false);
            run.Entry(subject, fields, declined.SystemUserId, AssignedAccessState.Declined, declined.Reason, AssignedAccessAction.None);
            return;
        }

        // (2) A manual grant/share landed on it: the rule never touches it again.
        if (live.FirstOrDefault(r => r.State == AssignedAccessState.Adopted) is { } adopted)
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Adopted, adopted.Reason ?? AssignedAccessReason.ManualGrant,
                    adopted.GrantId, adopted.SystemUserId, adopted.GrantedLevel, adopted.GrantedExpiry), ct).ConfigureAwait(false);
            run.Entry(subject, fields, adopted.SystemUserId, AssignedAccessState.Adopted, adopted.Reason, AssignedAccessAction.None);
            return;
        }

        var target = await ResolveTargetAsync(subject, flags, ct).ConfigureAwait(false);

        // (3) Access this owner created: verify it, convert it (141 link), renew it.
        if (live.FirstOrDefault(r => r.State is AssignedAccessState.Granted or AssignedAccessState.Shared) is { } ours)
        {
            if (await ContinueOursAsync(run, subject, fields, byField, ours, target, flags, ct).ConfigureAwait(false))
                return;
        }

        // (4) Access someone else gave that covered the assignment: is it still there?
        if (live.FirstOrDefault(r => r.State == AssignedAccessState.CoveredByExisting) is { } covered)
        {
            if (await ContinueCoveredAsync(run, subject, fields, byField, covered, target, flags, ct).ConfigureAwait(false))
                return;
        }

        // (5) Fresh: decide and write.
        await FreshAsync(run, subject, fields, byField, live, target, flags, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The owner's own access. Returns <c>false</c> only when the subject must be re-evaluated fresh.
    /// </summary>
    private async Task<bool> ContinueOursAsync(
        Run run, AssignedSubject subject, IReadOnlyList<string> fields, Dictionary<string, AssignedAccessLedgerRow?> byField,
        AssignedAccessLedgerRow ours, Target target, RootRecordFlags flags, CancellationToken ct)
    {
        if (ours.State == AssignedAccessState.Granted)
        {
            var key = GrantKeyFor(run, subject);
            var active = await ExternalGrantLifecycle.QueryActiveRowsAsync(_dataverse, key, ct).ConfigureAwait(false);
            var row = active.FirstOrDefault(r => r.Id == ours.GrantId)
                      ?? (active.Count > 0 ? ExternalGrantLifecycle.ElectSurvivor(active, run.Today) : null);

            if (row is null)
            {
                // Gone. A KNOWN cause is not an operator's decision: a closed record (closure deactivates every grant),
                // an inactive organization (task 117 R2). Anything else was removed outside the BFF — it sticks.
                if (KnownGrantRemovalCause(target, flags) is { } cause)
                {
                    await EnsureRowsAsync(run, subject, byField,
                        new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, cause, ours.GrantId), ct).ConfigureAwait(false);
                    run.Entry(subject, fields, null, AssignedAccessState.Skipped, cause, AssignedAccessAction.Ledger);
                    return true;
                }

                await EnsureRowsAsync(run, subject, byField,
                    new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand, ours.GrantId),
                    ct).ConfigureAwait(false);
                run.Entry(subject, fields, null, AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand,
                    AssignedAccessAction.Declined);
                return true;
            }

            var modified = IsModified(ours, row);

            // ── Task 141 link appeared: the contact is an internal user — convert grant → share ──
            // GitHub #1410 / owner round 82 ("the parent permissions control"): the record is not flagged secure, but it may be
            // filed under a secure matter or project whose No Access list governs it. A walled user is NOT converted (the
            // grant is left exactly as it is, and converts once the wall lifts); a check that cannot be completed is a fault
            // (reported, the run red) and nothing is converted this pass.
            var convertWall = target.Kind == TargetKind.Share && !modified && !flags.IsSecure && !flags.IsInactive
                ? await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                    run.Logical, run.RootId, target.SystemUserId!.Value, SecureWallRecordScope.AsFlagged, ct).ConfigureAwait(false)
                : null;
            if (convertWall?.Outcome == SecureShareWallOutcome.Unverifiable)
            {
                DenyListFault(run, subject, "the conversion of its grant to a share", detail: convertWall.Fault);
            }

            if (convertWall is not null && !convertWall.RefusesShare)
            {
                var written = await WriteShareAsync(run, subject, target.SystemUserId!.Value, ct).ConfigureAwait(false);
                if (written is not { } mask)
                    return true; // failure recorded; the grant stays

                await ExternalGrantLifecycle.DeactivateAsync(_dataverse, active.Select(r => r.Id), _logger, ct).ConfigureAwait(false);
                run.Writes += active.Count;
                await InvalidateGrantSetAsync(subject).ConfigureAwait(false);

                await EnsureRowsAsync(run, subject, byField,
                    new AssignedAccessLedgerWrite(AssignedAccessState.Shared, AssignedAccessReason.ConvertedFromGrant,
                        row.Id, target.SystemUserId, mask), ct).ConfigureAwait(false);
                run.Entry(subject, fields, target.SystemUserId, AssignedAccessState.Shared, AssignedAccessReason.ConvertedFromGrant,
                    AssignedAccessAction.Converted);
                return true;
            }

            // ── Owner answer A5: renew a still-assigned, unmodified auto grant inside the reminder window ──
            if (!modified && !flags.IsRestricted && !flags.IsInactive
                && row.ExpiresDate is { } expiry && expiry <= run.Today.AddDays(RenewalWindowDays)
                && row.AccessLevel is { } currentLevel && Enum.IsDefined(typeof(ExternalAccessLevel), currentLevel))
            {
                var renewed = ExternalGrantLifecycle.DefaultExpiry(run.Today);
                var outcome = await WriteGrantAsync(run, subject, (ExternalAccessLevel)currentLevel, renewed, ct).ConfigureAwait(false);
                if (outcome.Refusal is null)
                {
                    run.Writes++;
                    await EnsureRowsAsync(run, subject, byField,
                        new AssignedAccessLedgerWrite(AssignedAccessState.Granted, ours.Reason, row.Id, null, currentLevel, renewed),
                        ct).ConfigureAwait(false);
                    run.Entry(subject, fields, null, AssignedAccessState.Granted, ours.Reason, AssignedAccessAction.Renewed);
                    return true;
                }

                if (outcome.Refusal.IsDenyListReadFault)
                {
                    // Task 142 r3/r4: the No Access check could not be completed — a fault, reported; nothing is renewed this
                    // pass and the next one tries again (the grant is kept as it is below).
                    DenyListFault(run, subject, "its renewal");
                }
                else
                {
                    _logger.LogWarning(
                        "[ASSIGNED-ACCESS] Renewal of {Subject} on {Type} {RootId} was refused ({Reason}); the grant is left to lapse.",
                        subject, run.Logical, run.RootId, outcome.Refusal.ReasonCode);
                }
            }

            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Granted, ours.Reason, row.Id, null,
                    ours.GrantedLevel ?? row.AccessLevel, ours.GrantedExpiry ?? row.ExpiresDate), ct).ConfigureAwait(false);
            run.Entry(subject, fields, null, AssignedAccessState.Granted, modified ? AssignedAccessReason.KeptModified : ours.Reason,
                AssignedAccessAction.None);
            return true;
        }

        // ── Shared ──
        if (ours.SystemUserId is not { } user || user == Guid.Empty)
            return false; // a malformed row: decide fresh

        var current = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
        if (current == 0 && target.Kind == TargetKind.Skip && target.SkipReason == AssignedAccessReason.Restricted
            && target.SystemUserId == user)
        {
            // Owner round 67: the record became Restricted and this user is flagged external, so the Restricted remover
            // (RestrictedExternalShareRemover) took the share away — a KNOWN cause, never an operator's removal (which
            // would stick as Declined). Recorded as Restricted, so the share is given back once the record is not.
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, AssignedAccessReason.Restricted, null, user,
                    ours.GrantedLevel), ct).ConfigureAwait(false);
            run.Entry(subject, fields, user, AssignedAccessState.Skipped, AssignedAccessReason.Restricted,
                AssignedAccessAction.Ledger);
            return true;
        }

        if (current == 0)
        {
            // Task 143's enforcer removes a WALLED user's share on a secure record: a known cause, restored once the wall
            // is lifted (criterion 9). Any other removal was an operator's (the OOB MDA Share dialog) — it sticks. Task 158
            // final round (main-session round 58 item 1): on a work assignment or project filed under a secure record the
            // enforcer also removes it for a person on that PARENT's list, so the parents' lists are asked too — the guard's
            // one entry point, as the suggestion below asks it. GitHub #1410 / owner round 82: asked WHATEVER the record's flag
            // — the enforcer also removes it on a not-yet-secure record filed under a walled secure parent (the guard answers
            // NotSecure for a record with no secure parent, which falls through to the operator's removal as before).
            {
                var wall = await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                    run.Logical, run.RootId, user, SecureWallRecordScope.AsFlagged, ct).ConfigureAwait(false);
                if (wall.Outcome == SecureShareWallOutcome.Walled)
                {
                    await EnsureRowsAsync(run, subject, byField,
                        new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, AssignedAccessReason.RemovedByNoAccess,
                            null, user, ours.GrantedLevel), ct).ConfigureAwait(false);
                    run.Entry(subject, fields, user, AssignedAccessState.Skipped, AssignedAccessReason.RemovedByNoAccess,
                        AssignedAccessAction.Ledger);
                    return true;
                }

                if (wall.Outcome == SecureShareWallOutcome.Unverifiable)
                {
                    // Cannot tell why it went (task 143's enforcer, or an operator): decide nothing this pass. Task 142 r4
                    // (owner round 13 item 5): the wall check's Unverifiable FAILS THE RUN like the deny-list fault —
                    // counted, logged, the job red — never a quiet "nothing decided".
                    DenyListFault(run, subject, "the decision on its removed share", detail: wall.Fault);
                    run.Entry(subject, fields, user, AssignedAccessState.Shared, AssignedAccessReason.NoAccessUnverifiable,
                        AssignedAccessAction.None);
                    return true;
                }
            }

            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand, null, user),
                ct).ConfigureAwait(false);
            run.Entry(subject, fields, user, AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand,
                AssignedAccessAction.Declined);
            return true;
        }

        await EnsureRowsAsync(run, subject, byField,
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, ours.Reason, null, user, ours.GrantedLevel), ct)
            .ConfigureAwait(false);
        run.Entry(subject, fields, user, AssignedAccessState.Shared,
            ours.GrantedLevel is { } level && level != current ? AssignedAccessReason.KeptModified : ours.Reason,
            AssignedAccessAction.None);
        return true;
    }

    /// <summary>Access someone else gave. Returns <c>false</c> when the subject must be re-evaluated fresh.</summary>
    private async Task<bool> ContinueCoveredAsync(
        Run run, AssignedSubject subject, IReadOnlyList<string> fields, Dictionary<string, AssignedAccessLedgerRow?> byField,
        AssignedAccessLedgerRow covered, Target target, RootRecordFlags flags, CancellationToken ct)
    {
        bool stillCovered;
        var lapsed = false;
        if (covered.SystemUserId is { } user && user != Guid.Empty && target.Kind == TargetKind.Share)
        {
            stillCovered = Covers(await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false));
        }
        else if (target.Kind is TargetKind.ContactGrant or TargetKind.OrganizationGrant)
        {
            var active = await ExternalGrantLifecycle.QueryActiveRowsAsync(_dataverse, GrantKeyFor(run, subject), ct)
                .ConfigureAwait(false);
            var atCollaborate = active.Where(r => (r.AccessLevel ?? 0) >= (int)ExternalAccessLevel.Collaborate).ToList();

            // Covered only while the covering row CONFERS access (task 142 r1, finding 1): an expired grant stays at
            // statecode 0, and counting it left a still-assigned subject with nothing, permanently.
            stillCovered = Conferring(atCollaborate, run.Today).Count > 0;

            // The covering grant is still there but no longer confers (it lapsed): a KNOWN cause — expiry, owner (e) /
            // A5 — never an operator's removal. Decided fresh below, which gives the still-assigned subject its access.
            lapsed = !stillCovered && atCollaborate.Count > 0;
        }
        else
        {
            return false; // the subject's shape changed (link, eligibility): decide fresh
        }

        if (stillCovered)
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.CoveredByExisting, covered.Reason, covered.GrantId,
                    covered.SystemUserId), ct).ConfigureAwait(false);
            run.Entry(subject, fields, covered.SystemUserId, AssignedAccessState.CoveredByExisting, covered.Reason,
                AssignedAccessAction.None);
            return true;
        }

        // The covering access is gone. A known cause → decide fresh (which skips for the same cause, or renews a lapsed
        // one); otherwise someone removed access from an assigned subject on purpose — respect it, as for the owner's own
        // grant.
        if (lapsed || KnownGrantRemovalCause(target, flags) is not null)
            return false;

        await EnsureRowsAsync(run, subject, byField,
            new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand,
                covered.GrantId, covered.SystemUserId), ct).ConfigureAwait(false);
        run.Entry(subject, fields, covered.SystemUserId, AssignedAccessState.Declined, AssignedAccessReason.RemovedOutOfBand,
            AssignedAccessAction.Declined);
        return true;
    }

    /// <summary>A subject with no standing decision: apply the policy and write.</summary>
    private async Task FreshAsync(
        Run run, AssignedSubject subject, IReadOnlyList<string> fields, Dictionary<string, AssignedAccessLedgerRow?> byField,
        IReadOnlyList<AssignedAccessLedgerRow> live, Target target, RootRecordFlags flags, CancellationToken ct)
    {
        async Task SkipAsync(string reason, Guid? user = null)
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.Skipped, reason, null, user), ct).ConfigureAwait(false);
            run.Entry(subject, fields, user, AssignedAccessState.Skipped, reason, AssignedAccessAction.None);
        }

        if (target.Kind == TargetKind.Skip)
        {
            await SkipAsync(target.SkipReason!, target.SystemUserId).ConfigureAwait(false);
            return;
        }

        if (flags.IsInactive)
        {
            await SkipAsync(AssignedAccessReason.RootInactive, target.SystemUserId).ConfigureAwait(false);
            return;
        }

        if (target.Kind == TargetKind.Share)
        {
            await FreshShareAsync(run, subject, fields, byField, live, target.SystemUserId!.Value, flags, SkipAsync, ct)
                .ConfigureAwait(false);
            return;
        }

        // ── A grant (contact or organization) ──
        if (flags.IsRestricted)
        {
            await SkipAsync(AssignedAccessReason.Restricted).ConfigureAwait(false);
            return;
        }

        if (target.Kind == TargetKind.OrganizationGrant && flags.IsDirectOnly)
        {
            await SkipAsync(flags.IsSecure ? AssignedAccessReason.OrganizationOnSecure : AssignedAccessReason.OrganizationOnLimited)
                .ConfigureAwait(false);
            return;
        }

        var active = await ExternalGrantLifecycle.QueryActiveRowsAsync(_dataverse, GrantKeyFor(run, subject), ct)
            .ConfigureAwait(false);

        // Only a row that CONFERS access today counts (task 142 r1, finding 1). An expired row stays at statecode 0, so
        // "active" alone let a lapsed grant cover the assignment and left the subject with nothing — permanently, since
        // renewal applies only to the rule's own grants. Conferral is the read filter's own predicate.
        var conferring = Conferring(active, run.Today);

        // Never lower: a CONFERRING row at Collaborate or above covers, and the rule leaves it exactly as it is — level and
        // date (CoveredByExisting is never renewed; its date is its owner's). A LOWER conferring row is different: it is
        // raised below, renewed like the rule's own while the assignment lasts (A5), and put back — level AND date — when
        // the assignment ends (task 142 r2, finding 1).
        if (conferring.FirstOrDefault(r => (r.AccessLevel ?? 0) >= (int)ExternalAccessLevel.Collaborate) is { } covering)
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.CoveredByExisting, null, covering.Id), ct).ConfigureAwait(false);
            run.Entry(subject, fields, null, AssignedAccessState.CoveredByExisting, null, AssignedAccessAction.None);
            return;
        }

        // Owner answer A3: on a SECURE record a contact is suggested, not granted — after the deny check, so a walled
        // contact is never suggested.
        if (flags.IsSecure)
        {
            NoAccessCheckAnswer noAccess;
            try
            {
                noAccess = await _accessibleRecords.CheckGranteeNoAccessAsync(
                    run.Logical, run.RootId, subject.Id, Array.Empty<Guid>(), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Task 142 r3: a check that THREW is a fault, not an entry — not suggested (fail closed), and reported.
                DenyListFault(run, subject, "its suggestion", ex);
                await SkipAsync(AssignedAccessReason.NoAccessUnverifiable).ConfigureAwait(false);
                return;
            }

            if (noAccess == NoAccessCheckAnswer.Denied)
            {
                await SkipAsync(AssignedAccessReason.NoAccess).ConfigureAwait(false);
                return;
            }

            if (noAccess != NoAccessCheckAnswer.Allowed)
            {
                // Task 142 r4 (owner round 13 item 4): the check could not be completed — Unverifiable (or an answer this
                // code does not know). A fault, not an entry: not suggested (fail closed), and reported.
                DenyListFault(run, subject, "its suggestion", detail: noAccess.ToString());
                await SkipAsync(AssignedAccessReason.NoAccessUnverifiable).ConfigureAwait(false);
                return;
            }

            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.PendingConfirmation, null), ct).ConfigureAwait(false);
            run.Entry(subject, fields, null, AssignedAccessState.PendingConfirmation, null, AssignedAccessAction.Ledger);
            return;
        }

        // A lower CONFERRING grant is raised (and put back when the assignment ends). Otherwise the subject has no access
        // here and the rule gives it — a new row, or, when the key's rows all confer nothing (lapsed, or never bounded),
        // the core's elected row at Collaborate with an EXPLICIT today + 90 (task 142 r1, finding 2). Without that date the
        // core keeps the lapsed one and answers expired_not_restored, and the rule would report "granted" over a grant
        // that confers nothing. The date is the same DefaultExpiry renewal writes (owner A5); no conferring row exists
        // whose date it could shorten. A lapsed row's level is not a "prior" to put back: it conferred nothing, so the end
        // of the assignment leaves the subject where it started — with no access here.
        //
        // RECORDED DEVIATION (task 142 r2, finding 3; notes §6): when that lapsed row's stored level is ABOVE Collaborate
        // (an expired Full Access grant), the core writes Collaborate over it — the rule's ceiling (A1) cannot write Full,
        // and the core's never-lower refusal guards only a NARROWED request. The row conferred nothing, so no ACCESS is
        // lowered (none → Collaborate); refusing would leave an assigned subject with nothing (rule 5), and renewing it at
        // Full would grant above the rule's level for 90 days. The expired Full level is not restored at the end.
        //
        // A raise records what the subject held, to put back when the assignment ends — the highest conferring level AND
        // the latest conferring date (task 142 r2, finding 1: renewal extends a raised grant while it is assigned, so the
        // level alone would leave the operator's grant outliving the date the operator chose). Every conferring row here is
        // below Collaborate (one at or above it covered, above) and carries a date (ConfersAccessOn: null confers nothing).
        var prior = conferring.Count > 0 ? conferring.Max(r => r.AccessLevel ?? 0) : (int?)null;
        var priorExpiry = conferring.Count > 0 ? conferring.Max(r => r.ExpiresDate) : null;
        DateOnly? expiry = active.Count > 0 && conferring.Count == 0 ? ExternalGrantLifecycle.DefaultExpiry(run.Today) : null;
        var outcome = await WriteGrantAsync(run, subject, ExternalAccessLevel.Collaborate, expiry, ct).ConfigureAwait(false);
        if (outcome.Refusal is { } refusal)
        {
            if (refusal.IsDenyListReadFault)
            {
                // Task 142 r3/r4: the core's No Access check could not be completed (Unverifiable, or it threw). Not
                // "no-access" (an entry): a fault — nothing granted (fail closed), reported, and decided again next pass
                // (Skipped is never sticky).
                DenyListFault(run, subject, "its access");
                await SkipAsync(AssignedAccessReason.NoAccessUnverifiable).ConfigureAwait(false);
                return;
            }

            await SkipAsync(refusal.ReasonCode == ExternalGrantLifecycle.GranteeDeniedReasonCode
                    ? AssignedAccessReason.NoAccess
                    : AssignedAccessReason.GrantRefusedPrefix + refusal.ReasonCode)
                .ConfigureAwait(false);
            return;
        }

        if (outcome.Warning is { } warning)
        {
            // ADR-003: the grant does not confer access (a row lapsed between this read and the core's). This call sends
            // no date then (a date is sent only when nothing conferred), so since task 113 the core refused before
            // writing: nothing changed. Never reported as Granted: a failure, and nothing in the ledger changes, so the
            // next pass decides again from fresh reads (and, seeing nothing conferring, sends a date).
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] {Type} {RootId}: the grant for {Subject} was not written — the existing grant has lapsed " +
                "and confers no access ({Warning}); not recorded as granted.", run.Logical, run.RootId, subject, warning);
            run.Fail(subject, "grant-not-conferring",
                $"Access for {subject} on this record was not put in place yet: their existing grant has lapsed. The next update will try again.");
            return;
        }

        run.Writes++;

        var survivorExpiry = expiry
            ?? (active.Count > 0
                ? ExternalGrantLifecycle.ElectSurvivor(active, run.Today).ExpiresDate ?? ExternalGrantLifecycle.DefaultExpiry(run.Today)
                : ExternalGrantLifecycle.DefaultExpiry(run.Today));
        var reason = prior is { } p && priorExpiry is { } d ? AssignedAccessReason.RaisedFromLevel(p, d) : null;

        await EnsureRowsAsync(run, subject, byField,
            new AssignedAccessLedgerWrite(AssignedAccessState.Granted, reason, outcome.AccessRecordId, null,
                (int)ExternalAccessLevel.Collaborate, survivorExpiry), ct).ConfigureAwait(false);
        run.Entry(subject, fields, null, AssignedAccessState.Granted, reason,
            prior is null ? AssignedAccessAction.Granted : AssignedAccessAction.Raised);
    }

    private async Task FreshShareAsync(
        Run run, AssignedSubject subject, IReadOnlyList<string> fields, Dictionary<string, AssignedAccessLedgerRow?> byField,
        IReadOnlyList<AssignedAccessLedgerRow> live, Guid user, RootRecordFlags flags,
        Func<string, Guid?, Task> skipAsync, CancellationToken ct)
    {
        var current = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
        if (Covers(current))
        {
            await EnsureRowsAsync(run, subject, byField,
                new AssignedAccessLedgerWrite(AssignedAccessState.CoveredByExisting, null, null, user), ct).ConfigureAwait(false);
            run.Entry(subject, fields, user, AssignedAccessState.CoveredByExisting, null, AssignedAccessAction.None);
            return;
        }

        var restoring = live.Any(r => r.State == AssignedAccessState.Skipped && r.Reason == AssignedAccessReason.RemovedByNoAccess);

        {
            // Task 143's ONE write-time check, reused (never a second copy). Task 158 r1c-v2 (round 39 item 2): on a work
            // assignment or project filed under secure records, the share (or the suggestion of one) honours every secure
            // parent's No Access list too — the guard's one entry point for the record and its parents. GitHub #1410 / owner
            // round 82: asked WHATEVER the record's flag (a not-yet-secure record filed under a walled secure parent is
            // governed by that parent's list); the guard answers NotSecure for a record with no secure parent.
            var wall = await _noAccessGuard.CheckRecordAndSecureParentsAsync(
                run.Logical, run.RootId, user, SecureWallRecordScope.AsFlagged, ct).ConfigureAwait(false);
            if (wall.Outcome == SecureShareWallOutcome.Walled)
            {
                await skipAsync(restoring ? AssignedAccessReason.RemovedByNoAccess : AssignedAccessReason.NoAccess, user)
                    .ConfigureAwait(false);
                return;
            }

            if (wall.Outcome == SecureShareWallOutcome.Unverifiable)
            {
                // Task 142 r4 (owner round 13 item 5): a fault, not a wall — nothing shared or suggested (fail closed), and
                // the run FAILS like the deny-list fault (counted, logged, the job red). The ledger keeps what it said, so a
                // share 143's enforcer removed is still restored once the check reads again.
                DenyListFault(run, subject, restoring || !flags.IsSecure ? "its share" : "its suggestion", detail: wall.Fault);
                await skipAsync(restoring ? AssignedAccessReason.RemovedByNoAccess : AssignedAccessReason.NoAccessUnverifiable, user)
                    .ConfigureAwait(false);
                return;
            }

            if (flags.IsSecure && !restoring)
            {
                // Owner answer A3: suggest, do not share (secure records only).
                await EnsureRowsAsync(run, subject, byField,
                    new AssignedAccessLedgerWrite(AssignedAccessState.PendingConfirmation, null, null, user), ct).ConfigureAwait(false);
                run.Entry(subject, fields, user, AssignedAccessState.PendingConfirmation, null, AssignedAccessAction.Ledger);
                return;
            }
        }

        var written = await WriteShareAsync(run, subject, user, ct).ConfigureAwait(false);
        if (written is not { } mask)
            return; // failure recorded; nothing in the ledger changes

        var reason = current != 0 ? AssignedAccessReason.RaisedFromMaskPrefix + current : null;
        await EnsureRowsAsync(run, subject, byField,
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, reason, null, user, mask), ct).ConfigureAwait(false);
        run.Entry(subject, fields, user, AssignedAccessState.Shared, reason,
            restoring ? AssignedAccessAction.Restored : AssignedAccessAction.Shared);
    }

    // =========================================================================================
    // An assignment that ended (owner answer A4)
    // =========================================================================================

    private async Task EndAssignmentAsync(
        Run run, AssignedAccessLedgerRow row, AssignedSubject subject, bool stillNamedElsewhere, RootRecordFlags flags,
        CancellationToken ct)
    {
        var field = row.SourceField!.Trim().ToLowerInvariant();
        var fields = new[] { field };

        async Task EndAsync(string reason, string action)
        {
            await UpdateRowAsync(run, row, new AssignedAccessLedgerWrite(AssignedAccessState.Revoked, reason), ct).ConfigureAwait(false);
            run.Entry(subject, fields, row.SystemUserId, AssignedAccessState.Revoked, reason, action);
        }

        switch (row.State)
        {
            case AssignedAccessState.Granted:
                {
                    if (stillNamedElsewhere)
                    {
                        await EndAsync(AssignedAccessReason.KeptOtherField, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    var key = GrantKeyFor(run, subject);
                    var active = await ExternalGrantLifecycle.QueryActiveRowsAsync(_dataverse, key, ct).ConfigureAwait(false);
                    var grant = active.FirstOrDefault(r => r.Id == row.GrantId);
                    if (grant is null)
                    {
                        await EndAsync(AssignedAccessReason.AssignmentEnded, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (IsModified(row, grant))
                    {
                        await EndAsync(AssignedAccessReason.KeptModified, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (!run.Request.RevokeOnChange)
                    {
                        run.Entry(subject, fields, null, AssignedAccessState.Granted, AssignedAccessReason.AccessRemoved,
                            AssignedAccessAction.WouldRevoke);
                        return;
                    }

                    if (TryParseRaisedGrant(row.Reason, out var priorLevel, out var priorExpiry)
                        && Enum.IsDefined(typeof(ExternalAccessLevel), priorLevel))
                    {
                        // A raised MANUAL grant goes back to what someone chose — the level AND the date (task 142 r2, finding
                        // 1): an explicit lower request carrying an explicit date, so the core writes both and the rule's own
                        // renewal (A5) never outlives the assignment. Not narrowed (the earlier level is below Collaborate).
                        var conferredBefore = ExternalParticipationService.ConfersAccessOn(grant.ExpiresDate, run.Today);
                        var restored = await WriteGrantAsync(run, subject, (ExternalAccessLevel)priorLevel, priorExpiry, ct)
                            .ConfigureAwait(false);
                        if (restored.Refusal is { } refusal)
                        {
                            if (IsPolicyHold(refusal))
                            {
                                // Task 142 r2, finding 2: the record's policy (Restricted, an organization on a Secure or
                                // Limited record) or its No Access list forbids writing this grantee now — for as long as that
                                // lasts, possibly indefinitely (a secure record stays secure). Not a failure: nothing is
                                // exposed (the read path suppresses this grant on the same terms) and nothing can be done until
                                // it changes. The ledger row stays Granted, so every pass tries again and the restore happens
                                // the first pass after the policy allows it — a form save, Update Access, or the job.
                                _logger.LogInformation(
                                    "[ASSIGNED-ACCESS] {Type} {RootId}: putting back {Subject}'s raised grant waits on the record's " +
                                    "policy ({Reason}).", run.Logical, run.RootId, subject, refusal.ReasonCode);
                                run.Entry(subject, fields, null, AssignedAccessState.Granted,
                                    AssignedAccessReason.RestorePendingPrefix + refusal.ReasonCode, AssignedAccessAction.None);
                                return;
                            }

                            if (refusal.IsDenyListReadFault)
                            {
                                // Task 142 r3/r4 (verifier r2 finding 4; owner round 13 item 4): the core's No Access check
                                // could not be completed. Not the record's policy: a fault, reported (Success=false), never a
                                // green "restore-pending". Nothing written; the row stays Granted, so the next pass retries.
                                DenyListFault(run, subject, "its earlier level");
                                return;
                            }

                            run.Fail(subject, "restore-refused",
                                $"The raised grant of {subject} could not be put back to its earlier level ({refusal.ReasonCode}).");
                            return;
                        }

                        run.Writes++;

                        if (restored.Warning is not null)
                        {
                            // ADR-003: the earlier level and date are back on the row, but that date has passed, so the grant
                            // confers nothing — no access was put back, and none is reported. When the grant still conferred
                            // before this write (the rule had renewed it past the operator's date), putting the date back ENDED
                            // the access: reported as revoked. The core returns before its own cache invalidation on this
                            // path, so the grantee's cached grant set is cleared here — never left serving the ended access.
                            _logger.LogWarning(
                                "[ASSIGNED-ACCESS] {Type} {RootId}: {Subject}'s raised grant was put back to its earlier level and " +
                                "date, which has passed; it confers no access ({Warning}).", run.Logical, run.RootId, subject,
                                restored.Warning);
                            await InvalidateGrantSetAsync(subject).ConfigureAwait(false);
                            await EndAsync(AssignedAccessReason.PriorLevelRestoredLapsed,
                                conferredBefore ? AssignedAccessAction.Revoked : AssignedAccessAction.Ledger).ConfigureAwait(false);
                            return;
                        }

                        await EndAsync(AssignedAccessReason.PriorLevelRestored, AssignedAccessAction.Restored).ConfigureAwait(false);
                        return;
                    }

                    run.Writes += await ExternalGrantLifecycle.DeactivateAsync(_dataverse, active.Select(r => r.Id), _logger, ct)
                        .ConfigureAwait(false);
                    await InvalidateGrantSetAsync(subject).ConfigureAwait(false);
                    await EndAsync(AssignedAccessReason.AccessRemoved, AssignedAccessAction.Revoked).ConfigureAwait(false);
                    return;
                }

            case AssignedAccessState.Shared:
                {
                    if (stillNamedElsewhere)
                    {
                        await EndAsync(AssignedAccessReason.KeptOtherField, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (row.SystemUserId is not { } user || user == Guid.Empty)
                    {
                        await EndAsync(AssignedAccessReason.AssignmentEnded, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    var current = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
                    if (current == 0)
                    {
                        await EndAsync(AssignedAccessReason.AssignmentEnded, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (row.GrantedLevel is not { } written || written != current)
                    {
                        await EndAsync(AssignedAccessReason.KeptModified, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (flags.IsSecure)
                    {
                        // Owner S5: a secure record always keeps someone who can see it — this rule never removes a share there.
                        await EndAsync(AssignedAccessReason.KeptSecureRecord, AssignedAccessAction.Ledger).ConfigureAwait(false);
                        return;
                    }

                    if (!run.Request.RevokeOnChange)
                    {
                        run.Entry(subject, fields, user, AssignedAccessState.Shared, AssignedAccessReason.AccessRemoved,
                            AssignedAccessAction.WouldRevoke);
                        return;
                    }

                    var restoreTo = TryParsePrior(row.Reason, AssignedAccessReason.RaisedFromMaskPrefix, out var priorMask) ? priorMask : 0;
                    if (!await RemoveOrRestoreShareAsync(run, subject, user, restoreTo, ct).ConfigureAwait(false))
                        return; // failure recorded

                    await EndAsync(restoreTo != 0 ? AssignedAccessReason.PriorLevelRestored : AssignedAccessReason.AccessRemoved,
                        restoreTo != 0 ? AssignedAccessAction.Restored : AssignedAccessAction.Revoked).ConfigureAwait(false);
                    return;
                }

            case AssignedAccessState.Adopted:
                await EndAsync(AssignedAccessReason.KeptAdopted, AssignedAccessAction.Ledger).ConfigureAwait(false);
                return;

            default:
                // Declined, Covered, Pending, Skipped: the assignment ended, nothing of ours to remove. A Declined row
                // stops being sticky here — a LATER re-assignment is a new assignment.
                await EndAsync(AssignedAccessReason.AssignmentEnded, AssignedAccessAction.Ledger).ConfigureAwait(false);
                return;
        }
    }

    // =========================================================================================
    // Target resolution — the 141 link, eligibility, the organization's state
    // =========================================================================================

    private enum TargetKind
    {
        ContactGrant,
        OrganizationGrant,
        Share,
        Skip,
    }

    private sealed record Target(TargetKind Kind, string? SkipReason = null, Guid? SystemUserId = null)
    {
        public static Target Skip(string reason, Guid? user = null) => new(TargetKind.Skip, reason, user);
    }

    /// <param name="flags">The root's flags, read once per run: <see cref="InternalShareEndpoints.ClassifyEligibility"/> asks
    /// whether the record is Restricted for a linked user flagged external (owner round 67).</param>
    private async Task<Target> ResolveTargetAsync(AssignedSubject subject, RootRecordFlags flags, CancellationToken ct)
    {
        if (subject.Kind == AssignedSubjectKind.Organization)
        {
            int? state;
            try
            {
                state = await _store.ReadOrganizationStateAsync(subject.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[ASSIGNED-ACCESS] Organization {OrganizationId} could not be read.", subject.Id);
                return Target.Skip(AssignedAccessReason.SubjectUnreadable);
            }

            return state switch
            {
                null => Target.Skip(AssignedAccessReason.SubjectNotFound),
                0 => new Target(TargetKind.OrganizationGrant),
                _ => Target.Skip(AssignedAccessReason.SubjectInactive),
            };
        }

        // The contact, status-first (task 141's store — never IIdentityNormalizationService, whose "never an exception"
        // contract would turn a faulted read into "unlinked" and grant a contact row to an internal user).
        var contact = await _identities.GetContactAsync(subject.Id, ct).ConfigureAwait(false);
        if (contact.Status != LookupStatus.Read)
            return Target.Skip(AssignedAccessReason.LinkUnreadable);

        var row = contact.Rows.FirstOrDefault(r => r.ContactId == subject.Id);
        if (row is null)
            return Target.Skip(AssignedAccessReason.SubjectNotFound);
        if (!row.IsActive)
            return Target.Skip(AssignedAccessReason.SubjectInactive);

        Guid? boundOid = Guid.TryParse(row.RawOid?.Trim(), out var oid) && oid != Guid.Empty ? oid : null;

        IReadOnlyList<AssignedLinkCandidate> candidates;
        try
        {
            candidates = await _store.ReadLinkCandidatesAsync(subject.Id, boundOid, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[ASSIGNED-ACCESS] The systemusers contact {ContactId} represents could not be read.", subject.Id);
            return Target.Skip(AssignedAccessReason.LinkUnreadable);
        }

        // Task 141 §2/§5: the link (sprk_primarycontact) is honoured as it is; a user with NO link is represented by the
        // contact bound to its oid. A user linked to ANOTHER contact is not this contact's, whatever the oid says.
        var represented = candidates
            .Where(c => c.PrimaryContactId == subject.Id
                        || (c.PrimaryContactId is null && boundOid is { } b && c.Oid == b))
            .GroupBy(c => c.SystemUserId)
            .Select(g => g.First())
            .ToList();

        if (represented.Count == 0)
            return new Target(TargetKind.ContactGrant);

        if (represented.Count > 1)
            return Target.Skip(AssignedAccessReason.LinkAmbiguous); // never pick one of two

        // Owner round 67: the ONE rule /share-user applies. An enabled person is shared with, flagged external or not —
        // except on a Restricted record, where a user flagged external (sprk_isexternal = true; blank is not external)
        // gets nothing: no share, and no contact grant either, since a Restricted record admits no contact-based access.
        // That is recorded as Restricted (the reason a contact gets there), so lifting Restricted gives the share back.
        var user = represented[0];

        // Verifier V4: the ONE "barred on Restricted" predicate is asked FIRST — a DISABLED or non-person user flagged external
        // on a Restricted record is Restricted (the remover's known cause), never Ineligible (which would turn the remover's
        // removal into a sticky Declined and keep the share away after Restricted is lifted and the user re-enabled).
        if (InternalShareEndpoints.IsBarredOnRestricted(user.IsExternal, flags.IsRestricted))
            return Target.Skip(AssignedAccessReason.Restricted, user.SystemUserId);

        return InternalShareEndpoints.ClassifyEligibility(
                user.IsDisabled, user.AccessMode, user.ApplicationId, user.IsExternal, flags.IsRestricted) switch
        {
            InternalShareEndpoints.ShareEligibility.Eligible => new Target(TargetKind.Share, null, user.SystemUserId),
            InternalShareEndpoints.ShareEligibility.ExternalOnRestricted => Target.Skip(AssignedAccessReason.Restricted, user.SystemUserId),
            _ => Target.Skip(AssignedAccessReason.Ineligible, user.SystemUserId),
        };
    }

    private static string? KnownGrantRemovalCause(Target target, RootRecordFlags flags)
    {
        if (flags.IsInactive)
            return AssignedAccessReason.RootInactive; // project closure deactivates every grant on the record
        if (target.Kind == TargetKind.Skip && target.SkipReason == AssignedAccessReason.SubjectInactive)
            return AssignedAccessReason.SubjectInactive; // task 117 R2: an inactive organization's grants are deactivated
        return null;
    }

    // =========================================================================================
    // Writes — through the existing cores only (CLAUDE.md §11)
    // =========================================================================================

    /// <summary>
    /// The materializer's ONE route to a grant row: the grant core with the documented Assigned-To ceiling (named
    /// differently from the core so <c>GrantCeilingGuardTests</c> sees exactly one call of it, carrying the ceiling).
    /// </summary>
    private Task<GrantExternalAccessEndpoint.GrantUpsertOutcome> WriteGrantAsync(
        Run run, AssignedSubject subject, ExternalAccessLevel level, DateOnly? expiry, CancellationToken ct)
    {
        var request = new GrantAccessRequest(
            ContactId: subject.Kind == AssignedSubjectKind.Contact ? subject.Id : Guid.Empty,
            ProjectId: Guid.Empty,
            AccessLevel: level,
            ExpiryDate: expiry,
            OrganizationId: subject.Kind == AssignedSubjectKind.Organization ? subject.Id : null,
            RecordType: run.Request.RootType.ToString().ToLowerInvariant(),
            RecordId: run.RootId);

        // The ONE grant core (task 139, WP-1): policy, never-lower, No Access, expiry default, dedupe, invalidation.
        return GrantExternalAccessEndpoint.CreateGrantAsync(
            request, run.Request.RootType, run.RootId, run.Today, GrantCeiling.AssignedToRule, run.Request.GrantorOid,
            _dataverse, _participations, _accessibleRecords, _logger, ct);
    }

    /// <summary>
    /// Shares the root with <paramref name="user"/> at the union of what it holds and Collaborate — the
    /// <c>InternalShareEndpoints.ShareAsync</c> discipline: strict read, GrantAccess vs ModifyAccess from it, the stored
    /// mask read back, the user's root-set cache cleared in finally. Returns the confirmed mask, or <c>null</c> (failure
    /// recorded) when it could not be confirmed.
    /// </summary>
    private async Task<int?> WriteShareAsync(Run run, AssignedSubject subject, Guid user, CancellationToken ct)
    {
        var entitySet = ExternalGrantRoot.BindFor(run.Request.RootType).EntitySet;
        var current = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
        var target = current | CollaborateMask;
        if (current == target)
            return current;

        var principal = DataversePrincipalRef.User(user);
        int? stored = null;
        Exception? failure = null;
        try
        {
            if (current == 0)
                await _recordShare.GrantAccessAsync(entitySet, run.RootId, principal, RecordShareLevels.RightsCsvForMask(target), ct)
                    .ConfigureAwait(false);
            else
                await _recordShare.ModifyAccessAsync(entitySet, run.RootId, principal, RecordShareLevels.RightsCsvForMask(target), ct)
                    .ConfigureAwait(false);
            run.Writes++;
            stored = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            await InvalidateRootSetAsync(run, user).ConfigureAwait(false);
        }

        if (failure is not null || stored != target)
        {
            _logger.LogError(failure,
                "[ASSIGNED-ACCESS] Sharing {Type} {RootId} with {User} ({Subject}) was not confirmed: held {Current}, asked {Target}, " +
                "read back {Stored}.", run.Logical, run.RootId, user, subject, current, target, stored?.ToString() ?? "nothing readable");
            run.Fail(subject, "share-not-confirmed",
                $"Sharing this record with user {user} could not be confirmed. The next update will try again.");
            return null;
        }

        run.RootShareWritten = true; // task 149: a CONFIRMED root share write — its secure children follow (SyncChildrenAsync)
        return stored;
    }

    private async Task<bool> RemoveOrRestoreShareAsync(Run run, AssignedSubject subject, Guid user, int restoreTo, CancellationToken ct)
    {
        var entitySet = ExternalGrantRoot.BindFor(run.Request.RootType).EntitySet;
        var principal = DataversePrincipalRef.User(user);
        int? remaining = null;
        Exception? failure = null;
        try
        {
            if (restoreTo == 0)
                await _recordShare.RevokeAccessAsync(entitySet, run.RootId, principal, ct).ConfigureAwait(false);
            else
                await _recordShare.ModifyAccessAsync(entitySet, run.RootId, principal, RecordShareLevels.RightsCsvForMask(restoreTo), ct)
                    .ConfigureAwait(false);
            run.Writes++;
            remaining = await ReadDirectShareMaskAsync(run, user, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            await InvalidateRootSetAsync(run, user).ConfigureAwait(false);
        }

        if (failure is not null || remaining != restoreTo)
        {
            _logger.LogError(failure, "[ASSIGNED-ACCESS] Ending {User}'s auto share on {Type} {RootId} was not confirmed.",
                user, run.Logical, run.RootId);
            run.Fail(subject, "share-removal-not-confirmed",
                $"Removing the automatic share of user {user} could not be confirmed. The next update will try again.");
            return false;
        }

        run.RootShareWritten = true; // task 149: a CONFIRMED root share removal / restore — its secure children follow
        return true;
    }

    /// <summary>The user's DIRECT share mask on the root, from the STRICT read (a failure throws — never "no share").</summary>
    private async Task<int> ReadDirectShareMaskAsync(Run run, Guid user, CancellationToken ct)
    {
        var shares = await _recordShare.GetPrincipalAccessOrThrowAsync(run.Logical, run.RootId, ct).ConfigureAwait(false);
        var principal = DataversePrincipalRef.User(user);
        return shares.Where(s => s.Principal == principal).Aggregate(0, (mask, s) => mask | s.AccessRightsMask);
    }

    /// <summary>
    /// Clears the shared user's impersonated root-set cache under the tenant key <c>ImpersonatedRootSetSource.GetAsync</c>
    /// reads for that user — the request's tenants (the deployment's, for the job) — never "anonymous" (task 143's rule).
    /// </summary>
    private async Task InvalidateRootSetAsync(Run run, Guid user)
    {
        foreach (var tenant in run.Request.CacheTenants.Where(t => !string.IsNullOrWhiteSpace(t) && t != "anonymous").Distinct())
        {
            await ImpersonatedRootSetSource.InvalidateForTenantAsync(_cache, tenant, user, run.Logical, _logger).ConfigureAwait(false);
        }
    }

    private Task InvalidateGrantSetAsync(AssignedSubject subject)
        => _participations.InvalidateGrantSetsAsync(
            subject.Kind == AssignedSubjectKind.Contact ? new[] { subject.Id } : Array.Empty<Guid>(),
            subject.Kind == AssignedSubjectKind.Organization ? new[] { subject.Id } : Array.Empty<Guid>(),
            CancellationToken.None);

    // =========================================================================================
    // The ledger
    // =========================================================================================

    private static Dictionary<string, AssignedAccessLedgerRow?> RowsByField(
        IReadOnlyList<string> fields, IReadOnlyList<AssignedAccessLedgerRow> rows)
        => fields.ToDictionary(
            f => f,
            f => rows.FirstOrDefault(r => string.Equals(r.SourceField?.Trim(), f, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes <paramref name="write"/> to every field's row of the subject — only where it is not already so.</summary>
    private async Task EnsureRowsAsync(
        Run run, AssignedSubject subject, Dictionary<string, AssignedAccessLedgerRow?> byField, AssignedAccessLedgerWrite write,
        CancellationToken ct)
    {
        foreach (var (field, row) in byField)
        {
            if (row is not null && Says(row, write))
                continue;

            if (row is null)
                await _store.CreateLedgerAsync(run.Request.RootType, run.RootId, field, subject, write, ct).ConfigureAwait(false);
            else
                await _store.UpdateLedgerAsync(row.Id, write, ct).ConfigureAwait(false);

            run.Writes++;
        }
    }

    private async Task UpdateRowAsync(Run run, AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        if (Says(row, write))
            return;

        await _store.UpdateLedgerAsync(row.Id, write, ct).ConfigureAwait(false);
        run.Writes++;
    }

    /// <summary>Whether a row already records the write (a write never clears, so a value it leaves unset is not compared).</summary>
    internal static bool Says(AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write)
        => row.State == write.State
           && string.Equals(row.Reason, write.Reason, StringComparison.Ordinal)
           && (write.GrantId is null || row.GrantId == write.GrantId)
           && (write.SystemUserId is null || row.SystemUserId == write.SystemUserId)
           && (write.GrantedLevel is null || row.GrantedLevel == write.GrantedLevel)
           && (write.GrantedExpiry is null || row.GrantedExpiry == write.GrantedExpiry);

    /// <summary>
    /// "Unmodified" (owner answer A4): the grant still carries the level and expiry this owner last wrote. A manual change
    /// of either — or a renewal by anyone else — makes it modified, and modified access is never removed by the rule.
    /// </summary>
    private static bool IsModified(AssignedAccessLedgerRow ours, ExternalGrantRow grant)
        => (ours.GrantedLevel is { } level && grant.AccessLevel != level)
           || (ours.GrantedExpiry is { } expiry && grant.ExpiresDate != expiry);

    private static bool Covers(int mask) => (mask & CollaborateMask) == CollaborateMask;

    /// <summary>
    /// The rows that confer access on <paramref name="today"/> — <see cref="ExternalParticipationService.ConfersAccessOn"/>,
    /// the read filter's own predicate (an expired or never-bounded row confers nothing, though it stays at statecode 0).
    /// </summary>
    private static List<ExternalGrantRow> Conferring(IEnumerable<ExternalGrantRow> rows, DateOnly today)
        => rows.Where(r => ExternalParticipationService.ConfersAccessOn(r.ExpiresDate, today)).ToList();

    private static bool TryParsePrior(string? reason, string prefix, out int value)
    {
        value = 0;
        return reason is not null && reason.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(reason[prefix.Length..], System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Reads a raise's reason (<see cref="AssignedAccessReason.RaisedFromLevel"/>: <c>raised-from:{level}@{yyyy-MM-dd}</c>).
    /// A reason without BOTH parts is not a raise this owner can put back exactly: the caller then removes the access
    /// (fail closed — never a restore that could outlive the operator's date). No such row exists: the ledger was never
    /// deployed with the level-only form.
    /// </summary>
    private static bool TryParseRaisedGrant(string? reason, out int level, out DateOnly expiry)
    {
        level = 0;
        expiry = default;
        if (reason is null || !reason.StartsWith(AssignedAccessReason.RaisedFromLevelPrefix, StringComparison.Ordinal))
            return false;

        var parts = reason[AssignedAccessReason.RaisedFromLevelPrefix.Length..].Split('@');
        return parts.Length == 2
               && int.TryParse(parts[0], System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out level)
               && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out expiry);
    }

    /// <summary>
    /// The grant core's refusals that state the record's CURRENT policy or No Access list — task 138's Restricted and
    /// direct-only organization rules, FR-23's deny check — rather than a fault. A restore refused for one of them waits
    /// (task 142 r2, finding 2); any other refusal (an unreadable policy, a ceiling refusal) is a failure the job reports.
    /// A No Access check that could not be completed is a fault (task 142 r3/r4,
    /// <see cref="GrantPolicyDecision.IsDenyListReadFault"/>), so it is never a hold.
    /// </summary>
    private static bool IsPolicyHold(GrantPolicyDecision refusal)
        => !refusal.IsDenyListReadFault
           && refusal.ReasonCode is ExternalGrantLifecycle.RecordRestrictedReasonCode
               or ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode
               or ExternalGrantLifecycle.GranteeDeniedReasonCode;

    /// <summary>
    /// A No Access check that could not be completed. Three sources, one report: the deny-veto check answered
    /// <see cref="NoAccessCheckAnswer.Unverifiable"/> (task 142 r4 · owner round 13 item 4 — a read fault it used to absorb
    /// into "denied"), or it THREW (r3, verifier r2 finding 4), or task 143's wall guard answered
    /// <see cref="SecureShareWallOutcome.Unverifiable"/> on the internal-user share path (r4 · owner round 13 item 5).
    /// Never read as an entry or as a policy hold: the caller writes nothing (fail closed, ADR-003), and the subject is
    /// reported as a <see cref="DenyListUnreadableFailure"/> — logged as its own line and counted by the job — so a
    /// sustained outage turns the job red (ADR-036 A1) instead of passing green. The next pass decides again.
    /// </summary>
    /// <param name="detail">What could not be read, when the answer names it (the wall guard's fault, the answer).</param>
    private void DenyListFault(Run run, AssignedSubject subject, string what, Exception? ex = null, string? detail = null)
    {
        _logger.LogError(ex,
            "[ASSIGNED-ACCESS] DENY-LIST-UNREADABLE {Type} {RootId}: the No Access check for {Subject} could not be " +
            "completed ({Detail}), so {What} was not written (fail closed); the next pass tries again.",
            run.Logical, run.RootId, subject, detail ?? ex?.GetType().Name ?? "unverifiable", what);
        run.Fail(subject, DenyListUnreadableFailure,
            $"The No Access list could not be checked for {subject} on this record, so {what} was not written. " +
            "It is tried again automatically.");
    }

    private static ExternalGrantKey GrantKeyFor(Run run, AssignedSubject subject)
        => subject.Kind == AssignedSubjectKind.Contact
            ? ExternalGrantKey.ForContact(run.Request.RootType, run.RootId, subject.Id)
            : ExternalGrantKey.ForOrganization(run.Request.RootType, run.RootId, subject.Id);

    // =========================================================================================
    // Markers — the operator's own actions (criterion 9). Ledger-only; never throw.
    // =========================================================================================

    /// <summary>
    /// <c>/revoke</c> removed a grant: every live ledger row of that subject on that root becomes
    /// <see cref="AssignedAccessState.Declined"/> (operator removal sticks). Returns the residual read-time access the
    /// subject still has there (owner A2: standing and organization access stay) so the operator is told — never shown
    /// "removed" while access silently remains (criterion 17). Never throws.
    /// </summary>
    public async Task<IReadOnlyList<string>> MarkGrantRevokedAsync(ExternalGrantRootType rootType, Guid rootId,
        Guid? contactId, Guid? organizationId, CancellationToken ct)
    {
        var subject = contactId is { } c && c != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Contact, c)
            : organizationId is { } o && o != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Organization, o)
            : (AssignedSubject?)null;
        if (subject is null)
            return Array.Empty<string>();

        var marked = await MarkAsync(rootType, rootId, r => r.Subject == subject,
            r => r.State is AssignedAccessState.Granted or AssignedAccessState.CoveredByExisting or AssignedAccessState.Adopted
                or AssignedAccessState.PendingConfirmation or AssignedAccessState.Skipped,
            new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.RemovedByOperator), ct)
            .ConfigureAwait(false);

        if (marked == 0)
            return Array.Empty<string>();

        return await ReadResidualTermsAsync(rootType, rootId, subject.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>/unshare-user</c> is removing a share: the subject's live rows naming that user become Declined — written BEFORE the
    /// revoke (task 158 r1c-v2, round 47 item 2). Returns every row it marked (or tried to: a write that threw may have
    /// applied) as it was BEFORE the marker, so the route can put them back when its revoke is not confirmed
    /// (<see cref="RevertShareRemovedAsync"/>). Never throws.
    /// </summary>
    public async Task<IReadOnlyList<AssignedAccessLedgerRow>> MarkShareRemovedAsync(
        ExternalGrantRootType rootType, Guid rootId, Guid systemUserId, CancellationToken ct)
        => (await MarkRowsAsync(rootType, rootId, r => r.SystemUserId == systemUserId,
            r => r.State is AssignedAccessState.Shared or AssignedAccessState.CoveredByExisting or AssignedAccessState.Adopted
                or AssignedAccessState.PendingConfirmation or AssignedAccessState.Skipped,
            ShareRemovedMarker, ct).ConfigureAwait(false)).Attempted;

    private static readonly AssignedAccessLedgerWrite ShareRemovedMarker =
        new(AssignedAccessState.Declined, AssignedAccessReason.RemovedByOperator);

    /// <summary>
    /// Task 158 final round (main-session round 58 item 2): the operator's removal did NOT happen — its revoke failed or was not
    /// confirmed — so every row <see cref="MarkShareRemovedAsync"/> marked is put back to the state and reason it held before.
    /// A share the operator did not actually remove is never on record as declined (a Declined row is ended by its parent's
    /// unshare WITHOUT removing the share, so the share would outlive its source). Never throws: a row that cannot be put back
    /// is logged, and the route's answer (the removal was not confirmed) already asks the operator to try again.
    /// </summary>
    public async Task RevertShareRemovedAsync(
        ExternalGrantRootType rootType, Guid rootId, IReadOnlyList<AssignedAccessLedgerRow> marked, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(marked);
        try
        {
            foreach (var before in marked)
            {
                await _store.UpdateLedgerAsync(before.Id, new AssignedAccessLedgerWrite(before.State, before.Reason), ct)
                    .ConfigureAwait(false);
            }

            if (marked.Count > 0)
            {
                _logger.LogInformation(
                    "[ASSIGNED-ACCESS] The share removal on {Type} {RootId} was not confirmed: {Count} Declined marker(s) put back.",
                    rootType, rootId, marked.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[ASSIGNED-ACCESS] The share removal on {Type} {RootId} was not confirmed, and its Declined marker(s) could not all " +
                "be put back; the operator's retry of the unshare (the answer asks for one) settles them.", rootType, rootId);
        }
    }

    /// <summary>A manual <c>/grant</c> or <c>/invite-and-grant</c> landed on the subject: its live rows become Adopted. Never throws.</summary>
    public Task<int> MarkGrantAdoptedAsync(ExternalGrantRootType rootType, Guid rootId, Guid? contactId, Guid? organizationId,
        Guid accessRecordId, CancellationToken ct)
    {
        var subject = contactId is { } c && c != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Contact, c)
            : organizationId is { } o && o != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Organization, o)
            : (AssignedSubject?)null;
        if (subject is null)
            return Task.FromResult(0);

        return MarkAsync(rootType, rootId, r => r.Subject == subject,
            r => r.State != AssignedAccessState.Revoked && r.State != AssignedAccessState.Adopted,
            new AssignedAccessLedgerWrite(AssignedAccessState.Adopted, AssignedAccessReason.ManualGrant,
                accessRecordId == Guid.Empty ? null : accessRecordId), ct);
    }

    /// <summary>A manual <c>/share-user</c> landed on a user the ledger knows: those rows become Adopted. Never throws.</summary>
    public Task<int> MarkShareAdoptedAsync(ExternalGrantRootType rootType, Guid rootId, Guid systemUserId, CancellationToken ct)
        => MarkAsync(rootType, rootId, r => r.SystemUserId == systemUserId,
            r => r.State != AssignedAccessState.Revoked && r.State != AssignedAccessState.Adopted,
            new AssignedAccessLedgerWrite(AssignedAccessState.Adopted, AssignedAccessReason.ManualGrant, null, systemUserId), ct);

    /// <summary>
    /// Dismiss a suggestion (owner A3: Dismiss = Declined): the entry's subject, on its root, if it is
    /// <see cref="AssignedAccessState.PendingConfirmation"/>. Returns <c>null</c> when no such pending entry is on the root.
    /// Exceptions propagate (the endpoint answers ProblemDetails).
    /// </summary>
    public async Task<int?> DismissAsync(ExternalGrantRootType rootType, Guid rootId, Guid entryId, CancellationToken ct)
    {
        var ledger = await _store.ReadLedgerAsync(rootType, rootId, ct).ConfigureAwait(false);
        var entry = ledger.FirstOrDefault(r => r.Id == entryId);
        if (entry is null || entry.Subject is not { } subject || entry.State != AssignedAccessState.PendingConfirmation)
            return null;

        var count = 0;
        foreach (var row in ledger.Where(r => r.Subject == subject && r.State == AssignedAccessState.PendingConfirmation))
        {
            await _store.UpdateLedgerAsync(row.Id,
                new AssignedAccessLedgerWrite(AssignedAccessState.Declined, AssignedAccessReason.Dismissed), ct).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private async Task<int> MarkAsync(
        ExternalGrantRootType rootType, Guid rootId, Func<AssignedAccessLedgerRow, bool> about,
        Func<AssignedAccessLedgerRow, bool> eligible, AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        var (attempted, faulted) = await MarkRowsAsync(rootType, rootId, about, eligible, write, ct).ConfigureAwait(false);
        return faulted ? 0 : attempted.Count;
    }

    /// <summary>
    /// Marks every eligible row, and answers the rows it marked or tried to (as read, BEFORE the marker) and whether a read
    /// or write faulted. Never throws.
    /// </summary>
    private async Task<(IReadOnlyList<AssignedAccessLedgerRow> Attempted, bool Faulted)> MarkRowsAsync(
        ExternalGrantRootType rootType, Guid rootId, Func<AssignedAccessLedgerRow, bool> about,
        Func<AssignedAccessLedgerRow, bool> eligible, AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        var attempted = new List<AssignedAccessLedgerRow>();
        try
        {
            var ledger = await _store.ReadLedgerAsync(rootType, rootId, ct).ConfigureAwait(false);
            foreach (var row in ledger.Where(r => about(r) && eligible(r)))
            {
                attempted.Add(row); // before the write: one that threw may still have applied
                await _store.UpdateLedgerAsync(row.Id, write, CancellationToken.None).ConfigureAwait(false);
            }

            if (attempted.Count > 0)
            {
                _logger.LogInformation(
                    "[ASSIGNED-ACCESS] {Count} ledger row(s) on {Type} {RootId} marked {State} ({Reason}).",
                    attempted.Count, rootType, rootId, write.State, write.Reason);
            }

            return (attempted, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The operator's action stands. The next sync or job pass detects a removal itself (out-of-band rule), so a
            // lost Declined marker is repaired; a lost Adopted marker leaves the row "ours" — logged loudly.
            _logger.LogError(ex,
                "[ASSIGNED-ACCESS] Could not mark the ledger of {Type} {RootId} {State}; the operator's change stands.",
                rootType, rootId, write.State);
            return (attempted, true);
        }
    }

    // =========================================================================================
    // Manage Access: the suggestions, provenance and residual read-time access (criteria 6 and 17)
    // =========================================================================================

    /// <summary>The live ledger entries of a root, for Manage Access. Exceptions propagate.</summary>
    public async Task<IReadOnlyList<AssignedAccessListEntry>> ListAsync(ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
    {
        var ledger = (await _store.ReadLedgerAsync(rootType, rootId, ct).ConfigureAwait(false))
            .Where(r => r.State != AssignedAccessState.Revoked && r.Subject is not null && !string.IsNullOrWhiteSpace(r.SourceField))
            .ToList();

        var names = await _store.ReadSubjectNamesAsync(ledger.Select(r => r.Subject!.Value).Distinct().ToList(), ct)
            .ConfigureAwait(false);

        var residual = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var subject in ledger
                     .Where(r => r.State is AssignedAccessState.Granted or AssignedAccessState.CoveredByExisting
                         or AssignedAccessState.Adopted)
                     .Select(r => r.Subject!.Value)
                     .Distinct())
        {
            residual[subject.Id] = await ReadResidualTermsAsync(rootType, rootId, subject, ct).ConfigureAwait(false);
        }

        return ledger
            .Select(r => new AssignedAccessListEntry(
                r.Id,
                r.SourceField!.Trim().ToLowerInvariant(),
                SourceFieldLabel(r.SourceField!),
                r.Subject!.Value.Kind == AssignedSubjectKind.Contact ? "contact" : "organization",
                r.Subject!.Value.Id,
                names.TryGetValue(r.Subject!.Value.Id, out var n) ? n : null,
                r.SystemUserId,
                r.GrantId,
                r.State.ToString(),
                r.Reason,
                residual.TryGetValue(r.Subject!.Value.Id, out var terms) ? terms : Array.Empty<string>()))
            .OrderBy(e => e.State == nameof(AssignedAccessState.PendingConfirmation) ? 0 : 1)
            .ThenBy(e => e.SourceField, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The stable names of the read-time terms that keep a contact on a record after its grant is removed.</summary>
    public static class ResidualTerm
    {
        /// <summary>FR-25: the contact holds a standing grant, and the record names it in an access-conferring column.</summary>
        public const string StandingGrant = "standing-grant";

        /// <summary>FR-24 / task 043: an organization the contact belongs to holds a standing grant, and the record names it.</summary>
        public const string OrganizationStandingGrant = "organization-standing-grant";

        /// <summary>
        /// FR-24 / task 043, for an ORGANIZATION subject: the organization holds a standing grant and the record still
        /// names it, so its member contacts keep reaching the record through organization expansion after the
        /// organization's grant is removed.
        /// </summary>
        public const string OrganizationMembersStandingGrant = "organization-members-standing-grant";

        /// <summary>The terms could not be read: the operator is told access MAY remain (never "removed" by default).</summary>
        public const string Unknown = "unknown";
    }

    /// <summary>
    /// Which read-time terms still bring <paramref name="subject"/> (a contact) — or, for an organization, its member
    /// contacts — to the root once its grant is gone (owner A2 reversed: they STAY). Mirrors the composition's own gates:
    /// none on a direct-only (Secure/Limited), Restricted or inactive record; for a contact, the standing term when the
    /// contact holds a standing grant (it is named here) and organization expansion when one of its conferring
    /// organizations holds a standing grant AND is named in an organization column here; for an organization, organization
    /// expansion when it holds a standing grant and is still named here. A read that fails answers
    /// <see cref="ResidualTerm.Unknown"/>. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadResidualTermsAsync(
        ExternalGrantRootType rootType, Guid rootId, AssignedSubject subject, CancellationToken ct)
    {
        try
        {
            var logical = ExternalGrantRoot.LogicalNameFor(rootType);
            var flags = await _participations.GetRootRecordFlagsAsync(logical, new[] { rootId }, ct).ConfigureAwait(false);
            if (!flags.TryGetValue(rootId, out var f) || f.IsUnreadable)
                return new[] { ResidualTerm.Unknown };
            if (f.IsDirectOnly || f.IsRestricted || f.IsInactive)
                return Array.Empty<string>();

            var registry = RegistryFor(rootType);
            var root = await _store.ReadRootAsync(rootType, rootId, registry.Select(r => r.Field).ToList(), ct).ConfigureAwait(false);
            if (root is null)
                return Array.Empty<string>();

            if (subject.Kind == AssignedSubjectKind.Organization)
            {
                var stillNamed = registry.Any(r => r.Kind == AssignedSubjectKind.Organization
                                                   && root.Values.TryGetValue(r.Field, out var o) && o == subject.Id);
                return stillNamed
                       && (await _standingGrants.ReadForOrganizationAsync(subject.Id, ct).ConfigureAwait(false)).Rights != AccessRights.None
                    ? new[] { ResidualTerm.OrganizationMembersStandingGrant }
                    : Array.Empty<string>();
            }

            var contactId = subject.Id;
            var terms = new List<string>();
            var namedContact = registry.Any(r => r.Kind == AssignedSubjectKind.Contact
                                                 && root.Values.TryGetValue(r.Field, out var v) && v == contactId);
            if (namedContact && (await _standingGrants.ReadForContactAsync(contactId, ct).ConfigureAwait(false)).Rights != AccessRights.None)
                terms.Add(ResidualTerm.StandingGrant);

            var namedOrgs = registry
                .Where(r => r.Kind == AssignedSubjectKind.Organization)
                .Select(r => root.Values.TryGetValue(r.Field, out var v) ? v : null)
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .ToHashSet();
            if (namedOrgs.Count > 0)
            {
                var memberships = await _participations.ReadOrganizationMembershipsAsync(contactId, ct).ConfigureAwait(false);
                if (memberships.Unreadable)
                {
                    terms.Add(ResidualTerm.Unknown);
                }
                else
                {
                    foreach (var org in memberships.ConferringOrganizationIds.Where(namedOrgs.Contains).Distinct())
                    {
                        if ((await _standingGrants.ReadForOrganizationAsync(org, ct).ConfigureAwait(false)).Rights != AccessRights.None)
                        {
                            terms.Add(ResidualTerm.OrganizationStandingGrant);
                            break;
                        }
                    }
                }
            }

            return terms;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[ASSIGNED-ACCESS] Residual access of {Subject} on {RootId} could not be read.",
                subject, rootId);
            return new[] { ResidualTerm.Unknown };
        }
    }

    /// <summary>"sprk_assignedparalegal1" → "Assigned Paralegal 1" — the label Manage Access names a suggestion by.</summary>
    internal static string SourceFieldLabel(string field)
    {
        var name = field.Trim().ToLowerInvariant();
        if (name.StartsWith("sprk_", StringComparison.Ordinal))
            name = name[5..];

        var words = new List<string>();

        // Known vocabulary, longest first, then a trailing number.
        var vocabulary = new[] { "assigned", "lawfirm", "attorney", "paralegal", "external", "internal", "to" };
        var display = new Dictionary<string, string>
        {
            ["assigned"] = "Assigned",
            ["lawfirm"] = "Law Firm",
            ["attorney"] = "Attorney",
            ["paralegal"] = "Paralegal",
            ["external"] = "(External)",
            ["internal"] = "(Internal)",
            ["to"] = "To",
        };

        var rest = name;
        while (rest.Length > 0)
        {
            var word = vocabulary.FirstOrDefault(w => rest.StartsWith(w, StringComparison.Ordinal));
            if (word is not null)
            {
                words.Add(display[word]);
                rest = rest[word.Length..];
                continue;
            }

            var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                words.Add(digits);
                rest = rest[digits.Length..];
                continue;
            }

            return field; // not a recognised name: show it as it is
        }

        return string.Join(" ", words);
    }

    // =========================================================================================
    // A pass
    // =========================================================================================

    private sealed class Run
    {
        public Run(AssignedAccessRequest request, DateOnly today)
        {
            Request = request;
            Today = today;
            Logical = ExternalGrantRoot.LogicalNameFor(request.RootType);
        }

        public AssignedAccessRequest Request { get; }
        public DateOnly Today { get; }
        public string Logical { get; }
        public Guid RootId => Request.RootId;
        public int Writes { get; set; }

        /// <summary>Task 149: this run made at least one CONFIRMED system-user share write on the root.</summary>
        public bool RootShareWritten { get; set; }

        public List<AssignedAccessEntryOutcome> Entries { get; } = new();
        public List<AssignedAccessFailure> Failures { get; } = new();

        public void Entry(AssignedSubject subject, IReadOnlyList<string> fields, Guid? user, AssignedAccessState state,
            string? reason, string action)
            => Entries.Add(new AssignedAccessEntryOutcome(
                subject.Kind == AssignedSubjectKind.Contact ? "contact" : "organization", subject.Id, fields.ToList(), user,
                state.ToString(), reason, action));

        public void Fail(AssignedSubject? subject, string kind, string message)
            => Failures.Add(new AssignedAccessFailure(
                subject is null ? null : subject.Value.Kind == AssignedSubjectKind.Contact ? "contact" : "organization",
                subject?.Id, kind, message));

        public AssignedAccessOutcome ToOutcome(string status)
            => new(Request.RootType.ToString().ToLowerInvariant(), Request.RootId, status, Entries, Failures, Writes);
    }
}
