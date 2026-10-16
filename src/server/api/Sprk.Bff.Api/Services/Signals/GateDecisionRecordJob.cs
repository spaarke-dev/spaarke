using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>Stable <see cref="EventId"/>s for the gate Decision Record job (task 149). Kept beside the job, not in
/// <see cref="OntologyWriterEvents"/> or <see cref="DecisionRecordEvents"/>, so a parallel task adding an id there cannot collide.</summary>
public static class GateDecisionRecordEvents
{
    /// <summary>Error: the gate decided but its Decision Record job could not be queued. The gate's outcome and the RI action are
    /// unchanged; no record is written for that run (an enrichment retry queues it again). Ids only.</summary>
    public static readonly EventId EnqueueFailed = new(50380, nameof(EnqueueFailed));

    /// <summary>Warning: queuing the job exceeded <see cref="GateDecisionRecordEnqueuer.EnqueueTimeout"/> and was abandoned. Counted as
    /// <see cref="DecisionRecordRefusalReason.GateEnqueueFailed"/>: the job may or may not have been sent.</summary>
    public static readonly EventId EnqueueTimedOut = new(50381, nameof(EnqueueTimedOut));

    /// <summary>Warning (a refusal the writer already logged at Error, or a transient fault) or Error (an unexpected exception, or a
    /// poisoned job): the job did not finish. A transient one is abandoned and redelivered, then dead-lettered after the last delivery by
    /// <c>ServiceBusJobProcessor</c> (never dropped).</summary>
    public static readonly EventId HandlerFailed = new(50382, nameof(HandlerFailed));

    /// <summary>Information: the job finished, and says which way: the record was written, was already there, was skipped by design
    /// (D-144) or another copy of the same job holds the decision. Ids only.</summary>
    public static readonly EventId HandlerCompleted = new(50383, nameof(HandlerCompleted));
}

/// <summary>
/// What the gate decided, as the queue carries it (task 149; D-139): IDS and the gate's own decision values only. Never the
/// communication's subject, sender or body (D-123): the writer reads whatever else it needs by id when the job runs.
/// </summary>
/// <param name="CommunicationId">The assessed <c>sprk_communication</c>.</param>
/// <param name="EvaluationId">The id of the gate RUN (the fact snapshot's correlation id). NOT the record's key: that is
/// <see cref="GateDecisionKey"/> from (<paramref name="CommunicationId"/>, <paramref name="Authorize"/>).</param>
/// <param name="Authorize">The gate's outcome.</param>
/// <param name="Reason">The gate's reason code (<c>rule-matched-confidence-met</c> and its siblings: a closed set, never message text).</param>
/// <param name="Confidence">The assessment confidence the gate compared.</param>
/// <param name="Threshold">The threshold it compared against.</param>
/// <param name="MatchedRuleId">The matched <c>sprk_communicationrule</c>, or null when none matched.</param>
/// <param name="PrivilegeFlagged">The matched rule's privilege flag, carried forward (ADR-015: flagged, never decided).</param>
/// <param name="DirectFiledUnderId">The record the communication was DIRECTLY filed under when the gate ran (D-37's first tie-break), or null.</param>
/// <param name="DecidedOn">When the gate decided: the record states the DECISION time, not the (possibly retried) write time.</param>
public sealed record GateDecisionRecordPayload(
    Guid CommunicationId,
    Guid EvaluationId,
    bool Authorize,
    string Reason,
    double Confidence,
    double Threshold,
    Guid? MatchedRuleId,
    bool PrivilegeFlagged,
    Guid? DirectFiledUnderId,
    DateTimeOffset DecidedOn);

/// <summary>
/// The enqueue side of the gate Decision Record (task 149; D-139): turns "the gate decided X about this communication" into an ADR-004
/// job for <see cref="GateDecisionRecordJobHandler"/>. The communications gate calls it once per run, before its authorize/deny
/// branch, and does not wait for the record.
/// </summary>
/// <remarks>
/// <para><b>Never throws, never changes the gate.</b> A failed or timed-out enqueue is logged (ids only) and metered under
/// <see cref="DecisionRecordRefusalReason.GateEnqueueFailed"/> on <c>ontology.writer.failures</c>, and returns false. There is no inline
/// fallback write: the whole point of D-139 is that the gate stops spending Dataverse requests on the record. The next enrichment run of
/// the same communication queues the decision again, and the job is idempotent.</para>
/// <para><b>Idempotency key (ADR-004).</b> <see cref="GateDecisionKey.Of"/>: deterministic from (communication, outcome), the same on
/// every run. The queue's duplicate detection is off for the BFF queues, so two sends of the key are two messages; the handler (its lock,
/// and the writer's lookup) is what makes them one record.</para>
/// <para>§11: <i>Existing</i> - <see cref="SignalEvaluationTrigger"/> is the nearest enqueuer, for a different job and payload.
/// <i>Extension</i> - it is bound to Signal evaluation kinds and its payload, so it is not extended; this class reuses
/// <see cref="JobSubmissionService"/> and its shape. <i>Cost of doing nothing</i> - each email spends the record's ~6-9 Dataverse requests
/// inline on the shared app identity before the RI action, and every enrichment retry writes a duplicate record.</para>
/// </remarks>
public class GateDecisionRecordEnqueuer
{
    /// <summary>The job type the main queue routes to <see cref="GateDecisionRecordJobHandler"/>.</summary>
    public const string JobTypeName = "GateDecisionRecord";

