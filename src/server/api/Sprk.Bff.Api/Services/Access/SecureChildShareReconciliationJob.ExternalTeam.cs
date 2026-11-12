// unified-access-control-r2 task 191c (#2179 design §3.4, §3.7): the External-team pass of the two-minute share job (ADR-036: no
// new job). Its own watermarks, carried rows and sweep cursor, per instance, as the sibling passes keep theirs (ADR-052 §5).

namespace Sprk.Bff.Api.Services.Access;

public sealed partial class SecureChildShareReconciliationJob
{
    /// <summary>Task 191c: the most rows one recent-changes listing reads per lineage table; past it that table is held at the
    /// last (modifiedon, id) read and continues next run.</summary>
    internal const int MaxExternalListedPerTable = 500;

    /// <summary>Task 191c: the most rows carried between runs; past it the rest wait for the sweep (logged).</summary>
    internal const int MaxExternalCarried = 2000;

    /// <summary>Task 191c: how many consecutive runs a row is carried before it is left to the sweep (logged): a row that stays
    /// undecidable is not retried every two minutes for ever.</summary>
    internal const int MaxExternalCarriedRuns = 5;

    /// <summary>Task 191c: how many runs a family walk that stopped short (too many rows, or a read fault) is walked again before
    /// it is left to the sweep (logged).</summary>
    internal const int MaxExternalWalkAttempts = 3;

    /// <summary>Task 191c: the catch-up sweep's rows per page.</summary>
    internal const int ExternalSweepPageSize = 500;

    /// <summary>Task 191c: the most sweep pages one run reads, within <see cref="ExternalSweepBudget"/>.</summary>
    internal const int MaxExternalSweepPagesPerRun = 20;

    /// <summary>Task 191c: the time one run spends on sweep pages (a page started inside it finishes).</summary>
    internal static readonly TimeSpan ExternalSweepBudget = TimeSpan.FromSeconds(45);

    /// <summary>Task 191c (design §3.7): a full sweep starts at each instance start, then this long after the previous one
    /// started.</summary>
    internal static readonly TimeSpan ExternalSweepInterval = TimeSpan.FromHours(1);

    /// <summary>Task 191c: how far back the first run on an instance looks for changed rows.</summary>
    internal static readonly TimeSpan ExternalInitialLookback = TimeSpan.FromMinutes(60);

    /// <summary>Task 191c: each run's watermark is its start minus this overlap (clock skew, in-flight writes).</summary>
    internal static readonly TimeSpan ExternalOverlap = TimeSpan.FromMinutes(1);

