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
/// </remarks>
public sealed class SecureChildShareReconciliationJob : IScheduledJob
{
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
        SecureChildShareSyncResult result;
        using (var scope = _scopeFactory.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>()
                .ReconcileAllAsync(cancellationToken).ConfigureAwait(false);
        }
        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            result.IsComplete ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-SHARES] heartbeat status={Status} inScope={InScope} updated={Updated} unchanged={Unchanged} " +
            "notUpdated={NotUpdated} held={Held} outsideSecureRoots={Outside} granted={Granted} changed={Changed} " +
            "revoked={Revoked} detail={Detail} attempt={Attempt} durationMs={DurationMs} trigger={Trigger} runId={RunId} " +
            "correlationId={CorrelationId}",
            result.Status, result.ChildrenInScope, result.ChildrenUpdated, result.ChildrenUnchanged,
            result.ChildrenNotUpdated, result.ChildrenHeld, result.ChildrenOutsideSecureRoots, result.SharesGranted,
            result.SharesChanged, result.SharesRevoked, result.Detail, context.Attempt, (long)duration.TotalMilliseconds,
            context.Trigger, context.RunId, context.CorrelationId);

        if (result.Status == SecureChildShareSyncStatus.Failed)
        {
            // ADR-036 A1 rule 4: nothing was decided, and a retry this tick can still do the work.
            throw new InvalidOperationException(
                $"The secure-child share reconciliation could not run: {result.Detail}. Nothing was written.");
        }

        return new JobRunResult(
            Success: result.IsComplete,
            ErrorMessage: result.IsComplete
                ? null
                : $"{result.ChildrenNotUpdated} child(ren) not updated and {result.ChildrenHeld} held; the next run retries.",
            ProcessedItems: result.ChildrenInScope,
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
                detail = result.Detail,
                attempt = context.Attempt,
            }));
    }
}