    /// <summary>The longest an enqueue may hold the gate. A degraded Service Bus must not stall enrichment; on timeout the enqueue is
    /// abandoned (and metered) and the gate goes on.</summary>
    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(5);

    private readonly JobSubmissionService _jobs;
    private readonly ILogger<GateDecisionRecordEnqueuer> _logger;

    public GateDecisionRecordEnqueuer(JobSubmissionService jobs, ILogger<GateDecisionRecordEnqueuer> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Queues the decision's Decision Record job. Returns whether the job was queued.</summary>
    public virtual async Task<bool> EnqueueAsync(GateDecisionRecordPayload decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);

        try
        {
            var job = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = JobTypeName,
                SubjectId = decision.CommunicationId.ToString("D", CultureInfo.InvariantCulture),
                CorrelationId = decision.EvaluationId.ToString("D", CultureInfo.InvariantCulture),
                IdempotencyKey = GateDecisionKey.Of(decision.CommunicationId, decision.Authorize),
                Payload = JsonSerializer.SerializeToDocument(decision),
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EnqueueTimeout);
            try
            {
                await _jobs.SubmitJobAsync(job, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                _logger.LogWarning(GateDecisionRecordEvents.EnqueueTimedOut,
                    "[GATE-DR] queuing the Decision Record job for communication {CommunicationId} (evaluation {EvaluationId}, authorized {Authorize}) " +
                    "took longer than {TimeoutSeconds} s and was abandoned; the gate is unaffected.",
                    decision.CommunicationId, decision.EvaluationId, decision.Authorize, EnqueueTimeout.TotalSeconds);
                OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.GateEnqueueFailed);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Swallowed on purpose (the gate must not fail), so it is logged with the exception (App Insights `exceptions`) and metered.
            _logger.LogError(GateDecisionRecordEvents.EnqueueFailed, ex,
                "[GATE-DR] could not queue the Decision Record job for communication {CommunicationId} (evaluation {EvaluationId}, " +
                "authorized {Authorize}); the gate is unaffected ({ExceptionType}).",
                decision.CommunicationId, decision.EvaluationId, decision.Authorize, ex.GetType().Name);
            OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.GateEnqueueFailed);
            return false;
        }
    }
}

/// <summary>
/// The ADR-004 <see cref="IJobHandler"/> that writes a communications-gate decision's Decision Record (task 149; D-139, D-144). It
/// builds the fact snapshot from the payload and calls <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/> - task 042's entry
/// point, the writer's ONE create site - which creates the record only when no record exists for the decision.
/// </summary>
/// <remarks>
/// <para><b>Exactly one record per (communication, gate outcome).</b> Three layers, strongest last. (1) The enqueuer's key is the same on
/// every run. (2) A processing lock under that key, owned by this job id (a redelivery of this job takes over a dead attempt's lock; a
/// second job for the same decision is refused while the first runs). (3) The writer looks the record up by the decision key before it
/// creates (durable, so it survives a cache eviction and a timed-out create whose row exists). The lock narrows the window in which two
/// copies could both find nothing; it cannot close it (the idempotency cache is check-then-set and fails open), which is why (3) is
/// the writer's and not a cache entry.</para>
/// <para><b>Outcomes.</b> Written, already recorded, skipped by design (D-144: no readable owner or owning business unit) and a duplicate in
/// flight are all <c>Success</c>. A payload that cannot be read, or a refusal a retry cannot change (invalid request, no fact snapshot,
/// no subject, unfileable subject), is <c>Poisoned</c>: dead-lettered and visible. Everything else (a Dataverse fault, an owner the
/// resolver refused, a privilege fault, a lookup that could not be made) is <c>Failure</c>: abandoned and redelivered, then
/// dead-lettered after the last delivery by <c>ServiceBusJobProcessor</c>.</para>
/// <para><b>Priority at job time.</b> The matched rule's <c>sprk_priority</c> is read here, not by the gate (it was an inline request on
/// the gate's path). A rule edited between the decision and the job shows its later priority; the rule id in the snapshot is the
/// decision's.</para>
/// <para><b>D-123.</b> The payload, the fact snapshot and every log line hold ids and the gate's own values only.</para>
/// <para>Placement (ADR-004, ADR-052): a queue consumer on the existing main-queue seam. §11: <i>Existing</i> - no job type writes
/// Decision Records. <i>Extension</i> - the writer and the processor are reused unchanged; only the glue is new. <i>Cost of doing
/// nothing</i> - see <see cref="GateDecisionRecordEnqueuer"/>.</para>
/// </remarks>
public sealed class GateDecisionRecordJobHandler : IJobHandler
{
    /// <summary>The gate's proposed action as the Decision Record names it: free text, not a <c>DecisionActionCatalog</c> code (task 042).</summary>
    internal const string ProposedActionCode = "communication-ri-action";

