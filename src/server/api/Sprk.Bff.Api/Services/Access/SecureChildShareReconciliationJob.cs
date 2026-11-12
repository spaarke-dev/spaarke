using System.Text.Json;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 149 (C10 part 2, sharees) — every two minutes, brings every child of every secure record
/// into line with its root's shares (<see cref="SecureChildShareSynchronizer.ReconcileAllAsync"/>).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The share endpoints fan out to the children in the request, but four writers of a secure
/// child's access never pass through them:
/// <list type="bullet">
/// <item>a CHILD CREATED or RE-FILED under a secure record — about thirty BFF writer sites (task 146's census), the client
/// writers task 147 routes, and everything still to come; the ownership resolver decides the owner before the row exists,
/// so it cannot mirror;</item>
/// <item>the model-driven app's OWN Share and Unshare on a secure root (the Share privilege is held at Deep by Spaarke Core
/// User and Spaarke Office Add In User; owner C4 keeps it). An Unshare that does not reach the children is an over-share
/// for as long as nothing repairs it;</item>
/// <item>a fan-out that partly failed (the endpoint names the counts; this completes it);</item>
/// <item>any other root-share writer: task 143's No Access enforcer calls the synchronizer after it removes a root share
/// (wired in task 149 r3), and task 142's Assigned-To materializer calls it after a confirmed root share write (the binding
/// 142 x 149 merge-order obligation, notes/task-149 §14, discharged at the batch 4 integration); this job completes what
/// either could not.</item>
/// </list>
/// No relationship cascades Share/Unshare/Reparent (live metadata, 2026-10-02), so without this job none of the four would
/// ever reach the children. The cadence bounds every one of them to about two minutes — the owner's "minutes, never hourly"
/// (round 3, R3/R4) — and it is the "reconcile(root) runs on a schedule" condition the task's deployment gate names: tasks
/// 146 and 149 may reach a shared environment only with it enabled.</para>
/// <para><b>Writes are on.</b> Unlike the report-only jobs of tasks 137 and 141, this job IS the mechanism: report-only would
/// leave every new child invisible to the people shared on its root. Everything it writes is bounded by the root's own
/// shares (never wider), and a read it cannot complete writes nothing for the rows it concerns. The admin
/// <c>/api/admin/jobs/{jobId}/disable</c> endpoint stops it (per instance, ADR-036 A1 §2).</para>
/// <para><b>ADR-036 A1.</b> Rule 3: the unit of work is a child's share set, and the synchronizer is idempotent (it writes
/// only the difference and reads it back), so a repeated tick is harmless and no claim marker is needed. Rule 4: a run that
/// could not read the children (status Failed) throws after the heartbeat, because a retry this tick can still do the work;
/// a run that mirrored some children and not others returns <c>Success = false</c> — the next tick revisits every child.
/// Rule 5: one heartbeat per attempt. Rule 6: registered through <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — BFF identity, BFF domain code (the
/// same synchronizer the share endpoints call), low volume (one query per child table plus batched share reads), and the
/// scheduler's lease gives one dispatch per tick. No package, store or interface.</para>
/// <para><b>Direct 1:1 messages (uac-r2 task 198 Part C).</b> After the secure children, each run brings every message owned by
/// the Direct-message owner team (fm task 054a) into line with its thread's participants
/// (<see cref="DirectMessageParticipantShares"/>): the participants the Restricted bar allows get Read on the message and its
/// content rows, and no other user keeps a direct share on the message. This is the durable re-grant: a grant that did not land,
/// a Direct team or Secure team that could not be told, content rows created after the grant, and an unfile or secure exit
/// back to the Direct team all leave the ROW in a state this pass finds — no queue, column or marker is written. Each run takes,
/// up to <see cref="MaxDirectMessagesPerRun"/> messages: the ones an earlier run left incomplete (carried, per instance), the
/// Direct-team messages and content rows modified since this pass's watermark, then the next slice of a rolling sweep over every
/// Direct-team message in id order (the backstop that survives a restart). A capped run logs it and carries the rest. A carry past
/// <see cref="MaxDirectCarried"/> holds the watermark, as a listing or sweep fault does, so the dropped messages are listed again;
/// a listed message this instance finished after its latest listed change is then skipped (#2731), so a held window re-lists
/// without re-working what is done, and the carry drains. Writes are
/// on, like the secure mirror; there is no report-only switch on this job, and no new setting. When the Direct team is not
/// configured the pass does nothing; when it cannot be resolved it writes nothing and the run is not a success.</para>
/// </remarks>
public sealed partial class SecureChildShareReconciliationJob : IScheduledJob
{
    /// <summary>Task 198 Part C: the most Direct-team messages one run reconciles; the rest are carried to the next run.</summary>
    internal const int MaxDirectMessagesPerRun = 200;