    private readonly object _externalGate = new();
    private DateTime? _externalSince;
    private readonly Dictionary<string, (DateTime At, Guid AfterId)> _externalHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Table, Guid Id), int> _externalCarried = new();
    private readonly Dictionary<(string Table, Guid Id), int> _externalWalkAgain = new();
    private (string Table, Guid After)? _externalSweepCursor;
    private bool _externalSweepActive = true; // the instance-start sweep
    private DateTimeOffset? _externalSweepStartedAt;
    private int _externalUnavailableLogged;

    /// <summary>What the External-team pass did in one run.</summary>
    internal sealed record ExternalTeamRun(string Status)
    {
        internal const string StatusOk = "ok";
        internal const string StatusUnavailable = "unavailable";
        internal const string StatusIncomplete = "incomplete";
        internal const string StatusFailed = "failed";
        internal const string StatusListingFailed = "listing-failed";

        public bool WritesEnabled { get; init; }
        public bool ReownWritesEnabled { get; init; }
        public int CarriedIn { get; init; }
        public int WalkedAgain { get; init; }
        public int Listed { get; init; }
        public int Swept { get; init; }
        public int Examined { get; init; }
        public int Undecided { get; init; }
        public int TeamGrants { get; init; }
        public int TeamChanges { get; init; }
        public int TeamRevokes { get; init; }
        public int FlaggedUserRevokes { get; init; }
        public int Reowns { get; init; }
        public int Written { get; init; }
        public int Reowned { get; init; }
        public int ExternalTeamOwned { get; init; }
        public int DirectOwnedFlaggedShares { get; init; }
        public int Carried { get; init; }
        public int Failures { get; init; }
        public int LeftToSweep { get; init; }
        public bool SweepPassComplete { get; init; }
        public string? Failure { get; init; }

        /// <summary>The pass ran without a failure: no listing or prerequisite failed and no row was carried for a fault (a
        /// read, write or read-back failure, a withheld revoke). Rows carried only because the predicate answered Undecided are
        /// not a failure: holding the job on them would make it permanently incomplete (pass-1 F3, pass-2 F2).</summary>
        public bool Ok => Status is not (StatusFailed or StatusListingFailed) && Failures == 0;

        /// <summary>Nothing was left out of line this run (or the pass is not composed in this host).</summary>
        public bool Clean => Status is StatusOk or StatusUnavailable;
    }

    /// <summary>
    /// Task 191c: carried rows, family walks that stopped short, then every lineage row changed since this pass's watermark with
    /// everything filed under it (the reverse walk: a communication or thread to the documents derived from it and the records
    /// about it, a root to its descendants), then sweep pages within a time budget (at instance start, then hourly). Writes only
    /// what the engine's settings allow (off by default: the plan is reported per table). Never throws for a Dataverse fault.
    /// </summary>
    private async Task<ExternalTeamRun> ReconcileExternalTeamAsync(
        IServiceProvider services, DateTimeOffset startedAt, Guid runId, CancellationToken ct)
    {
        var engine = services.GetService<ExternalTeamShareSynchronizer>();
        if (engine is null)
        {
            // Once per instance (F4): a host without the engine never brings the External team's shares into line.
            if (Interlocked.Exchange(ref _externalUnavailableLogged, 1) == 0)
            {
                _logger.LogWarning("[EXTERNAL-TEAM-SHARE] run={RunId}: the External-team share engine is not composed in this host; " +
                    "the External Collaborators team's shares are not brought into line here.", runId);
            }

            return new ExternalTeamRun(ExternalTeamRun.StatusUnavailable);
        }

        DateTime since;
        Dictionary<string, (DateTime At, Guid AfterId)> held;
        Dictionary<(string Table, Guid Id), int> carriedRuns;
        Dictionary<(string Table, Guid Id), int> walkRuns;
        (string Table, Guid After)? cursor;
        bool sweep;
        lock (_externalGate)
        {
            since = _externalSince ?? (startedAt - ExternalInitialLookback).UtcDateTime;
            held = new Dictionary<string, (DateTime At, Guid AfterId)>(_externalHeld, StringComparer.OrdinalIgnoreCase);
            carriedRuns = new Dictionary<(string Table, Guid Id), int>(_externalCarried);
            walkRuns = new Dictionary<(string Table, Guid Id), int>(_externalWalkAgain);
            cursor = _externalSweepCursor;
            if (!_externalSweepActive && _externalSweepStartedAt is { } last && startedAt - last >= ExternalSweepInterval)
                _externalSweepActive = true;
            sweep = _externalSweepActive;
            if (sweep && cursor is null)
                _externalSweepStartedAt = startedAt;
        }

        var results = new List<ExternalTeamShareResult>();
        string? failure = null;
        var listingFailed = false;

        // 1. Carried rows (each brought into line on its own; no walk).
        if (carriedRuns.Count > 0)
            results.Add(await engine.SyncRowsAsync(carriedRuns.Keys.ToList(), "carried", ct).ConfigureAwait(false));

        // 2. Family walks that stopped short last run, walked again.
        //    One start per walk, so a large family does not stop the others at the same rows (pass-2 F1).
        foreach (var start in walkRuns.Keys)
            results.Add(await engine.SyncFamiliesAsync([start], "walk-again", ct).ConfigureAwait(false));

        // 3. Recent changes and their families.
        var listed = 0;
        try
        {
            var (rows, cappedAt) = await engine.ListModifiedSinceAsync(held, since, MaxExternalListedPerTable, ct).ConfigureAwait(false);
            listed = rows.Count;
            var result = rows.Count == 0 ? ExternalTeamShareResult.Nothing : await engine.SyncFamiliesAsync(rows, "recent", ct).ConfigureAwait(false);
            results.Add(result);
            if (result.Status != ExternalTeamShareStatus.Failed)
            {
                lock (_externalGate)
                {
                    _externalSince = (startedAt - ExternalOverlap).UtcDateTime;
                    _externalHeld.Clear();
                    foreach (var (table, at) in cappedAt)
                        _externalHeld[table] = at;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            listingFailed = true;
            failure = "the recent-changes listing failed";
            _logger.LogWarning(ex, "[EXTERNAL-TEAM-SHARE] run={RunId}: the recent-changes listing failed; the watermark is held and the " +
                "next run lists again.", runId);
        }

        // 4. The catch-up sweep: pages within the run's budget.
        var swept = 0;
        var sweepComplete = false;
        if (sweep)
        {
            var sweepStarted = _timeProvider.GetTimestamp();
            for (var page = 0; page < MaxExternalSweepPagesPerRun && _timeProvider.GetElapsedTime(sweepStarted) < ExternalSweepBudget; page++)
            {
                try
                {
                    var (rows, next) = await engine.ListSweepPageAsync(cursor, ExternalSweepPageSize, ct).ConfigureAwait(false);
                    swept += rows.Count;
                    var result = rows.Count == 0 ? ExternalTeamShareResult.Nothing : await engine.SyncRowsAsync(rows, "sweep", ct).ConfigureAwait(false);
                    results.Add(result);
                    if (result.Status == ExternalTeamShareStatus.Failed)
                        break; // the cursor is held: the page is read again next run

                    cursor = next;
                    lock (_externalGate)
                    {
                        _externalSweepCursor = next;
                        if (next is null)
                        {
                            _externalSweepActive = false;
                            sweepComplete = true;
                        }
                    }

                    if (next is null)
                        break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    failure ??= "the sweep listing failed";
                    listingFailed = true;
                    _logger.LogWarning(ex, "[EXTERNAL-TEAM-SHARE] run={RunId}: the sweep listing failed; its cursor is held.", runId);
                    break;
                }
            }
        }

        // 5. What is looked at again next run, with how many runs each has been: past the cap a row (or a walk) is left to the
        //    sweep, which decides every row again.
        var leftToSweep = 0;
        var nextCarried = new Dictionary<(string Table, Guid Id), int>();
        foreach (var row in results.SelectMany(r => r.Carried).Distinct())
        {
            var runs = (carriedRuns.TryGetValue(row, out var n) ? n : 0) + 1;
            if (runs > MaxExternalCarriedRuns)
                leftToSweep++;
            else if (nextCarried.Count < MaxExternalCarried)
                nextCarried[row] = runs;
            else
                leftToSweep++;
        }

        var nextWalk = new Dictionary<(string Table, Guid Id), int>();
        foreach (var row in results.SelectMany(r => r.WalkAgain).Distinct())
        {
            var runs = (walkRuns.TryGetValue(row, out var n) ? n : 0) + 1;
            if (runs > MaxExternalWalkAttempts || nextWalk.Count >= MaxExternalCarried)
                leftToSweep++;
            else
                nextWalk[row] = runs;
        }

        if (leftToSweep > 0)
        {
            _logger.LogWarning("[EXTERNAL-TEAM-SHARE] run={RunId}: {Count} row(s) or walk(s) carried for {Runs} runs (or over the cap of " +
                "{Cap}) are left to the hourly sweep.", runId, leftToSweep, MaxExternalCarriedRuns, MaxExternalCarried);
        }

        lock (_externalGate)
        {
            _externalCarried.Clear();
            foreach (var (row, runs) in nextCarried)
                _externalCarried[row] = runs;
            _externalWalkAgain.Clear();
            foreach (var (row, runs) in nextWalk)
                _externalWalkAgain[row] = runs;
        }

        var failed = results.Any(r => r.Status == ExternalTeamShareStatus.Failed);
        failure ??= results.FirstOrDefault(r => r.Status == ExternalTeamShareStatus.Failed)?.Detail;
        return new ExternalTeamRun(
            listingFailed ? ExternalTeamRun.StatusListingFailed
            : failed ? ExternalTeamRun.StatusFailed
            : nextCarried.Count > 0 || nextWalk.Count > 0 ? ExternalTeamRun.StatusIncomplete
            : ExternalTeamRun.StatusOk)
        {
            WritesEnabled = engine.WritesEnabled,
            ReownWritesEnabled = engine.ReownWritesEnabled,
            CarriedIn = carriedRuns.Count,
            WalkedAgain = walkRuns.Count,
            Listed = listed,
            Swept = swept,
            Examined = results.Sum(r => r.Examined),
            Undecided = results.Sum(r => r.Undecided),
            TeamGrants = results.Sum(r => r.TeamGrants),
            TeamChanges = results.Sum(r => r.TeamChanges),
            TeamRevokes = results.Sum(r => r.TeamRevokes),
            FlaggedUserRevokes = results.Sum(r => r.FlaggedUserRevokes),
            Reowns = results.Sum(r => r.Reowns),
            Written = results.Sum(r => r.Written),
            Reowned = results.Sum(r => r.Reowned),
            ExternalTeamOwned = results.Sum(r => r.ExternalTeamOwned),
            DirectOwnedFlaggedShares = results.Sum(r => r.DirectOwnedFlaggedShares),
            Carried = nextCarried.Count + nextWalk.Count,
            Failures = results.Sum(r => r.Failures),
            LeftToSweep = leftToSweep,
            SweepPassComplete = sweepComplete,
            Failure = failure,
        };
    }
}