    internal const string ProposedActionLabel = "Create a review task and notify the communication's owner";

    /// <summary>How long a job holds the decision's processing lock. A crashed attempt's lock is taken over by its own redelivery after a
    /// minute (<c>IdempotencyService.OwnerTakeoverAge</c>); this only bounds a lock nothing released.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DecisionRecordWriter _writer;
    private readonly IGenericEntityService _entities;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<GateDecisionRecordJobHandler> _logger;

    public GateDecisionRecordJobHandler(
        DecisionRecordWriter writer,
        IGenericEntityService entities,
        IIdempotencyService idempotency,
        ILogger<GateDecisionRecordJobHandler> logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => GateDecisionRecordEnqueuer.JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        if (!TryReadPayload(job, out var payload))
        {
            _logger.LogError(GateDecisionRecordEvents.HandlerFailed,
                "[GATE-DR] job {JobId} has an unreadable payload; it is poisoned (a retry cannot fix it).", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid gate decision payload", job.Attempt, stopwatch.Elapsed);
        }

        var key = GateDecisionKey.Of(payload.CommunicationId, payload.Authorize);
        var owner = job.JobId.ToString("N", CultureInfo.InvariantCulture);

        if (!await _idempotency.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false))
        {
            // Another copy of the same decision is being written right now. It owns the write, and the broker redelivers it if it fails.
            _logger.LogInformation(GateDecisionRecordEvents.HandlerCompleted,
                "[GATE-DR] job {JobId} not run: another job holds the decision for communication {CommunicationId} (authorized {Authorize}).",
                job.JobId, payload.CommunicationId, payload.Authorize);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        try
        {
            var request = await BuildRequestAsync(payload, ct).ConfigureAwait(false);
            var result = await _writer.WriteGateDecisionAsync(request, ct).ConfigureAwait(false);

            _logger.LogInformation(GateDecisionRecordEvents.HandlerCompleted,
                "[GATE-DR] job {JobId} done: communication {CommunicationId} authorized {Authorize}, record {RecordId} " +
                "{Result}, durationMs={DurationMs}, correlationId={CorrelationId}.",
                job.JobId, payload.CommunicationId, payload.Authorize, result.RecordId,
                result.AlreadyRecorded ? "already recorded" : "written", (long)stopwatch.Elapsed.TotalMilliseconds, job.CorrelationId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (DecisionRecordRefusedException ex) when (ex.Reason is DecisionRecordRefusalReason.GateSkippedNoUnitTeam
                                                          or DecisionRecordRefusalReason.GateSkippedOwnerDisabled)
        {
            // D-144, by design: nothing is written and a retry meets the same condition. The writer has already logged it (Warning) and
            // counted it on ontology.writer.skips; completing the message is the honest outcome.
            _logger.LogInformation(GateDecisionRecordEvents.HandlerCompleted,
                "[GATE-DR] job {JobId} done without a record: communication {CommunicationId} authorized {Authorize} was skipped by design " +
                "(its owner is disabled or its owner or owning business unit cannot be read, D-144).",
                job.JobId, payload.CommunicationId, payload.Authorize);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (DecisionRecordRefusedException ex)
        {
            // The writer has already logged this refusal at Error (WriteRefused) and counted it under its own reason.
            if (IsPermanent(ex.Reason))
            {
                _logger.LogError(GateDecisionRecordEvents.HandlerFailed,
                    "[GATE-DR] job {JobId} for communication {CommunicationId} authorized {Authorize} is poisoned: the writer refused it " +
                    "for a reason a retry does not change ({Reason}).",
                    job.JobId, payload.CommunicationId, payload.Authorize, ex.Reason);
                return JobOutcome.Poisoned(job.JobId, JobType, ex.Reason, job.Attempt, stopwatch.Elapsed);
            }

            _logger.LogWarning(GateDecisionRecordEvents.HandlerFailed,
                "[GATE-DR] job {JobId} for communication {CommunicationId} authorized {Authorize} did not finish ({Reason}); " +
                "it will be redelivered (attempt {Attempt}, delivery {Delivery}), and dead-lettered after the last delivery.",
                job.JobId, payload.CommunicationId, payload.Authorize, ex.Reason, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, ex.Reason, job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(GateDecisionRecordEvents.HandlerFailed, ex,
                "[GATE-DR] job {JobId} for communication {CommunicationId} authorized {Authorize} threw ({ExceptionType}); it will be " +
                "redelivered (attempt {Attempt}, delivery {Delivery}).",
                job.JobId, payload.CommunicationId, payload.Authorize, ex.GetType().Name, job.Attempt, job.DeliveryCount);
            OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.GateRecordFailed);
            return JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
        }
        finally
        {
            // Not with ct: on a graceful stop ct is cancelled and a cancelled token never reaches the cache (task 068, #1086).
            await _idempotency.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The refusals a retry cannot change: the request itself is wrong, or the subject can never be filed. Every other reason
    /// is a fault, a missing privilege or a resolver refusal that an administrator or time can clear.</summary>
    private static bool IsPermanent(string reason) =>
        reason is DecisionRecordRefusalReason.ReviewInvalid
            or DecisionRecordRefusalReason.FactSnapshotMissing
            or DecisionRecordRefusalReason.OwnerMissing
            or DecisionRecordRefusalReason.SubjectUnsupported
            or DecisionRecordRefusalReason.SubjectUnfileable;

    private async Task<GateDecisionRecordRequest> BuildRequestAsync(GateDecisionRecordPayload p, CancellationToken ct)
    {
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["correlationId"] = p.EvaluationId,
            ["communicationId"] = p.CommunicationId,
            ["gateOutcome"] = p.Authorize ? "Authorized" : "Denied",
            ["gateReason"] = p.Reason,
            ["confidence"] = p.Confidence,
            ["threshold"] = p.Threshold,
            ["matchedRuleId"] = p.MatchedRuleId,
            ["matchedRulePriority"] = await ReadRulePriorityAsync(p.MatchedRuleId, ct).ConfigureAwait(false),
            ["privilegeFlagged"] = p.PrivilegeFlagged,
        };

        return new GateDecisionRecordRequest(
            CommunicationId: p.CommunicationId,
            EvaluationId: p.EvaluationId,
            Authorize: p.Authorize,
            Reason: p.Reason,
            ProposedActionCode: ProposedActionCode,
            ProposedActionLabel: ProposedActionLabel,
            Facts: facts,
            PrivilegeFlagged: p.PrivilegeFlagged,
            DirectFiledUnderId: p.DirectFiledUnderId,
            DecidedOn: p.DecidedOn == default ? null : p.DecidedOn); // an absent time is the writer's clock, never year 1
    }

    /// <summary>The matched rule's <c>sprk_priority</c> for the fact snapshot (the gate's decision carries the rule id, not its priority).
    /// Best effort: a rule that cannot be read leaves the priority out of the snapshot and never costs the record.</summary>
    private async Task<int?> ReadRulePriorityAsync(Guid? ruleId, CancellationToken ct)
    {
        if (ruleId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        try
        {
            var rule = await _entities.RetrieveAsync("sprk_communicationrule", id, new[] { "sprk_priority" }, ct).ConfigureAwait(false);
            return rule?.GetAttributeValue<int?>("sprk_priority");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[GATE-DR] could not read the priority of rule {RuleId} for the Decision Record; recording without it.", id);
            return null;
        }
    }

    private static bool TryReadPayload(JobContract job, out GateDecisionRecordPayload payload)
    {
        payload = null!;
        if (job.Payload is null) return false;

        GateDecisionRecordPayload? read;
        try
        {
            read = job.Payload.Deserialize<GateDecisionRecordPayload>(PayloadOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (read is null || read.CommunicationId == Guid.Empty || read.EvaluationId == Guid.Empty || string.IsNullOrWhiteSpace(read.Reason))
        {
            return false;
        }

        payload = read;
        return true;
    }
}