    /// <summary>Task 198 Part C: the most rows one recent-changes listing reads per table; past it the watermark is held.</summary>
    internal const int MaxDirectListedPerTable = 500;

    /// <summary>Task 198 Part C: the rolling sweep's largest slice per run (within <see cref="MaxDirectMessagesPerRun"/>).</summary>
    internal const int DirectSweepSlice = 100;

    /// <summary>Fix round 1 R4: the part of every run's budget kept for the sweep, however many messages are carried or recent.</summary>
    internal const int DirectSweepReserve = 50;

    /// <summary>Task 198 Part C: the most messages carried between runs; past it the watermark is held (#2731: and the messages
    /// already finished are skipped when the held window is listed again).</summary>
    internal const int MaxDirectCarried = 1000;

    /// <summary>#2731: the most finished messages remembered for the skip while a window is held; past it the oldest are
    /// forgotten (and worked again if listed). Bounds the per-instance state however long a window stays held.</summary>
    internal const int MaxDirectFinishedRemembered = 5000;

    /// <summary>Fix round 1 R4: how many runs in a row one message may stay incomplete before it is backed off.</summary>
    internal const int MaxDirectAttempts = 3;

    /// <summary>Fix round 1 R4: how long a backed-off message waits before its next attempt (logged as an error each time).</summary>
    internal static readonly TimeSpan DirectBackoff = TimeSpan.FromMinutes(30);

    /// <summary>Task 198 Part C: how far back the first run on an instance looks for recently changed Direct messages.</summary>
    internal static readonly TimeSpan DirectInitialLookback = TimeSpan.FromMinutes(60);

    /// <summary>Task 198 Part C: each run's watermark is its start minus this overlap (clock skew, in-flight writes).</summary>
    internal static readonly TimeSpan DirectOverlap = TimeSpan.FromMinutes(1);

    // Task 198 Part C: the Direct pass's watermark, carried messages and sweep cursor — per instance, in this singleton, as the
    // sibling jobs keep theirs (ADR-052 §5). A restart loses them; the rolling sweep then reaches every Direct message again.
    private readonly object _directGate = new();
    private DateTimeOffset? _directWatermark;
    private readonly HashSet<Guid> _directPending = new();
    private Guid? _directSweepCursor;

    // Fix round 1 R4: per carried message, how many runs in a row it stayed incomplete, and (once backed off) when it is next tried.
    private readonly Dictionary<Guid, (int Failures, DateTimeOffset? RetryAt)> _directAttempts = new();

    // #2731: per message reconciled complete, the start of the run that finished it. A held window is listed again; a message
    // finished after its latest listed change is skipped. Only listed messages are recorded; entries are pruned as the watermark
    // passes them and capped at MaxDirectFinishedRemembered, so it stays bounded however long a window is held.
    private readonly Dictionary<Guid, DateTimeOffset> _directFinishedAt = new();

    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "secure-child-share-reconciliation";

    /// <summary>Every two minutes: the window in which a new child, a re-file or an MDA Share/Unshare is not yet mirrored.</summary>
    internal const string DefaultCronSchedule = "*/2 * * * *";

    // The synchronizer is resolved per run from a scope, as the sibling jobs resolve their Dataverse seams: constructing the
    // job — which the scheduler's registry does at startup — then needs nothing beyond the framework.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecureChildShareReconciliationJob> _logger;

    public SecureChildShareReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SecureChildShareReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Secure Child Share Reconciliation";

    /// <inheritdoc />
    public string Description =>
        "Shares every child of a secure project, matter or work assignment with exactly the internal users and teams its " +
        "root is shared with (never Assign, never wider; Share only where the root share holds it), and removes every other share on it. Catches new and " +
        "re-filed children and model-driven-app Share/Unshare of a secure root (task 149).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var startedAt = _timeProvider.GetUtcNow();
        SecureChildShareSyncResult result;
        DirectMessageRun direct;
        ExternalTeamRun external;
        using (var scope = _scopeFactory.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>()
                .ReconcileAllAsync(cancellationToken).ConfigureAwait(false);

            // Task 198 Part C: the Direct-team messages, after the secure children. Its own window; never throws.
            direct = await ReconcileDirectMessagesAsync(scope.ServiceProvider, startedAt, context.RunId, cancellationToken)
                .ConfigureAwait(false);

            // uac-r2 task 191c: the External-team share, after the Direct pass. Its own window; never throws for a Dataverse fault.
            external = await ReconcileExternalTeamAsync(scope.ServiceProvider, startedAt, context.RunId, cancellationToken)
                .ConfigureAwait(false);
        }
        var duration = _timeProvider.GetElapsedTime(started);
        var complete = result.IsComplete && direct.Ok && external.Ok;

