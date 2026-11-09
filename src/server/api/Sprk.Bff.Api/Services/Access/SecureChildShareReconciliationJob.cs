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
/// Direct-team message in id order (the backstop that survives a restart). A capped run logs it and carries the rest. Writes are
/// on, like the secure mirror; there is no report-only switch on this job, and no new setting. When the Direct team is not
/// configured the pass does nothing; when it cannot be resolved it writes nothing and the run is not a success.</para>
/// </remarks>
public sealed class SecureChildShareReconciliationJob : IScheduledJob
{
    /// <summary>Task 198 Part C: the most Direct-team messages one run reconciles; the rest are carried to the next run.</summary>
    internal const int MaxDirectMessagesPerRun = 200;

    /// <summary>Task 198 Part C: the most rows one recent-changes listing reads per table; past it the watermark is held.</summary>
    internal const int MaxDirectListedPerTable = 500;

    /// <summary>Task 198 Part C: the rolling sweep's slice per run (within <see cref="MaxDirectMessagesPerRun"/>).</summary>
    internal const int DirectSweepSlice = 100;

    /// <summary>Task 198 Part C: the most messages carried between runs; past it the watermark is held.</summary>
    internal const int MaxDirectCarried = 1000;

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
        using (var scope = _scopeFactory.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>()
                .ReconcileAllAsync(cancellationToken).ConfigureAwait(false);

            // Task 198 Part C: the Direct-team messages, after the secure children. Its own window; never throws.
            direct = await ReconcileDirectMessagesAsync(scope.ServiceProvider, startedAt, context.RunId, cancellationToken)
                .ConfigureAwait(false);
        }
        var duration = _timeProvider.GetElapsedTime(started);
        var complete = result.IsComplete && direct.Ok;

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
                      : $"; Direct messages: {direct.Status}, {direct.Incomplete} not complete" +
                        (direct.Failure is null ? "" : $" ({direct.Failure})"))
                  + "; the next run retries.",
            ProcessedItems: result.ChildrenInScope + direct.Examined,
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
                    swept = direct.Swept,
                    examined = direct.Examined,
                    granted = direct.Granted,
                    revoked = direct.Revoked,
                    incomplete = direct.Incomplete,
                    carried = direct.Carried,
                    capReached = direct.CapReached,
                    sweepPassComplete = direct.SweepPassComplete,
                    incompleteMessages = direct.IncompleteIds.Take(50).ToArray(),
                    failure = direct.Failure,
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
        public int Swept { get; init; }
        public int Examined { get; init; }
        public int Granted { get; init; }
        public int Revoked { get; init; }
        public int Incomplete { get; init; }
        public int Carried { get; init; }
        public bool CapReached { get; init; }
        public bool SweepPassComplete { get; init; }
        public IReadOnlyList<Guid> IncompleteIds { get; init; } = [];
        public string? Failure { get; init; }

        /// <summary>Nothing undecided: not configured, unavailable (no lookup registered), or a pass that finished every message.</summary>
        public bool Ok => Status is StatusNotConfigured or StatusUnavailable || (Status == StatusOk && Incomplete == 0 && Failure is null);
    }

    /// <summary>
    /// Task 198 Part C — the Direct-team messages: carried, recently changed (the message or a content row), then the rolling
    /// sweep; at most <see cref="MaxDirectMessagesPerRun"/>, each through <see cref="DirectMessageParticipantShares.ReconcileAsync"/>.
    /// Never throws (cancellation aside).
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
        lock (_directGate)
        {
            since = _directWatermark ?? startedAt - DirectInitialLookback;
            carried = _directPending.OrderBy(id => id).ToList();
            sweepAfter = _directSweepCursor;
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
                Since = since, CarriedIn = carried.Count, Carried = carried.Count, Incomplete = carried.Count,
                Failure = "the Direct-message owner team cannot be resolved",
            };
        }

        // ── Listing: recently changed messages and content rows, then the sweep slice ──
        var recent = new List<Guid>();
        var listingCapped = false;
        string? failure = null;
        try
        {
            var messages = await ListAsync(dataverse, DirectMessageParticipantShares.CommunicationTable, null, team, since, ct)
                .ConfigureAwait(false);
            listingCapped |= messages.Count >= MaxDirectListedPerTable;
            recent.AddRange(messages);
            foreach (var (table, messageLookup) in DirectMessageParticipantShares.ContentRows)
            {
                var ofRows = await ListAsync(dataverse, table, messageLookup, team, since, ct).ConfigureAwait(false);
                listingCapped |= ofRows.Count >= MaxDirectListedPerTable;
                recent.AddRange(ofRows);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failure = "the recently changed Direct messages could not be listed";
            _logger.LogError(ex, "[DIRECT-SHARES] run={RunId}: the recently changed Direct messages could not be listed; the window is " +
                "listed again next run.", runId);
        }

        var first = carried.Concat(recent).Where(id => id != Guid.Empty).Distinct().ToList();
        var sweepTake = Math.Min(DirectSweepSlice, Math.Max(0, MaxDirectMessagesPerRun - first.Count));
        var swept = new List<Guid>();
        var sweepPassComplete = false;
        if (sweepTake > 0)
        {
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failure ??= "the Direct-message sweep could not be read";
                _logger.LogError(ex, "[DIRECT-SHARES] run={RunId}: the Direct-message sweep could not be read; it resumes next run.", runId);
                sweepTake = 0;
            }
        }

        var all = first.Concat(swept).Distinct().ToList();
        var work = all.Take(MaxDirectMessagesPerRun).ToList();
        var leftOver = all.Skip(MaxDirectMessagesPerRun).ToList();
        var capReached = leftOver.Count > 0;
        if (capReached)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Total} Direct messages are due, past the per-run cap of {Cap}; {LeftOver} " +
                "are carried to the next run.", runId, all.Count, MaxDirectMessagesPerRun, leftOver.Count);
        }

        // ── Reconcile each ──
        var reconciler = new DirectMessageParticipantShares(
            dataverse, shares, lookup, configuration,
            services.GetService<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>() is { } resolver ? resolver.ProbeColumnsAsync : null,
            services.GetService<Spaarke.Dataverse.DataverseWebApiClient>(), _logger);
        var incomplete = new List<Guid>();
        int granted = 0, revoked = 0;
        foreach (var id in work)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await reconciler.ReconcileAsync(id, team, ct).ConfigureAwait(false);
            granted += outcome.Granted.Count;
            revoked += outcome.Revoked.Count;
            if (!outcome.Complete)
                incomplete.Add(id);
        }

        // ── Carry, watermark, cursor ──
        var carry = incomplete.Concat(leftOver).Distinct().ToList();
        var carryOverflow = carry.Count > MaxDirectCarried;
        if (carryOverflow)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: {Carry} messages to carry, past the cap of {Cap}; the watermark is held, so " +
                "the window is listed again.", runId, carry.Count, MaxDirectCarried);
        }

        int carriedOut;
        lock (_directGate)
        {
            _directPending.Clear();
            _directPending.UnionWith(carry.Take(MaxDirectCarried));
            if (failure is null && !listingCapped && !carryOverflow)
                _directWatermark = startedAt - DirectOverlap;
            if (sweepTake > 0 && swept.Count > 0 && !sweepPassComplete)
                _directSweepCursor = swept[^1];
            else if (sweepTake > 0 && sweepPassComplete)
                _directSweepCursor = null;
            carriedOut = _directPending.Count;
        }

        if (listingCapped)
        {
            _logger.LogWarning("[DIRECT-SHARES] run={RunId}: a recent-changes listing reached its cap of {Cap}; the watermark is held, " +
                "so the window is listed again next run.", runId, MaxDirectListedPerTable);
        }

        return new DirectMessageRun(failure is null ? DirectMessageRun.StatusOk : DirectMessageRun.StatusListingFailed)
        {
            Since = since,
            CarriedIn = carried.Count,
            Listed = recent.Distinct().Count(),
            Swept = swept.Count,
            Examined = work.Count,
            Granted = granted,
            Revoked = revoked,
            Incomplete = incomplete.Count,
            Carried = carriedOut,
            CapReached = capReached || listingCapped,
            SweepPassComplete = sweepPassComplete,
            IncompleteIds = incomplete,
            Failure = failure,
        };
    }

    /// <summary>
    /// The ids of the Direct-team messages changed since <paramref name="since"/>: the messages themselves
    /// (<paramref name="messageLookup"/> null), or the messages a content table's changed rows name. At most
    /// <see cref="MaxDirectListedPerTable"/> rows. A fault propagates (the caller holds the window).
    /// </summary>
    private static async Task<List<Guid>> ListAsync(
        Spaarke.Dataverse.IGenericEntityService dataverse, string table, string? messageLookup, Guid team, DateTimeOffset since,
        CancellationToken ct)
    {
        var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(table)
        {
            ColumnSet = messageLookup is null
                ? new Microsoft.Xrm.Sdk.Query.ColumnSet(false)
                : new Microsoft.Xrm.Sdk.Query.ColumnSet(messageLookup),
            TopCount = MaxDirectListedPerTable,
            NoLock = true,
        };
        query.Criteria.AddCondition("owningteam", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, team);
        query.Criteria.AddCondition("modifiedon", Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterEqual, since.UtcDateTime);
        IEnumerable<Microsoft.Xrm.Sdk.Entity> rows =
            (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities
            ?? Enumerable.Empty<Microsoft.Xrm.Sdk.Entity>();
        return messageLookup is null
            ? rows.Select(e => e.Id).Where(id => id != Guid.Empty).ToList()
            : rows.Select(e => e.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(messageLookup)?.Id ?? Guid.Empty)
                .Where(id => id != Guid.Empty).ToList();
    }
}
