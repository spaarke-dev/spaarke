using System.Text.Json;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 142 (GitHub #1065) — the Assigned-To safety net (L4): every 5 minutes, every root with
/// an "Assigned *" column set or a live ledger row is materialized through <see cref="AssignedAccessMaterializer"/>.
/// </summary>
/// <remarks>
/// <para><b>What it catches that nothing else does</b> (owner round 3 R3/R4: the form save and "Update Access" are the
/// fast paths, this is the net at ≤ 5 minutes): a grid edit, an import or a flow (no product code runs); a form or
/// wizard whose sync call failed; a task-141 link that appeared (grant → share conversion, carrying a Declined state);
/// a record that became Secure, Restricted or walled; an auto grant or share removed outside the BFF (recorded Declined,
/// never re-created); renewal of a still-assigned auto grant inside the reminder window (owner A5). The job adds no rule
/// of its own — the ONE owner decides.</para>
///
/// <para><b>Restricted records and users flagged external</b> (unified-access-control-r2 task 114, owner round 67 amendment
/// 4(b)). Every RESTRICTED root is a candidate too (<see cref="AssignedAccessStore.ScanRestrictedRootsAsync"/>), and on each
/// one <see cref="RestrictedExternalShareRemover"/> removes the direct shares of users flagged <c>sprk_isexternal = true</c>
/// — before the materializer, as the sync route orders them — the backstop for a share made after the record became
/// Restricted (the platform's Share dialog, which the Access ribbon hides on such records). WRITES ON, like task 143's No
/// Access job: it only ever removes, inside its own rules, and is not the ended-assignment removal the revoke switch below
/// governs. Restricted wins over the last-reader rule (owner round 67 item 3): a secure Restricted record left with no
/// internal reader is counted (<c>noInternalReader</c>) and logged naming the record — an administrator shares it with an
/// internal user; it does not make the run partial. A Restricted
/// record OWNED by a user flagged external is counted (<c>ownerIsExternal</c>) and logged as a warning naming the record —
/// an administrator reassigns it; it does not make the run partial (ownership is not a share, and is never changed here).</para>
///
/// <para><b>Posture</b> (owner answer R3 / escalation (g), as amended by round 3 R3/R4 "minutes, never hourly"):
/// registered ENABLED, every 5 minutes, with writes ON for create, convert and renew. Its REMOVAL direction —
/// revoke-on-change for a field cleared outside the product — is gated on <see cref="RevokeOnChangeConfigKey"/>, named
/// positively so an absent, empty or unparseable value is REPORT-ONLY ("would-revoke" in the result). This task turns it
/// on in dev once the live gate passes. ⚠️ While it is report-only, a stale auto grant left by a field change made
/// OUTSIDE the product can survive indefinitely (fail-open in the removal direction), not merely for one cadence; the
/// sync route and the L1 writers always remove — the window is only for non-product writes.</para>
///
/// <para><b>Cache key.</b> No caller: a shared user's impersonated root-set cache is cleared under the DEPLOYMENT's tenant
/// (<see cref="ImpersonatedRootSetSource.DeploymentCacheTenant"/>) — the namespace that user's own reads write — never
/// <c>CacheTenantFor(null)</c> = "anonymous". Unconfigured, nothing is cleared and a stale set lapses within the cache
/// TTL; the run says so.</para>
///
/// <para><b>The <see cref="ExternalAccessReconciliationJob"/> A1 rules, applied.</b> A bounded run: the candidate roots
/// are read (two scans per root type, each to <see cref="AssignedAccessStore.MaxScanRows"/>; one more reports TRUNCATED),
/// and at most <see cref="MaxRootsPerRun"/> are materialized per run — recently modified roots FIRST (so a grid edit is
/// picked up on the next tick), then a window that ROTATES from run to run so every root is visited within
/// ceil(N / <see cref="MaxRootsPerRun"/>) runs. A failed scan or any root the materializer could not complete records
/// <c>Success=false</c> — never "nothing to reconcile"; a heartbeat line every attempt; no throw from
/// <see cref="ExecuteAsync"/> (ADR-036 A1 rule 4: idempotent by construction — an unchanged root costs zero writes — so
/// the next tick picks up whatever this one could not). No chunk claims or markers: every write is decided from a fresh
/// read of the root, its ledger and its grants/shares, and a second run repeats it as a no-op.</para>
///
/// <para><b>Rows whose record was deleted</b> (owner round 71, 2026-10-06). Deleting a matter or work assignment empties the
/// root lookup of its ledger rows (RemoveLink — a table may have only one cascade-delete parent, and the project holds it),
/// so the rows stay live with no root: the candidate scan cannot name a root for them, the materializer never visits them,
/// and they count toward that scan's bound forever. Each run therefore also reads the live Assigned-To rows with every root
/// lookup empty (<see cref="AssignedAccessStore.ScanRootlessLedgerRowsAsync"/>), reads the record the row's KEY names
/// (<see cref="AssignedAccessStore.RootFromLedgerKey"/> — the root id kept as text), and only when that read answers "no such
/// record" marks the row <see cref="AssignedAccessState.Revoked"/> with <see cref="AssignedAccessReason.RootDeleted"/> —
/// the state an ended assignment takes (rows are never deleted or deactivated by the BFF), written conditionally on the
/// version read (<see cref="AssignedAccessStore.UpdateLedgerIfUnchangedAsync"/>). It changes no access — the grant such a row
/// names is ended by <see cref="ExternalAccessReconciliationJob"/>'s rule R4 — so it is not behind the revoke switch. A read
/// that FAILS changes nothing (the run is reported partial and the next tick retries); a record that still exists, or a key
/// that names none, leaves the row as it is and is counted. At most <see cref="MaxRootsPerRun"/> records are read per run;
/// the rest wait for the next tick (reported).</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): the BFF's in-process <c>Spaarke.Scheduling</c> host, registered with
/// <c>AddScheduledJob</c> in <c>ExternalAccessModule</c> beside the No Access job — BFF domain code (the materializer,
/// the grant core, the share seam), BFF-owned tables, BFF identity, low volume (B2/B3). Deliberately not an R4 inside
/// <see cref="ExternalAccessReconciliationJob"/>: that job's one reason to change is the lifecycle of grant and
/// membership rows ("a row's own state is the truth"), and it ships report-only for writes — which would switch off a
/// feature the owner requires.</para>
/// </remarks>
public sealed class AssignedAccessReconciliationJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "assigned-access-reconciliation";

    /// <summary>Every 5 minutes (owner round 3 R3/R4: "≤ 5 min, never hourly").</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>
    /// The switch that lets the job REMOVE access on an ended assignment. Named positively, so an absent, empty or
    /// unparseable value is report-only. It governs the JOB only (owner answer R3 / (g)).
    /// </summary>
    internal const string RevokeOnChangeConfigKey = "ExternalAccess:AssignedAccess:JobRevokeOnChangeEnabled";

    /// <summary>Roots materialized per run. Past it the window rotates (reported ROTATING).</summary>
    internal const int MaxRootsPerRun = 200;

    /// <summary>A root modified this recently is materialized first, ahead of the rotating window.</summary>
    internal static readonly TimeSpan RecentChangeWindow = TimeSpan.FromMinutes(15);

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    /// <summary>How many root ids with problems the result lists (the logs carry every one).</summary>
    internal const int MaxSampledRoots = 50;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly ExternalGrantRootType[] RootTypes =
        { ExternalGrantRootType.Project, ExternalGrantRootType.Matter, ExternalGrantRootType.WorkAssignment };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssignedAccessReconciliationJob> _logger;

    private readonly object _cursorGate = new();
    private (ExternalGrantRootType Type, Guid Id)? _cursor;

    public AssignedAccessReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<AssignedAccessReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Assigned-To Access Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Every 5 minutes, gives every contact or organization named in an 'Assigned *' column of a project, matter or work " +
        "assignment its Collaborate access (or a share for a linked internal user), renews it before it lapses, records " +
        "access an operator removed outside the product so it is never re-created, and — when its switch is on — removes " +
        "the automatic access of an assignment that ended. Ledger rows whose record was deleted are marked revoked. On a " +
        "Restricted record, removes the shares of users flagged external.";

    /// <summary>Whether the job may REMOVE access on an ended assignment. Report-only unless explicitly true.</summary>
    internal bool RevokeOnChangeEnabled =>
        bool.TryParse(_configuration[RevokeOnChangeConfigKey], out var enabled) && enabled;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var revokeOnChange = RevokeOnChangeEnabled;
        var status = StatusOk;
        var problems = new List<string>();
        var materialized = 0;
        var writes = 0;
        var wouldRevoke = 0;
        var denyListUnreadable = 0;
        var candidates = 0;
        var truncated = false;
        var rotating = false;
        var incompleteRoots = new List<Guid>();
        var rootless = new RootlessLedgerReport();
        var restrictedCandidates = 0;
        var externalSharesRemoved = 0;
        var noInternalReader = 0;
        var ownerIsExternal = 0;

        var tenant = ImpersonatedRootSetSource.DeploymentCacheTenant(_configuration);
        var cacheTenants = tenant is null ? Array.Empty<string>() : new[] { tenant };
        if (tenant is null)
        {
            problems.Add("AzureAd:TenantId is not configured, so shared users' cached root sets were not cleared; they lapse " +
                         $"within {ImpersonatedRootSetSource.CacheTtl.TotalMinutes:0} minutes.");
            _logger.LogWarning(
                "[ASSIGNED-ACCESS-RECON] No deployment tenant configured (AzureAd:TenantId / TENANT_ID): shares will not clear " +
                "the shared users' cached root sets (never the 'anonymous' key).");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<AssignedAccessStore>();
            var materializer = scope.ServiceProvider.GetRequiredService<AssignedAccessMaterializer>();
            var restrictedRemover = scope.ServiceProvider.GetRequiredService<RestrictedExternalShareRemover>();

            // ── Candidates: every root with an Assigned column set, and every root holding a live ledger row ──
            var all = new Dictionary<(ExternalGrantRootType, Guid), DateTimeOffset?>();
            var assigned = new HashSet<(ExternalGrantRootType, Guid)>();
            foreach (var type in RootTypes)
            {
                var fields = materializer.RegistryFor(type).Select(r => r.Field).ToList();
                var (roots, scanTruncated) = await store.ScanAssignedRootsAsync(type, fields, cancellationToken).ConfigureAwait(false);
                truncated |= scanTruncated;
                foreach (var r in roots)
                {
                    all[(r.RootType, r.RootId)] = r.ModifiedOn;
                    assigned.Add((r.RootType, r.RootId));
                }
            }

            var (ledgerRoots, ledgerTruncated) = await store.ScanLedgerRootsAsync(cancellationToken).ConfigureAwait(false);
            truncated |= ledgerTruncated;
            foreach (var r in ledgerRoots)
            {
                all.TryAdd((r.RootType, r.RootId), null);
                assigned.Add((r.RootType, r.RootId));
            }

            // ── Task 114 (owner round 67 amendment 4(b)): every RESTRICTED root, for the external-user share rule — the
            //    backstop for a share made after the record became Restricted (the platform's own Share dialog) ──
            var restricted = new HashSet<(ExternalGrantRootType, Guid)>();
            foreach (var type in RootTypes)
            {
                var (roots, scanTruncated) = await store.ScanRestrictedRootsAsync(type, cancellationToken).ConfigureAwait(false);
                truncated |= scanTruncated;
                foreach (var r in roots)
                {
                    if (all.TryGetValue((r.RootType, r.RootId), out var known))
                        all[(r.RootType, r.RootId)] = known ?? r.ModifiedOn;
                    else
                        all[(r.RootType, r.RootId)] = r.ModifiedOn;
                    restricted.Add((r.RootType, r.RootId));
                }
            }

            // ── #1478 (task 175): the work assignments and projects filed BELOW a Restricted matter or project are Restricted
            //    through it (task 174's effective rule) before their own column catches up — candidates too. A listing that
            //    cannot complete is a problem of this run (the next run lists them again); nothing is decided on part of it. ──
            var restrictedParents = restricted
                .Where(r => r.Item1 is ExternalGrantRootType.Matter or ExternalGrantRootType.Project)
                .Select(r => (ExternalGrantRoot.LogicalNameFor(r.Item1), r.Item2))
                .ToList();
            if (restrictedParents.Count > 0)
            {
                var generic = scope.ServiceProvider.GetService<Spaarke.Dataverse.IGenericEntityService>();
                if (generic is null)
                {
                    problems.Add("RESTRICTED-BELOW: no Dataverse reader is configured, so records filed under Restricted records were not listed.");
                }
                else
                {
                    try
                    {
                        var below = await Sprk.Bff.Api.Services.Access.SecureRootInheritance.ListFiledRootsBelowAsync(
                            generic, _logger, restrictedParents, cancellationToken).ConfigureAwait(false);
                        foreach (var filed in below.Roots.Where(r => r.Confirmed))
                        {
                            var type = string.Equals(filed.Table, ExternalGrantRoot.LogicalNameFor(ExternalGrantRootType.Project),
                                StringComparison.OrdinalIgnoreCase)
                                ? ExternalGrantRootType.Project
                                : ExternalGrantRootType.WorkAssignment;
                            all.TryAdd((type, filed.Id), null);
                            restricted.Add((type, filed.Id));
                        }

                        if (below.DepthBoundReached)
                            problems.Add("RESTRICTED-BELOW: a filing chain below a Restricted record is deeper than the walk follows; the rest were not listed.");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex,
                            "[ASSIGNED-ACCESS-RECON] The records filed under {Count} Restricted record(s) could not be listed; they " +
                            "are listed again next run.", restrictedParents.Count);
                        problems.Add("RESTRICTED-BELOW: the records filed under Restricted records could not be listed this run.");
                    }
                }
            }

            restrictedCandidates = restricted.Count;

            if (truncated)
                problems.Add($"TRUNCATED: a candidate scan returned more than {AssignedAccessStore.MaxScanRows} rows; the rest were not read.");

            candidates = all.Count;
            var window = NextWindow(all);
            rotating = window.Count < all.Count;
            if (rotating)
            {
                problems.Add(
                    $"ROTATING: {all.Count} candidate roots exceed the {MaxRootsPerRun} materialized per run; recently changed " +
                    $"roots were taken first, and every root is visited within {(all.Count + MaxRootsPerRun - 1) / MaxRootsPerRun} runs.");
            }

            foreach (var (type, id) in window)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Before the materializer, as on the sync route: a share removed here is recorded by it as Restricted.
                if (restricted.Contains((type, id)))
                {
                    var report = await restrictedRemover.RemoveForRecordAsync(type, id, cacheTenants, cancellationToken)
                        .ConfigureAwait(false);
                    externalSharesRemoved += report.Removed.Count;
                    writes += report.Removed.Count;
                    if (report.NoInternalReader)
                        noInternalReader++; // the remover logged the record; an administrator shares it with an internal user
                    if (report.OwnerIsExternal is not null)
                        ownerIsExternal++; // the remover logged the record once this run; an administrator reassigns it
                    if (!report.Complete)
                        incompleteRoots.Add(id);
                }

                if (!assigned.Contains((type, id)))
                {
                    AdvanceCursor((type, id), rotating);
                    continue; // a Restricted root with no Assigned column or ledger row: nothing for the materializer
                }

                var outcome = await materializer.MaterializeAsync(
                    new AssignedAccessRequest(type, id, AssignedAccessTrigger.Job, GrantorOid: null, revokeOnChange, cacheTenants),
                    cancellationToken).ConfigureAwait(false);

                AdvanceCursor((type, id), rotating);
                materialized++;
                writes += outcome.Writes;
                wouldRevoke += outcome.Entries.Count(e => e.Action == AssignedAccessAction.WouldRevoke);
                denyListUnreadable += outcome.Failures.Count(f => f.Kind == AssignedAccessMaterializer.DenyListUnreadableFailure);

                // A deleted root (NotFound) and a root type with no registry are answers, not failures.
                if (outcome.Status is AssignedAccessStatus.NotFound or AssignedAccessStatus.NoRegistry)
                    continue;

                if (!outcome.Complete && !incompleteRoots.Contains(id))
                    incompleteRoots.Add(id); // once per root, even when the Restricted rule above also left it incomplete
            }

            // ── Ledger rows whose record was deleted (owner round 71) ──
            await RetireRootlessLedgerRowsAsync(store, rootless, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every candidate root was materialized.");
        }
        catch (Exception ex)
        {
            // A scan failed: recorded FAILED, never "nothing to reconcile" (ADR-003, inverted for a writer).
            status = StatusError;
            problems.Add($"The candidate roots could not be read: {ex.Message}");
            _logger.LogError(ex,
                "[ASSIGNED-ACCESS-RECON] Run failed — the candidate roots could not be scanned; nothing more was reconciled. " +
                "attempt={Attempt} correlationId={CorrelationId}", context.Attempt, context.CorrelationId);
        }

        if (incompleteRoots.Count > 0)
        {
            status = StatusError;
            problems.Add($"{incompleteRoots.Count} root(s) could not be fully materialized; see the per-root errors.");
        }

        if (denyListUnreadable > 0)
        {
            // Task 142 r3/r4: counted on its own, so a No Access read outage is visible as such — never a policy hold. Since
            // r4 it counts every source: the deny-veto check's Unverifiable answer, a throw, and task 143's wall guard on
            // the share path (owner round 13 items 4 and 5). The roots are already incomplete (the run is red); this names
            // the cause.
            problems.Add($"DENY-LIST-UNREADABLE: the No Access list could not be checked for {denyListUnreadable} subject(s), " +
                         "so nothing was granted, shared, suggested, renewed or put back for them (fail closed).");
        }

        if (rootless.ScanFailed)
        {
            status = StatusError;
            problems.Add("The ledger rows whose record was deleted could not be read; none was marked revoked.");
        }

        if (rootless.Unresolved > 0)
        {
            problems.Add($"{rootless.Unresolved} ledger row(s) with no record were left unchanged: the record their key names " +
                         "could not be read, or the row could not be written. The next run retries them.");
        }

        if (rootless.Truncated || rootless.Deferred > 0)
        {
            problems.Add($"ROOTLESS-LEDGER: more ledger rows with no record exist than one run takes ({rootless.Deferred} " +
                         "deferred); the next run continues.");
        }

        if (!revokeOnChange && wouldRevoke > 0)
        {
            // The configured posture, not a failure: reported (heartbeat + ResultJson.wouldRevoke) so the owner's flip of
            // the switch is an informed one, but it does not make the run partial.
            _logger.LogWarning(
                "[ASSIGNED-ACCESS-RECON] REPORT-ONLY: {WouldRevoke} ended assignment(s) kept their automatic access because " +
                "{Key} is not true.", wouldRevoke, RevokeOnChangeConfigKey);
        }

        if (status == StatusOk && (truncated || rotating || problems.Count > 0))
            status = StatusPartial;

        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT (ADR-036 A1 rule 5): one line on every attempt, whatever happened.
        _logger.LogInformation(
            "[ASSIGNED-ACCESS-RECON] heartbeat status={Status} candidates={Candidates} materialized={Materialized} " +
            "writes={Writes} wouldRevoke={WouldRevoke} revokeOnChange={RevokeOnChange} incomplete={Incomplete} " +
            "denyListUnreadable={DenyListUnreadable} " +
            "truncated={Truncated} rotating={Rotating} cacheTenantConfigured={CacheTenant} " +
            "rootlessFound={RootlessFound} rootlessRevoked={RootlessRevoked} rootlessRecordExists={RootlessRecordExists} " +
            "rootlessKeyUnparseable={RootlessKeyUnparseable} rootlessUnresolved={RootlessUnresolved} " +
            "rootlessDeferred={RootlessDeferred} rootlessScanFailed={RootlessScanFailed} " +
            "restrictedCandidates={RestrictedCandidates} externalSharesRemoved={ExternalSharesRemoved} " +
            "noInternalReader={NoInternalReader} ownerIsExternal={OwnerIsExternal} durationMs={DurationMs} " +
            "attempt={Attempt} correlationId={CorrelationId}",
            status, candidates, materialized, writes, wouldRevoke, revokeOnChange, incompleteRoots.Count, denyListUnreadable,
            truncated, rotating,
            tenant is not null,
            rootless.Found, rootless.Revoked, rootless.RecordExists, rootless.KeyUnparseable, rootless.Unresolved,
            rootless.Deferred, rootless.ScanFailed,
            restrictedCandidates, externalSharesRemoved, noInternalReader, ownerIsExternal,
            (long)duration.TotalMilliseconds, context.Attempt, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: writes + rootless.Revoked,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    candidates,
                    materialized,
                    writes,
                    wouldRevoke,
                    revokeOnChange,
                    truncated,
                    rotating,
                    cacheTenantConfigured = tenant is not null,
                    incompleteRoots = incompleteRoots.Take(MaxSampledRoots).ToArray(),
                    incompleteTotal = incompleteRoots.Count,
                    denyListUnreadable,
                    rootlessLedger = rootless.ToReport(),
                    restrictedCandidates,
                    externalSharesRemoved,
                    noInternalReader,
                    ownerIsExternal,
                    attempt = context.Attempt,
                },
                ResultJsonOptions));
    }

    /// <summary>
    /// Marks REVOKED (<see cref="AssignedAccessReason.RootDeleted"/>) each live Assigned-To ledger row whose root lookups are all
    /// empty AND whose key names a record that a read confirms is gone. Never on an inference: a failed read, a record that
    /// still exists and a key naming no record all leave the row unchanged (counted). Cancellation propagates; every other
    /// fault is counted on <paramref name="report"/>, never thrown — the materialized roots' outcome above is already decided.
    /// </summary>
    private async Task RetireRootlessLedgerRowsAsync(
        AssignedAccessStore store, RootlessLedgerReport report, JobRunContext context, CancellationToken ct)
    {
        IReadOnlyList<AssignedAccessLedgerRow> rows;
        try
        {
            (rows, report.Truncated) = await store.ScanRootlessLedgerRowsAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report.ScanFailed = true;
            _logger.LogError(ex,
                "[ASSIGNED-ACCESS-RECON] The ledger rows with no record could not be scanned; none was marked revoked. " +
                "correlationId={CorrelationId}", context.CorrelationId);
            return;
        }

        // record -> does it exist (null = the read failed). One read per record, however many rows name it.
        var exists = new Dictionary<AssignedRootRef, bool?>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            // Re-decided in code, not trusted from the scan filter: a row that names a root, or is already Revoked, is not ours.
            if (AssignedAccessStore.RootOf(row) is not null || row.State == AssignedAccessState.Revoked)
                continue;

            report.Found++;
            if (AssignedAccessStore.RootFromLedgerKey(row.LedgerKey) is not { } root)
            {
                report.KeyUnparseable++;
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS-RECON] Ledger row {RowId} names no record and its key '{Key}' names none either; left " +
                    "unchanged. correlationId={CorrelationId}", row.Id, row.LedgerKey, context.CorrelationId);
                continue;
            }

            if (!exists.TryGetValue(root, out var present))
            {
                if (exists.Count >= MaxRootsPerRun)
                {
                    report.Deferred++;
                    continue;
                }

                try
                {
                    present = await store.ReadRootAsync(root.RootType, root.RootId, Array.Empty<string>(), ct).ConfigureAwait(false)
                        is not null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    present = null;
                    _logger.LogError(ex,
                        "[ASSIGNED-ACCESS-RECON] {Type} {RootId} could not be read, so its ledger rows with no record are left " +
                        "unchanged (never revoked on a failed read). correlationId={CorrelationId}",
                        root.RootType, root.RootId, context.CorrelationId);
                }

                exists[root] = present;
            }

            if (present is null)
            {
                report.Unresolved++;
                continue;
            }

            if (present.Value)
            {
                // The record is there but the row's lookup is empty (cleared by hand?) — not a deletion; left as it is.
                report.RecordExists++;
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS-RECON] Ledger row {RowId} has no record lookup, but {Type} {RootId} its key names still " +
                    "exists; left unchanged. correlationId={CorrelationId}",
                    row.Id, root.RootType, root.RootId, context.CorrelationId);
                continue;
            }

            var priorState = row.State;
            var priorReason = row.Reason;
            try
            {
                if (await store.UpdateLedgerIfUnchangedAsync(
                        row, new AssignedAccessLedgerWrite(AssignedAccessState.Revoked, AssignedAccessReason.RootDeleted), ct)
                    .ConfigureAwait(false))
                {
                    report.Revoked++;
                    _logger.LogInformation(
                        "[ASSIGNED-ACCESS-RECON] Ledger row {RowId} marked Revoked ({Reason}): {Type} {RootId} was deleted. " +
                        "before=[state={PriorState} reason={PriorReason}] correlationId={CorrelationId}",
                        row.Id, AssignedAccessReason.RootDeleted, root.RootType, root.RootId, priorState, priorReason,
                        context.CorrelationId);
                }

                // false: the row changed since it was read — the next pass decides on it as it is now.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Unresolved++;
                _logger.LogError(ex,
                    "[ASSIGNED-ACCESS-RECON] Ledger row {RowId} (its record {RootId} was deleted) could not be marked revoked; " +
                    "the next run retries it. correlationId={CorrelationId}", row.Id, root.RootId, context.CorrelationId);
            }
        }
    }

    /// <summary>What one run did with the live ledger rows that name no record (owner round 71).</summary>
    private sealed class RootlessLedgerReport
    {
        public int Found;
        public int Revoked;
        public int RecordExists;
        public int KeyUnparseable;
        public int Unresolved;
        public int Deferred;
        public bool Truncated;
        public bool ScanFailed;

        public object ToReport() => new
        {
            found = Found,
            revoked = Revoked,
            recordExists = RecordExists,
            keyUnparseable = KeyUnparseable,
            unresolved = Unresolved,
            deferred = Deferred,
            truncated = Truncated,
            scanFailed = ScanFailed,
        };
    }

    /// <summary>
    /// The roots this run materializes: every root modified within <see cref="RecentChangeWindow"/> first, then — from a
    /// cursor after the last root a previous run reached, in (type, id) order, wrapping round — enough others to fill
    /// <see cref="MaxRootsPerRun"/>. All of them when they fit.
    /// </summary>
    private IReadOnlyList<(ExternalGrantRootType Type, Guid Id)> NextWindow(
        IReadOnlyDictionary<(ExternalGrantRootType, Guid), DateTimeOffset?> all)
    {
        var ordered = all.Keys.OrderBy(k => (int)k.Item1).ThenBy(k => k.Item2).ToList();
        if (ordered.Count <= MaxRootsPerRun)
            return ordered;

        var since = _timeProvider.GetUtcNow() - RecentChangeWindow;
        var window = ordered
            .Where(k => all[k] is { } modified && modified >= since)
            .OrderByDescending(k => all[k])
            .Take(MaxRootsPerRun)
            .ToList();
        var taken = window.ToHashSet();

        (ExternalGrantRootType, Guid)? cursor;
        lock (_cursorGate)
        {
            cursor = _cursor;
        }

        var start = cursor is { } after
            ? ordered.FindIndex(k => ((int)k.Item1, k.Item2).CompareTo(((int)after.Item1, after.Item2)) > 0)
            : 0;
        if (start < 0)
            start = 0;

        for (var i = 0; i < ordered.Count && window.Count < MaxRootsPerRun; i++)
        {
            var next = ordered[(start + i) % ordered.Count];
            if (taken.Add(next))
                window.Add(next);
        }

        return window;
    }

    private void AdvanceCursor((ExternalGrantRootType, Guid) root, bool rotating)
    {
        lock (_cursorGate)
        {
            _cursor = rotating ? root : null;
        }
    }
}