        // Task 191c: the External-team pass's own heartbeat line (counts only; row ids are in the per-table report lines).
        _logger.Log(
            external.Clean ? LogLevel.Information : LogLevel.Warning,
            "[EXTERNAL-TEAM-SHARE] heartbeat status={Status} writes={Writes} reownWrites={ReownWrites} carriedIn={CarriedIn} " +
            "listed={Listed} swept={Swept} examined={Examined} undecided={Undecided} teamGrants={TeamGrants} teamChanges={TeamChanges} " +
            "teamRevokes={TeamRevokes} flaggedUserRevokes={FlaggedUserRevokes} reowns={Reowns} written={Written} reowned={Reowned} " +
            "externalTeamOwned={ExternalTeamOwned} directOwnedFlaggedShares={DirectOwnedFlagged} carried={Carried} " +
            "sweepPassComplete={SweepPassComplete} failure={Failure} runId={RunId}",
            external.Status, external.WritesEnabled, external.ReownWritesEnabled, external.CarriedIn, external.Listed, external.Swept,
            external.Examined, external.Undecided, external.TeamGrants, external.TeamChanges, external.TeamRevokes,
            external.FlaggedUserRevokes, external.Reowns, external.Written, external.Reowned, external.ExternalTeamOwned,
            external.DirectOwnedFlaggedShares, external.Carried, external.SweepPassComplete, external.Failure, context.RunId);

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            complete ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-SHARES] heartbeat status={Status} inScope={InScope} updated={Updated} unchanged={Unchanged} " +
            "notUpdated={NotUpdated} held={Held} outsideSecureRoots={Outside} granted={Granted} changed={Changed} " +
            "revoked={Revoked} hardDenyWiderThanRead={HardDenyWiderThanRead} hardDenyUnchecked={HardDenyUnchecked} " +
            "hardDenyNarrowFailed={HardDenyNarrowFailed} detail={Detail} directStatus={DirectStatus} " +
            "directExamined={DirectExamined} directGranted={DirectGranted} directRevoked={DirectRevoked} " +
            "directIncomplete={DirectIncomplete} directCarried={DirectCarried} directCapReached={DirectCapReached} " +
            "attempt={Attempt} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            result.Status, result.ChildrenInScope, result.ChildrenUpdated, result.ChildrenUnchanged,
            result.ChildrenNotUpdated, result.ChildrenHeld, result.ChildrenOutsideSecureRoots, result.SharesGranted,
            result.SharesChanged, result.SharesRevoked, result.HardDenyWiderThanRead, result.HardDenyUnchecked,
            result.HardDenyNarrowFailed, result.Detail, direct.Status, direct.Examined, direct.Granted, direct.Revoked,
            direct.Incomplete, direct.Carried, direct.CapReached,
            context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        if (result.Status == SecureChildShareSyncStatus.Failed)
        {
            // ADR-036 A1 rule 4: nothing was decided, and a retry this tick can still do the work.
            throw new InvalidOperationException(
                $"The secure-child share reconciliation could not run: {result.Detail}. Nothing was written.");
        }

        return new JobRunResult(
            Success: complete,
            ErrorMessage: complete
                ? null
                : $"{result.ChildrenNotUpdated} child(ren) not updated and {result.ChildrenHeld} held"
                  + (result.HardDenyNarrowFailed > 0 || result.HardDenyUnchecked > 0
                      ? $"; {result.HardDenyNarrowFailed} hard-deny row(s) not narrowed to Read and " +
                        $"{result.HardDenyUnchecked} unchecked"
                      : "")
                  + (direct.Ok
                      ? ""
                      : $"; Direct messages: {direct.Status}, {direct.Incomplete} not complete, {direct.BackedOff} backed off" +
                        (direct.ListingCapped ? ", a recent-changes listing capped" : "") +
                        (direct.Failure is null ? "" : $" ({direct.Failure})"))
                  + (external.Ok
                      ? ""
                      : $"; External team: {external.Status}, {external.Failures} row(s) carried for a fault, {external.Carried} carried in all" +
                        (external.Failure is null ? "" : $" ({external.Failure})"))
                  + "; the next run retries.",
            ProcessedItems: result.ChildrenInScope + direct.Examined + external.Examined,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                status = result.Status.ToString(),
                inScope = result.ChildrenInScope,
                updated = result.ChildrenUpdated,
                unchanged = result.ChildrenUnchanged,
                notUpdated = result.ChildrenNotUpdated,
                held = result.ChildrenHeld,
                outsideSecureRoots = result.ChildrenOutsideSecureRoots,
                granted = result.SharesGranted,
                changed = result.SharesChanged,
                revoked = result.SharesRevoked,
                hardDenyWiderThanRead = result.HardDenyWiderThanRead,
                hardDenyUnchecked = result.HardDenyUnchecked,
                hardDenyNarrowFailed = result.HardDenyNarrowFailed,
                detail = result.Detail,
                // Task 198 Part C: the Direct-team messages brought into line with their thread's participants.
                directMessages = new
                {
                    status = direct.Status,
                    since = direct.Since?.UtcDateTime,
                    carriedIn = direct.CarriedIn,
                    listed = direct.Listed,
                    skippedFinished = direct.SkippedFinished,
                    swept = direct.Swept,
                    examined = direct.Examined,
                    granted = direct.Granted,
                    revoked = direct.Revoked,
                    incomplete = direct.Incomplete,
                    carried = direct.Carried,
                    capReached = direct.CapReached,
                    listingCapped = direct.ListingCapped,
                    backedOff = direct.BackedOff,
                    sweepPassComplete = direct.SweepPassComplete,
                    incompleteMessages = direct.IncompleteIds.Take(50).ToArray(),
                    failure = direct.Failure,
                },
                // Task 191c: the External Collaborators team share (planned when its writes are off, made when on).
                externalTeam = new
                {
                    status = external.Status,
                    writesEnabled = external.WritesEnabled,
                    reownWritesEnabled = external.ReownWritesEnabled,
                    carriedIn = external.CarriedIn,
                    listed = external.Listed,
                    swept = external.Swept,
                    examined = external.Examined,
                    undecided = external.Undecided,
                    teamGrants = external.TeamGrants,
                    teamChanges = external.TeamChanges,
                    teamRevokes = external.TeamRevokes,
                    flaggedUserRevokes = external.FlaggedUserRevokes,
                    reowns = external.Reowns,
                    written = external.Written,
                    reowned = external.Reowned,
                    externalTeamOwned = external.ExternalTeamOwned,
                    directOwnedFlaggedShares = external.DirectOwnedFlaggedShares,
                    carried = external.Carried,
                    failures = external.Failures,
                    leftToSweep = external.LeftToSweep,
                    sweepPassComplete = external.SweepPassComplete,
                    failure = external.Failure,
                },
                attempt = context.Attempt,
            }));
    }

    /// <summary>What the Direct-message pass did in one run (task 198 Part C).</summary>
    internal sealed record DirectMessageRun(string Status)
    {
        internal const string StatusOk = "ok";
        internal const string StatusNotConfigured = "not-configured";
        internal const string StatusUnavailable = "unavailable";
        internal const string StatusTeamUnknown = "direct-team-unknown";
        internal const string StatusListingFailed = "listing-failed";

        public DateTimeOffset? Since { get; init; }
        public int CarriedIn { get; init; }
        public int Listed { get; init; }

        /// <summary>#2731: listed messages not worked because this instance finished them after their latest listed change.</summary>
        public int SkippedFinished { get; init; }

        public int Swept { get; init; }
        public int Examined { get; init; }
        public int Granted { get; init; }
        public int Revoked { get; init; }
        public int Incomplete { get; init; }
        public int Carried { get; init; }
        public bool CapReached { get; init; }

        /// <summary>Fix round 1 R4: a recent-changes listing reached its cap this run (more changed rows wait).</summary>
        public bool ListingCapped { get; init; }

        /// <summary>Fix round 1 R4: carried messages not tried this run because they are backed off.</summary>
        public int BackedOff { get; init; }

        public bool SweepPassComplete { get; init; }
        public IReadOnlyList<Guid> IncompleteIds { get; init; } = [];
        public string? Failure { get; init; }

        /// <summary>
        /// Nothing undecided: not configured, unavailable (no lookup registered), or a pass that finished every message it tried,
        /// listed every changed row (no capped listing) and holds no backed-off message.
        /// </summary>
        public bool Ok => Status is StatusNotConfigured or StatusUnavailable
            || (Status == StatusOk && Incomplete == 0 && Failure is null && !ListingCapped && BackedOff == 0);
    }

    /// <summary>
    /// Task 198 Part C — the Direct-team messages: carried, recently changed (the message or a content row), then the rolling
    /// sweep; at most <see cref="MaxDirectMessagesPerRun"/>, each through <see cref="DirectMessageParticipantShares.ReconcileAsync"/>.
    /// Fix round 1 R4: the listings are ordered by <c>modifiedon</c> and a capped listing moves the watermark only to its last row;
    /// <see cref="DirectSweepReserve"/> of every run is the sweep's; a message incomplete <see cref="MaxDirectAttempts"/> runs in a
    /// row is backed off for <see cref="DirectBackoff"/> (an error log each time), so it cannot fill the budget. #2731: the recent
    /// messages are worked oldest change first across every table; the carry keeps the incomplete, then the backed-off, then the
    /// left-over messages, so a carry past its cap drops the newest listed changes and holds the watermark. A listed message this
    /// instance finished after its latest listed change (less <see cref="DirectOverlap"/>) is skipped, never a carried one; the
    /// finished record is pruned as the watermark passes it. Never throws (cancellation aside).
    /// </summary>
    private async Task<DirectMessageRun> ReconcileDirectMessagesAsync(
        IServiceProvider services, DateTimeOffset startedAt, Guid runId, CancellationToken ct)
    {
        var lookup = services.GetService<Sprk.Bff.Api.Infrastructure.Dataverse.DirectMessageOwnerTeam>();
        var dataverse = services.GetService<Spaarke.Dataverse.IGenericEntityService>();
        var shares = services.GetService<IDataverseRecordShareService>();
        var configuration = services.GetService<IConfiguration>();
        if (lookup is null || dataverse is null || shares is null || configuration is null)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: the Direct-message lookup or a Dataverse seam is not registered; the " +
                "Direct-message pass did not run.", runId);
            return new DirectMessageRun(DirectMessageRun.StatusUnavailable);
        }

        DateTimeOffset since;
        List<Guid> carried;
        Guid? sweepAfter;
        Dictionary<Guid, (int Failures, DateTimeOffset? RetryAt)> attempts;
        Dictionary<Guid, DateTimeOffset> finishedAt;
        lock (_directGate)
        {
            // #2709: the first run's window start is stored before anything can fail. A run that holds the watermark (a
            // listing or sweep fault, a carry past its cap, an unresolved Direct team) then leaves the next run listing from
            // here; recomputing the look-back from each later run's own start would slide the window past what was never listed.
            _directWatermark ??= startedAt - DirectInitialLookback;
            since = _directWatermark.Value;
            carried = _directPending.OrderBy(id => id).ToList();
            sweepAfter = _directSweepCursor;
            attempts = new Dictionary<Guid, (int, DateTimeOffset?)>(_directAttempts);
            finishedAt = new Dictionary<Guid, DateTimeOffset>(_directFinishedAt);
        }

        // The Direct team, by its RESOLVED id only (K3: a refusal carries no id and lists nothing).
        Sprk.Bff.Api.Infrastructure.Dataverse.DirectMessageOwnerTeamResolution resolution;
        try
        {
            resolution = await lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            resolution = new Sprk.Bff.Api.Infrastructure.Dataverse.DirectMessageOwnerTeamResolution(null, "unreadable", ex);
        }

        if (resolution.IsNotConfigured && resolution.Fault is null)
            return new DirectMessageRun(DirectMessageRun.StatusNotConfigured);
        if (!resolution.IsResolved || resolution.TeamId is not { } team || team == Guid.Empty)
        {
            // Nothing can be listed or granted; what is carried stays carried and the watermark stays.
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: the Direct-message owner team cannot be resolved ({Refusal}); no Direct " +
                "message is granted this run, and {Carried} carried message(s) wait for the next.", runId, resolution.Refusal,
                carried.Count);
            return new DirectMessageRun(DirectMessageRun.StatusTeamUnknown)
            {
                Since = since,
                CarriedIn = carried.Count,
                Carried = carried.Count,
                Incomplete = carried.Count,
                Failure = "the Direct-message owner team cannot be resolved",
            };
        }

        bool BackedOff(Guid id) => attempts.TryGetValue(id, out var a) && a.RetryAt is { } retryAt && retryAt > startedAt;

        // ── Listing: recently changed messages and content rows, oldest change first ──
        // #2731: each message's earliest and latest change in the window, across its own row and its content rows. The recent
        // messages are worked oldest change first across every table, so a carry past its cap drops the NEWEST changes; the
        // latest change decides whether a message this instance already finished needs working again (below).
        var firstChange = new Dictionary<Guid, DateTimeOffset>();
        var lastChange = new Dictionary<Guid, DateTimeOffset>();
        var horizons = new List<DateTimeOffset?>();
        string? failure = null;
        try
        {
            var listings = new List<(string Table, string? Lookup)> { (DirectMessageParticipantShares.CommunicationTable, null) };
            listings.AddRange(DirectMessageParticipantShares.ContentRows.Select(c => (c.Table, (string?)c.MessageLookup)));
            foreach (var (table, messageLookup) in listings)
            {
                var (rows, horizon, capped) = await ListAsync(dataverse, table, messageLookup, team, since, ct).ConfigureAwait(false);
                foreach (var (id, changedAt) in rows)
                {
                    // A row without modifiedon (Dataverse always returns it) is the earliest change for ordering and the latest
                    // for the skip, so it is worked early and never skipped: missing data fails toward working the message.
                    var earliest = changedAt ?? since;
                    var latest = changedAt ?? DateTimeOffset.MaxValue;
                    if (!firstChange.TryGetValue(id, out var first) || earliest < first)
                        firstChange[id] = earliest;
                    if (!lastChange.TryGetValue(id, out var last) || latest > last)
                        lastChange[id] = latest;
                }

                if (capped)
                    horizons.Add(horizon);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failure = "the recently changed Direct messages could not be listed";
            _logger.LogError(ex, "[DIRECT-SHARES] run={RunId}: the recently changed Direct messages could not be listed; the window is " +
                "listed again next run.", runId);
        }

        var recent = firstChange.OrderBy(c => c.Value).ThenBy(c => c.Key).Select(c => c.Key).ToList();
        var listingCapped = horizons.Count > 0;
        if (listingCapped)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Capped} recent-changes listing(s) reached the cap of {Cap}; the watermark " +
                "moves at most to the last row listed, so the rest is listed by the next runs.", runId, horizons.Count,
                MaxDirectListedPerTable);
        }

        // #2731: a held window is listed again, so a recent message this instance finished after its latest listed change (with the
        // overlap's margin) needs nothing: it is skipped. Working it again re-filled the carry each run, so a carry past its cap
        // never drained. A carried message is never skipped: it is not finished.
        var carriedSet = carried.ToHashSet();
        var skippedFinished = recent
            .Where(id => !carriedSet.Contains(id) && finishedAt.TryGetValue(id, out var at) && lastChange[id] < at - DirectOverlap)
            .ToHashSet();

        // ── The budget: carried and recent first (all but the sweep's reserve), then the sweep ──
        var candidates = carried.Concat(recent.Where(id => !skippedFinished.Contains(id))).Where(id => id != Guid.Empty).Distinct().ToList();
        var deferred = candidates.Where(BackedOff).ToList();
        var priority = candidates.Where(id => !BackedOff(id)).ToList();
        var work = priority.Take(MaxDirectMessagesPerRun - DirectSweepReserve).ToList();
        var leftOver = priority.Skip(work.Count).ToList();

        var sweepTake = Math.Min(DirectSweepSlice, MaxDirectMessagesPerRun - work.Count);
        var swept = new List<Guid>();
        var sweepPassComplete = false;
        var sweepRead = false;
        try
        {
            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(DirectMessageParticipantShares.CommunicationTable)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
                TopCount = sweepTake,
                NoLock = true,
            };
            query.Criteria.AddCondition("owningteam", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, team);
            if (sweepAfter is { } after)
                query.Criteria.AddCondition("sprk_communicationid", Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterThan, after);
            query.AddOrder("sprk_communicationid", Microsoft.Xrm.Sdk.Query.OrderType.Ascending);
            swept = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities
                .Select(e => e.Id).Where(id => id != Guid.Empty).ToList() ?? [];
            sweepPassComplete = swept.Count < sweepTake;
            sweepRead = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failure ??= "the Direct-message sweep could not be read";
            _logger.LogError(ex, "[DIRECT-SHARES] run={RunId}: the Direct-message sweep could not be read; it resumes next run.", runId);
        }

        var inWork = work.ToHashSet();
        foreach (var id in swept)
        {
            if (BackedOff(id))
                deferred.Add(id);
            else if (inWork.Add(id))
                work.Add(id);
        }

        deferred = deferred.Distinct().ToList();
        var capReached = leftOver.Count > 0;
        if (capReached)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Total} carried or recent Direct messages are due, past this run's budget " +
                "of {Budget}; {LeftOver} are carried to the next run.", runId, priority.Count,
                MaxDirectMessagesPerRun - DirectSweepReserve, leftOver.Count);
        }

        // ── Reconcile each ──
        var reconciler = new DirectMessageParticipantShares(
            dataverse, shares, lookup, configuration,
            services.GetService<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>() is { } resolver ? resolver.ProbeColumnsAsync : null,
            services.GetService<Spaarke.Dataverse.DataverseWebApiClient>(), _logger);
        var incomplete = new List<Guid>();
        var finished = new List<Guid>();
        int granted = 0, revoked = 0;
        // 198 pass 2 N2 (ontology): a participant this pass grants on the MESSAGE also gets its gate Decision Records (D-215), through the
        // one mirror the live grant calls. Only after a pass that granted, so a steady state costs nothing. Registered by SignalsModule.
        var gateRecords = services.GetService<Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing>();
        foreach (var id in work)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await reconciler.ReconcileAsync(id, team, ct).ConfigureAwait(false);
            granted += outcome.Granted.Count;
            revoked += outcome.Revoked.Count;
            if (gateRecords is not null && outcome.Granted.Any(g => g.Table == DirectMessageParticipantShares.CommunicationTable))
                await gateRecords.MirrorCommunicationSharesAsync(id, ct).ConfigureAwait(false); // never throws (cancellation is logged)
            if (outcome.Complete)
            {
                attempts.Remove(id);
                finished.Add(id);
                continue;
            }

            incomplete.Add(id);
            var failures = (attempts.TryGetValue(id, out var before) ? before.Failures : 0) + 1;
            DateTimeOffset? retryAt = null;
            if (failures >= MaxDirectAttempts)
            {
                retryAt = startedAt + DirectBackoff;
                _logger.LogError("[DIRECT-SHARES] run={RunId}: Direct message {Id} has stayed incomplete {Failures} runs in a row ({Detail}); " +
                    "it is backed off and tried again after {RetryAt:o}.", runId, id, failures, outcome.Detail ?? outcome.Status.ToString(),
                    retryAt);
            }

            attempts[id] = (failures, retryAt);
        }

        // ── Carry, watermark, cursor ──
        // #2731: the incomplete and backed-off messages first, so a carry past its cap drops the left-over tail: the newest listed
        // changes (recent is worked oldest first), which the held window lists again.
        var carry = incomplete.Concat(deferred).Concat(leftOver).Distinct().ToList();
        var carryOverflow = carry.Count > MaxDirectCarried;
        if (carryOverflow)
        {
            // A dropped message this run did not list (one carried in) is reached only by the sweep — certainly so when the listing
            // was complete; when it faulted or was capped, the next runs may still list it.
            var unlisted = carry.Skip(MaxDirectCarried).Count(id => !firstChange.ContainsKey(id));
            if (failure is null && !listingCapped)
            {
                _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Carry} messages to carry, past the cap of {Cap}; the watermark is held, so " +
                    "the dropped messages this run listed are listed again (those already finished are skipped), and the {Unlisted} " +
                    "dropped message(s) it did not list are reached by the sweep.", runId, carry.Count, MaxDirectCarried, unlisted);
            }
            else
            {
                _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Carry} messages to carry, past the cap of {Cap}; the watermark is held, so " +
                    "the dropped messages this run listed are listed again (those already finished are skipped); the listing was " +
                    "incomplete, so the {Unlisted} dropped message(s) it did not list are listed again by a later run or reached by " +
                    "the sweep.", runId, carry.Count, MaxDirectCarried, unlisted);
            }
        }

        DateTimeOffset? watermark = null;
        if (failure is null && !carryOverflow)
        {
            if (!listingCapped)
            {
                watermark = startedAt - DirectOverlap;
            }
            else if (horizons.All(h => h is not null))
            {
                // The capped listings were read oldest change first: everything before the earliest last-row is listed.
                var horizon = horizons.Min()!.Value;
                if (horizon <= since)
                {
                    // Every listed row of a capped listing carries the window's own start: listing again would return the
                    // same rows. Step past it; the rolling sweep reaches the rows that share that instant.
                    horizon = since.AddTicks(1);
                    _logger.LogWarning("[DIRECT-SHARES] run={RunId}: a capped listing did not move past {Since:o}; the watermark steps " +
                        "past it, and the sweep reaches the rows changed at that instant.", runId, since);
                }

                watermark = horizon;
            }
        }

        int carriedOut;
        lock (_directGate)
        {
            _directPending.Clear();
            _directPending.UnionWith(carry.Take(MaxDirectCarried));
            _directAttempts.Clear();
            foreach (var (id, attempt) in attempts.Where(a => _directPending.Contains(a.Key)))
                _directAttempts[id] = attempt;

            // #2731: what this run finished AND listed (a skip only ever applies to a listed message, so a swept or carried-only
            // message is not recorded), and no longer what it left incomplete (an incomplete message is worked again).
            foreach (var id in finished.Where(firstChange.ContainsKey))
                _directFinishedAt[id] = startedAt;
            foreach (var id in incomplete)
                _directFinishedAt.Remove(id);
            if (watermark is { } next)
            {
                _directWatermark = next;

                // Every change listed from here on is at or after the watermark, so an entry finished within the overlap of it
                // can no longer skip anything.
                foreach (var id in _directFinishedAt.Where(f => f.Value - DirectOverlap <= next).Select(f => f.Key).ToList())
                    _directFinishedAt.Remove(id);
            }

            // A window held for long keeps every entry; past the cap the oldest go. Forgetting one only means working it again.
            if (_directFinishedAt.Count > MaxDirectFinishedRemembered)
            {
                foreach (var id in _directFinishedAt.OrderBy(f => f.Value).ThenBy(f => f.Key)
                             .Take(_directFinishedAt.Count - MaxDirectFinishedRemembered).Select(f => f.Key).ToList())
                    _directFinishedAt.Remove(id);
            }

            if (sweepRead && sweepPassComplete)
                _directSweepCursor = null;
            else if (sweepRead && swept.Count > 0)
                _directSweepCursor = swept[^1];
            carriedOut = _directPending.Count;
        }

        return new DirectMessageRun(failure is null ? DirectMessageRun.StatusOk : DirectMessageRun.StatusListingFailed)
        {
            Since = since,
            CarriedIn = carried.Count,
            Listed = recent.Distinct().Count(),
            SkippedFinished = skippedFinished.Count,
            Swept = swept.Count,
            Examined = work.Count,
            Granted = granted,
            Revoked = revoked,
            Incomplete = incomplete.Count,
            Carried = carriedOut,
            CapReached = capReached || listingCapped,
            ListingCapped = listingCapped,
            BackedOff = deferred.Count,
            SweepPassComplete = sweepPassComplete,
            IncompleteIds = incomplete,
            Failure = failure,
        };
    }

    /// <summary>
    /// The Direct-team messages changed since <paramref name="since"/>, each with the row's <c>modifiedon</c> (null when the row
    /// carries none): the messages themselves (<paramref name="messageLookup"/> null), or the messages a content table's changed
    /// rows name — oldest change first, at most
    /// <see cref="MaxDirectListedPerTable"/> rows. <c>Capped</c> when the cap was reached; <c>Horizon</c> is then the last listed
    /// row's <c>modifiedon</c> (null when it carries none). A fault propagates (the caller holds the window).
    /// </summary>
    private static async Task<(List<(Guid Id, DateTimeOffset? ChangedAt)> Rows, DateTimeOffset? Horizon, bool Capped)> ListAsync(
        Spaarke.Dataverse.IGenericEntityService dataverse, string table, string? messageLookup, Guid team, DateTimeOffset since,
        CancellationToken ct)
    {
        var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(table)
        {
            ColumnSet = messageLookup is null
                ? new Microsoft.Xrm.Sdk.Query.ColumnSet("modifiedon")
                : new Microsoft.Xrm.Sdk.Query.ColumnSet(messageLookup, "modifiedon"),
            TopCount = MaxDirectListedPerTable,
            NoLock = true,
        };
        query.Criteria.AddCondition("owningteam", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, team);
        query.Criteria.AddCondition("modifiedon", Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterEqual, since.UtcDateTime);
        query.AddOrder("modifiedon", Microsoft.Xrm.Sdk.Query.OrderType.Ascending);
        query.AddOrder(table + "id", Microsoft.Xrm.Sdk.Query.OrderType.Ascending);
        var rows = ((await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities
            ?? Enumerable.Empty<Microsoft.Xrm.Sdk.Entity>()).ToList();
        // Null when a row carries no modifiedon; the caller decides what that means.
        static DateTimeOffset? ChangedAt(Microsoft.Xrm.Sdk.Entity e) => e.GetAttributeValue<DateTime?>("modifiedon") is { } at
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
            : null;
        var listed = rows
            .Select(e => (Id: messageLookup is null ? e.Id : e.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(messageLookup)?.Id ?? Guid.Empty,
                ChangedAt: ChangedAt(e)))
            .Where(r => r.Id != Guid.Empty)
            .ToList();
        var capped = rows.Count >= MaxDirectListedPerTable;
        DateTimeOffset? horizon = capped && rows[^1].GetAttributeValue<DateTime?>("modifiedon") is { } last
            ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc))
            : null;
        return (listed, horizon, capped);
    }
}
