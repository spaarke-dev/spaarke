using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Answers "does this session document still exist, and under what retention?" for the retention
/// sweep, without handing the sweep anything else.
/// </summary>
/// <remarks>
/// A one-method port rather than an injected <c>IServiceProvider</c> / <c>IServiceScopeFactory</c>.
/// Task 061 removed exactly that ambient reach from <see cref="Chat.SessionFilesCleanupJob"/> on the
/// grounds that a service locator inside a component whose job is to DELETE things is a reach, not a
/// boundary — and this job is the first component that genuinely CAN delete durable bytes, so the same
/// reasoning binds harder here. After construction, <see cref="SessionFileRetentionJob"/> can reach
/// exactly two things: the durable store, and this delegate.
/// </remarks>
/// <param name="tenantId">Tenant that owns the session (parsed from the blob's first path segment).</param>
/// <param name="sessionId">Session id (parsed from the blob name).</param>
/// <param name="cancellationToken">Cancellation token.</param>
public delegate Task<SessionRetentionProbe> SessionRetentionProbeDelegate(
    string tenantId,
    string sessionId,
    CancellationToken cancellationToken);

/// <summary>Per-pass counters, returned so tests can assert on a pass and telemetry can report one.</summary>
/// <param name="BlobsExamined">Durable blobs enumerated.</param>
/// <param name="SessionsProbed">Distinct sessions a Cosmos probe was actually spent on.</param>
/// <param name="BlobsDeleted">Blobs deleted (always 0 in dry-run mode).</param>
/// <param name="BlobsRetainedIndefinitely">Blobs kept because their session is FILED (<c>Ttl == -1</c>).</param>
/// <param name="BlobsRetainedIndeterminate">Blobs kept because the question could not be answered.</param>
/// <param name="DeleteFailures">Deletes that threw. The pass continues; the blob is retried next pass.</param>
public sealed record SessionFileRetentionPassResult(
    int BlobsExamined,
    int SessionsProbed,
    int BlobsDeleted,
    int BlobsRetainedIndefinitely,
    int BlobsRetainedIndeterminate,
    int DeleteFailures)
{
    internal static readonly SessionFileRetentionPassResult Empty = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// spaarkeai-compose-r8 FR-B04 (task 062) — the expiry pass that makes a durable session file's
/// lifetime follow its SESSION's retention: the 90-day container default for unfiled sessions, and
/// INDEFINITE for filed ones (<see cref="StoredSession.Ttl"/> = <see cref="StoredSession.NeverExpireTtl"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pass exists at all, and why it is driven from the BLOB side.</b> Blobs have no TTL of their
/// own. The Cosmos <c>sessions</c> container has one (90 days, per-item overridable), so the manifest
/// can expire while the bytes persist. A retention rule that walked the manifest would therefore be
/// blind to precisely the population it exists to reap — once the manifest is gone, nothing in Cosmos
/// names the blobs or even names the tenants that own them. Only the blob namespace still records them,
/// which is why this pass enumerates
/// <see cref="SessionFileBlobStore.ListAllForRetentionAsync"/> and asks Cosmos about each session it
/// finds, rather than the other way round. The same reasoning is why task 063's erasure must enumerate
/// by tenant PREFIX rather than by manifest.
/// </para>
/// <para>
/// <b>What it will and will not delete.</b> The decision is
/// <see cref="SessionFileRetentionPolicy.Evaluate"/>'s, and only
/// <see cref="SessionFileRetentionVerdict.Expired"/> — a definitive Cosmos 404 for the session PLUS a
/// blob older than <see cref="SessionFileRetentionPolicy.DefaultRetentionWindow"/> — permits one. A
/// filed session short-circuits on the <c>-1</c> sentinel before any arithmetic runs. A probe that
/// could not answer retains. This job re-checks the verdict itself before calling delete, so the
/// deletable case is stated twice, in two files.
/// </para>
/// <para>
/// <b>Cost control.</b> Blobs are grouped by <c>(tenant, session)</c> and a session is probed at most
/// ONCE per pass. A session whose every blob is younger than the retention window is not probed at all
/// — it cannot produce a deletable verdict, so the Cosmos read would be wasted. In steady state (most
/// files belong to live or recent sessions) the pass is one listing plus a small number of point reads.
/// </para>
/// <para>
/// <b>Inert unless the store is enabled.</b> <c>SessionFileStore:BlobEndpoint</c> ships EMPTY and must
/// stay empty until task 063 merges, so this job's <see cref="ExecuteAsync"/> logs once and returns
/// without starting a timer. That is also its kill switch: it inherits the feature's existing gate
/// rather than adding a flag (ADR-018 — same choice <see cref="Chat.SessionFilesCleanupJob"/> made).
/// </para>
/// <para>
/// <b>Operator escape hatch.</b> <c>SessionFileStore:RetentionSweepDryRun</c> makes the pass evaluate
/// and log every verdict while deleting nothing. Recommended for the first passes after the store is
/// first enabled in an environment, because a wrong retention rule is discovered as missing data.
/// </para>
/// <para>
/// <b>Placement (root CLAUDE.md §10).</b> In the BFF, under <c>Services/Ai/Sessions/</c>, beside the
/// store it sweeps and inside the AI boundary (ADR-013) — no CRUD code touches it, so no
/// <c>PublicContracts/</c> facade is needed. ADR-001: <see cref="BackgroundService"/> +
/// <see cref="PeriodicTimer"/>, mirroring <see cref="Chat.SessionFilesCleanupJob"/> — no Azure
/// Function, no Hangfire/Quartz, no new job framework. No new package, no new Azure resource.
/// </para>
/// <para>
/// <b>§11 three-question gate.</b> (1) <i>Existing overlap</i> — none.
/// <see cref="Chat.SessionFilesCleanupJob"/> is the only adjacent sweeper and it is the WRONG one: it
/// evicts the hot AI-Search index on a 24h Redis-key signal, and task 061 deliberately made it
/// structurally incapable of reaching durable bytes (enforced by
/// <c>tests/Spaarke.ArchTests/SessionFilesCleanupScopeTests.cs</c>). Adding retention to it would undo
/// exactly the property 061 was sequenced to establish. <c>ScheduledJobHost</c> (the generic cron
/// framework) was considered: it costs TWO registrations plus a seeded definition and stores run
/// history in an explicitly-interim <c>InMemoryBackgroundJobStore</c>, so it is more surface, not less.
/// (2) <i>Extend instead?</i> — see above; the one extendable candidate fails on the safety property.
/// (3) <i>Cost of doing nothing</i> — durable bytes would accumulate with no expiry at all: ADR-015's
/// "MUST define retention and deletion behavior for stored outputs" stays unmet, and the mechanical
/// gate holding <c>BlobEndpoint</c> empty could never be lifted, which makes tasks 060 and 061 dead
/// code in every deployed environment.
/// </para>
/// </remarks>
public sealed class SessionFileRetentionJob : BackgroundService
{
    /// <summary>How often the sweep runs when no interval is configured.</summary>
    /// <remarks>
    /// Daily, not hourly. Retention moves on a 90-day scale, the pass costs a full container listing,
    /// and nothing user-visible depends on its latency — the hot-index sweep
    /// (<see cref="Chat.SessionFilesCleanupJob"/>, 6h) is the one with a cost reason to run often.
    /// </remarks>
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

    /// <summary>Configuration key overriding <see cref="DefaultInterval"/> (hours; clamped to [1, 168]).</summary>
    public const string IntervalHoursConfigKey = "SessionFileStore:RetentionSweepIntervalHours";

    /// <summary>Configuration key that makes the sweep evaluate and log without deleting.</summary>
    public const string DryRunConfigKey = "SessionFileStore:RetentionSweepDryRun";

    /// <summary>
    /// Telemetry event name emitted once per pass. Low-cardinality fields only (counts + mode).
    /// </summary>
    internal const string TelemetryEventName = "r8.session_file_retention.run";

    private readonly SessionFileBlobStore _durableStore;
    private readonly SessionRetentionProbeDelegate _probeSessionRetention;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly bool _dryRun;
    private readonly TimeSpan _retentionWindow;
    private readonly ILogger<SessionFileRetentionJob> _logger;

    public SessionFileRetentionJob(
        SessionFileBlobStore durableStore,
        SessionRetentionProbeDelegate probeSessionRetention,
        IConfiguration configuration,
        ILogger<SessionFileRetentionJob> logger)
        : this(
            durableStore,
            probeSessionRetention,
            ResolveInterval(configuration),
            ResolveDryRun(configuration),
            SessionFileRetentionPolicy.DefaultRetentionWindow,
            TimeProvider.System,
            logger)
    {
    }

    /// <summary>
    /// Test constructor — substitutes the clock, the cadence and the retention window so a pass can be
    /// driven deterministically. The decision logic, the store and the probe are the production ones.
    /// </summary>
    internal SessionFileRetentionJob(
        SessionFileBlobStore durableStore,
        SessionRetentionProbeDelegate probeSessionRetention,
        TimeSpan interval,
        bool dryRun,
        TimeSpan retentionWindow,
        TimeProvider timeProvider,
        ILogger<SessionFileRetentionJob> logger)
    {
        _durableStore = durableStore ?? throw new ArgumentNullException(nameof(durableStore));
        _probeSessionRetention = probeSessionRetention ?? throw new ArgumentNullException(nameof(probeSessionRetention));
        _interval = interval;
        _dryRun = dryRun;
        _retentionWindow = retentionWindow;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_durableStore.IsEnabled)
        {
            // The store is the feature's own kill switch (ADR-018 — no new flag). With no blob endpoint
            // there is nothing to sweep and nothing that could be deleted.
            _logger.LogInformation(
                "Session-file retention sweep is not running: the durable store is disabled " +
                "('{Key}' is not configured).",
                SessionFileBlobStore.BlobEndpointConfigKey);
            return;
        }

        _logger.LogInformation(
            "Session-file retention sweep started. IntervalHours={IntervalHours}, RetentionDays={RetentionDays}, DryRun={DryRun}",
            _interval.TotalHours, _retentionWindow.TotalDays, _dryRun);

        using var timer = new PeriodicTimer(_interval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed pass must never take the host down — the next tick retries, and every
                // undeleted blob is simply retained, which is the safe direction.
                _logger.LogError(ex, "Session-file retention pass failed. The next scheduled pass will retry.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one expiry pass. Exposed to tests so the retention behaviour is asserted against the real
    /// pass rather than a re-implementation of it.
    /// </summary>
    internal async Task<SessionFileRetentionPassResult> RunPassAsync(CancellationToken cancellationToken)
    {
        if (!_durableStore.IsEnabled)
        {
            return SessionFileRetentionPassResult.Empty;
        }

        var stopwatch = Stopwatch.StartNew();
        var now = _timeProvider.GetUtcNow();

        // Group first, then decide. One Cosmos probe per SESSION, not per blob — a 20-file session
        // costs one point read, and a session whose files are all young costs none at all.
        var bySession = new Dictionary<(string TenantId, string SessionId), List<SessionFileBlobRef>>();
        var examined = 0;

        await foreach (var blob in _durableStore.ListAllForRetentionAsync(cancellationToken).ConfigureAwait(false))
        {
            examined++;

            var key = (blob.TenantId, blob.SessionId);
            if (!bySession.TryGetValue(key, out var blobs))
            {
                blobs = [];
                bySession[key] = blobs;
            }

            blobs.Add(blob);
        }

        var probed = 0;
        var deleted = 0;
        var retainedIndefinitely = 0;
        var retainedIndeterminate = 0;
        var deleteFailures = 0;

        foreach (var ((tenantId, sessionId), blobs) in bySession)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Cheap pre-filter, NOT a retention decision. A session with no blob old enough to be past
            // the window cannot produce an Expired verdict for any of its blobs whatever Cosmos says,
            // so the probe would be pure cost. Anything that skips here is retained.
            if (!blobs.Any(b => IsPastRetentionWindow(b, now)))
            {
                continue;
            }

            probed++;
            var probe = await SafeProbeAsync(tenantId, sessionId, cancellationToken).ConfigureAwait(false);

            foreach (var blob in blobs)
            {
                var verdict = SessionFileRetentionPolicy.Evaluate(probe, blob.CreatedOn, now, _retentionWindow);

                switch (verdict)
                {
                    case SessionFileRetentionVerdict.RetainIndefinitely:
                        retainedIndefinitely++;
                        continue;

                    case SessionFileRetentionVerdict.RetainIndeterminate:
                        retainedIndeterminate++;
                        continue;

                    case SessionFileRetentionVerdict.RetainWhileSessionLives:
                    case SessionFileRetentionVerdict.RetainWithinRetentionWindow:
                        continue;

                    case SessionFileRetentionVerdict.Expired:
                        break;

                    default:
                        // A verdict this job does not understand is a RETAIN. A new enum member must
                        // never become deletable by default.
                        retainedIndeterminate++;
                        continue;
                }

                // Stated twice, in two files, deliberately: the only condition under which durable user
                // bytes are destroyed is a definitive session-absent probe plus an aged blob.
                if (verdict != SessionFileRetentionVerdict.Expired
                    || probe.State != SessionRetentionState.Absent
                    || SessionFileRetentionPolicy.IsIndefiniteTtl(probe.Ttl))
                {
                    retainedIndeterminate++;
                    continue;
                }

                if (_dryRun)
                {
                    _logger.LogInformation(
                        "Session-file retention (DRY RUN) would delete an expired durable copy. " +
                        "TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}, BlobCreatedOn={CreatedOn}",
                        tenantId, sessionId, blob.FileId, blob.CreatedOn);
                    continue;
                }

                try
                {
                    if (await _durableStore.DeleteAsync(tenantId, sessionId, blob.FileId, cancellationToken).ConfigureAwait(false))
                    {
                        deleted++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    deleteFailures++;
                    _logger.LogWarning(ex,
                        "Session-file retention could not delete an expired durable copy — it is retained " +
                        "and will be retried on the next pass. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                        tenantId, sessionId, blob.FileId);
                }
            }
        }

        stopwatch.Stop();

        var result = new SessionFileRetentionPassResult(
            BlobsExamined: examined,
            SessionsProbed: probed,
            BlobsDeleted: deleted,
            BlobsRetainedIndefinitely: retainedIndefinitely,
            BlobsRetainedIndeterminate: retainedIndeterminate,
            DeleteFailures: deleteFailures);

        // ADR-015: counts and identifiers only, never content. Retained-indefinitely is reported
        // explicitly so "filed sessions are being kept" is an observable fact, not an assumption.
        _logger.LogInformation(
            "{Event}: examined={Examined}, sessionsProbed={Probed}, deleted={Deleted}, " +
            "retainedIndefinitely={RetainedIndefinitely}, retainedIndeterminate={RetainedIndeterminate}, " +
            "deleteFailures={DeleteFailures}, dryRun={DryRun}, durationMs={DurationMs}",
            TelemetryEventName, examined, probed, deleted, retainedIndefinitely, retainedIndeterminate,
            deleteFailures, _dryRun, stopwatch.ElapsedMilliseconds);

        return result;
    }

    private bool IsPastRetentionWindow(SessionFileBlobRef blob, DateTimeOffset now)
        => blob.CreatedOn is { } createdOn && now - createdOn >= _retentionWindow;

    private async Task<SessionRetentionProbe> SafeProbeAsync(
        string tenantId, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _probeSessionRetention(tenantId, sessionId, cancellationToken).ConfigureAwait(false)
                   ?? SessionRetentionProbe.Indeterminate;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Session-file retention probe threw for session {SessionId} (tenant={TenantId}) — " +
                "treating as Indeterminate, which RETAINS.",
                sessionId, tenantId);
            return SessionRetentionProbe.Indeterminate;
        }
    }

    private static TimeSpan ResolveInterval(IConfiguration configuration)
    {
        var configured = configuration?[IntervalHoursConfigKey];
        if (double.TryParse(configured, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hours)
            && hours > 0)
        {
            // Clamped: an interval of minutes would hammer the listing for no benefit, and one longer
            // than a week would let expired bytes linger past any reasonable audit window.
            return TimeSpan.FromHours(Math.Clamp(hours, 1, 168));
        }

        return DefaultInterval;
    }

    private static bool ResolveDryRun(IConfiguration configuration)
        => bool.TryParse(configuration?[DryRunConfigKey], out var dryRun) && dryRun;
}
