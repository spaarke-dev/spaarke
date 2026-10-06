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
        "the automatic access of an assignment that ended.";

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

            // ── Candidates: every root with an Assigned column set, and every root holding a live ledger row ──
            var all = new Dictionary<(ExternalGrantRootType, Guid), DateTimeOffset?>();
            foreach (var type in RootTypes)
            {
                var fields = materializer.RegistryFor(type).Select(r => r.Field).ToList();
                var (roots, scanTruncated) = await store.ScanAssignedRootsAsync(type, fields, cancellationToken).ConfigureAwait(false);
                truncated |= scanTruncated;
                foreach (var r in roots)
                    all[(r.RootType, r.RootId)] = r.ModifiedOn;
            }

            var (ledgerRoots, ledgerTruncated) = await store.ScanLedgerRootsAsync(cancellationToken).ConfigureAwait(false);
            truncated |= ledgerTruncated;
            foreach (var r in ledgerRoots)
                all.TryAdd((r.RootType, r.RootId), null);

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

                if (!outcome.Complete)
                    incompleteRoots.Add(id);
            }
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
            "truncated={Truncated} rotating={Rotating} cacheTenantConfigured={CacheTenant} durationMs={DurationMs} " +
            "attempt={Attempt} correlationId={CorrelationId}",
            status, candidates, materialized, writes, wouldRevoke, revokeOnChange, incompleteRoots.Count, denyListUnreadable,
            truncated, rotating,
            tenant is not null, (long)duration.TotalMilliseconds, context.Attempt, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: writes,
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
                    attempt = context.Attempt,
                },
                ResultJsonOptions));
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
