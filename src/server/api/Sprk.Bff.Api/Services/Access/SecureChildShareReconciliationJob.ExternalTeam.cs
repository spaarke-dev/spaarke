// unified-access-control-r2 task 191c (#2179 design §3.4, §3.7): the External-team pass of the two-minute share job (ADR-036: no
// new job). Its own watermarks, carried rows and sweep cursor, per instance, as the sibling passes keep theirs (ADR-052 §5).

namespace Sprk.Bff.Api.Services.Access;

public sealed partial class SecureChildShareReconciliationJob
{
    /// <summary>Task 191c: the most rows one recent-changes listing reads per lineage table; past it that table's watermark is
    /// held at the newest row read.</summary>
    internal const int MaxExternalListedPerTable = 500;

    /// <summary>Task 191c: the most rows carried between runs; past it the rest wait for the sweep (logged).</summary>
    internal const int MaxExternalCarried = 2000;

    /// <summary>Task 191c: the catch-up sweep's rows per run.</summary>
    internal const int ExternalSweepPageSize = 500;

    /// <summary>Task 191c (design §3.7): a full sweep starts at each instance start, then this long after the previous one
    /// started.</summary>
    internal static readonly TimeSpan ExternalSweepInterval = TimeSpan.FromHours(1);

    /// <summary>Task 191c: how far back the first run on an instance looks for changed rows.</summary>
    internal static readonly TimeSpan ExternalInitialLookback = TimeSpan.FromMinutes(60);

    /// <summary>Task 191c: each run's watermark is its start minus this overlap (clock skew, in-flight writes).</summary>
    internal static readonly TimeSpan ExternalOverlap = TimeSpan.FromMinutes(1);

    private readonly object _externalGate = new();
    private DateTime? _externalSince;
    private readonly Dictionary<string, DateTime> _externalHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Table, Guid Id)> _externalCarried = new();
    private (string Table, Guid After)? _externalSweepCursor;
    private bool _externalSweepActive = true; // the instance-start sweep
    private DateTimeOffset? _externalSweepStartedAt;

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
        public bool SweepPassComplete { get; init; }
        public string? Failure { get; init; }

        /// <summary>Nothing left out of line this run (or the pass is not composed in this host).</summary>
        public bool Ok => Status is StatusOk or StatusUnavailable;
    }

    /// <summary>
    /// Task 191c: carried rows, then every lineage row changed since this pass's watermark with everything filed under it (the
    /// reverse walk: a communication or thread to the documents derived from it and the records about it, a root to its
    /// descendants), then the next page of the catch-up sweep (at instance start, then hourly). Writes only what the engine's
    /// settings allow (off by default: the plan is reported per table). Never throws for a Dataverse fault.
    /// </summary>
    private async Task<ExternalTeamRun> ReconcileExternalTeamAsync(
        IServiceProvider services, DateTimeOffset startedAt, Guid runId, CancellationToken ct)
    {
        var engine = services.GetService<ExternalTeamShareSynchronizer>();
        if (engine is null)
            return new ExternalTeamRun(ExternalTeamRun.StatusUnavailable);

        DateTime since;
        Dictionary<string, DateTime> held;
        List<(string Table, Guid Id)> carried;
        (string Table, Guid After)? cursor;
        bool sweep;
        lock (_externalGate)
        {
            since = _externalSince ?? (startedAt - ExternalInitialLookback).UtcDateTime;
            held = new Dictionary<string, DateTime>(_externalHeld, StringComparer.OrdinalIgnoreCase);
            carried = _externalCarried.ToList();
            cursor = _externalSweepCursor;
            if (!_externalSweepActive && _externalSweepStartedAt is { } last && startedAt - last >= ExternalSweepInterval)
                _externalSweepActive = true;
            sweep = _externalSweepActive;
            if (sweep && cursor is null)
                _externalSweepStartedAt = startedAt;
        }

        var results = new List<ExternalTeamShareResult>();
        var stillCarried = new HashSet<(string Table, Guid Id)>();
        string? failure = null;
        var listingFailed = false;

        // 1. Carried rows (each brought into line on its own; no walk).
        if (carried.Count > 0)
        {
            var result = await engine.SyncRowsAsync(carried, "carried", ct).ConfigureAwait(false);
            results.Add(result);
            stillCarried.UnionWith(result.Carried);
        }

        // 2. Recent changes and their families.
        var listed = 0;
        try
        {
            var (rows, cappedAt) = await engine.ListModifiedSinceAsync(held, since, MaxExternalListedPerTable, ct).ConfigureAwait(false);
            listed = rows.Count;
            var result = rows.Count == 0 ? ExternalTeamShareResult.Nothing : await engine.SyncFamiliesAsync(rows, "recent", ct).ConfigureAwait(false);
            results.Add(result);
            stillCarried.UnionWith(result.Carried);
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

        // 3. The catch-up sweep's next page.
        var swept = 0;
        var sweepComplete = false;
        if (sweep)
        {
            try
            {
                var (page, next) = await engine.ListSweepPageAsync(cursor, ExternalSweepPageSize, ct).ConfigureAwait(false);
                swept = page.Count;
                var result = page.Count == 0 ? ExternalTeamShareResult.Nothing : await engine.SyncRowsAsync(page, "sweep", ct).ConfigureAwait(false);
                results.Add(result);
                stillCarried.UnionWith(result.Carried);
                if (result.Status != ExternalTeamShareStatus.Failed)
                {
                    lock (_externalGate)
                    {
                        _externalSweepCursor = next;
                        if (next is null)
                        {
                            _externalSweepActive = false;
                            sweepComplete = true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failure ??= "the sweep listing failed";
                listingFailed = true;
                _logger.LogWarning(ex, "[EXTERNAL-TEAM-SHARE] run={RunId}: the sweep listing failed; its cursor is held.", runId);
            }
        }

        var kept = stillCarried.Take(MaxExternalCarried).ToList();
        if (stillCarried.Count > kept.Count)
        {
            _logger.LogWarning("[EXTERNAL-TEAM-SHARE] run={RunId}: {Dropped} carried row(s) over the cap of {Cap} wait for the sweep.",
                runId, stillCarried.Count - kept.Count, MaxExternalCarried);
        }

        lock (_externalGate)
        {
            _externalCarried.Clear();
            _externalCarried.UnionWith(kept);
        }

        var failed = results.Any(r => r.Status == ExternalTeamShareStatus.Failed);
        failure ??= results.FirstOrDefault(r => r.Status == ExternalTeamShareStatus.Failed)?.Detail;
        return new ExternalTeamRun(
            listingFailed ? ExternalTeamRun.StatusListingFailed
            : failed ? ExternalTeamRun.StatusFailed
            : kept.Count > 0 ? ExternalTeamRun.StatusIncomplete
            : ExternalTeamRun.StatusOk)
        {
            WritesEnabled = engine.WritesEnabled,
            ReownWritesEnabled = engine.ReownWritesEnabled,
            CarriedIn = carried.Count,
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
            Carried = kept.Count,
            SweepPassComplete = sweepComplete,
            Failure = failure,
        };
    }
}
