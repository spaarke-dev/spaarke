using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>ebilling-r1 task 084h-3: stable <see cref="EventId"/>s for the per-record Procedure Decision Record repair job. They are in block
/// 50320-50329, which task 084h-2 opened in <c>ProcedureDecisionRecordEvents</c> (it uses 50320-50322). Every line holds ids, codes and
/// exception type names only, never the reason or the note text.</summary>
public static class RecordProcedureDecisionJobEvents
{
    /// <summary>Information: the repair job of a Procedure change was queued.</summary>
    public static readonly EventId Queued = new(50323, nameof(Queued));

    /// <summary>Warning: queuing the repair job took longer than <see cref="RecordProcedureDecisionEnqueuer.EnqueueTimeout"/> and was
    /// abandoned. The job may or may not have been sent; the Procedure service answers NotRecorded.</summary>
    public static readonly EventId EnqueueTimedOut = new(50324, nameof(EnqueueTimedOut));

    /// <summary>Warning (or Error on the last delivery): the job did not finish for a reason a retry may clear. It is abandoned and
    /// redelivered, and dead-lettered after the last delivery by <c>ServiceBusJobProcessor</c>.</summary>
    public static readonly EventId HandlerRetrying = new(50325, nameof(HandlerRetrying));

    /// <summary>Error: the writer refused the repair for a reason a retry cannot change. Nothing was written; the job is completed, not
    /// retried, and metered under <see cref="RecordProcedureDecisionRepairReason.Abandoned"/>.</summary>
    public static readonly EventId HandlerAbandoned = new(50326, nameof(HandlerAbandoned));

    /// <summary>Information: the job finished. The record was written, or was already there (the inline create had landed), or another
    /// copy of the job holds the decision.</summary>
    public static readonly EventId HandlerCompleted = new(50327, nameof(HandlerCompleted));

    /// <summary>Error: the job's payload cannot be read. It is poisoned (dead-lettered, so the message is kept) and metered under
    /// <see cref="RecordProcedureDecisionRepairReason.PayloadInvalid"/>.</summary>
    public static readonly EventId PayloadInvalid = new(50328, nameof(PayloadInvalid));
}

/// <summary>
/// ebilling-r1 task 084h-3: the bounded <c>reason</c> tags the repair job adds on <c>ontology.writer.failures</c>
/// (<see cref="OntologyWriterTelemetry.RecordFailure"/>). They are not writer refusals (the writer meters its own refusals under
/// <see cref="DecisionRecordRefusalReason"/>), so they are not added to that class; each counts one repair outcome once.
/// </summary>
public static class RecordProcedureDecisionRepairReason
{
    /// <summary>The repair job could not be queued (a Service Bus fault or timeout). The change stands with no record (NotRecorded).</summary>
    public const string EnqueueFailed = "decision_record_procedure_enqueue_failed";

    /// <summary>The writer refused the repair permanently. The job is completed with no record; this is the one count of that gap.</summary>
    public const string Abandoned = "decision_record_procedure_repair_abandoned";

    /// <summary>The repair threw an exception that is not a writer refusal. The job is retried.</summary>
    public const string Faulted = "decision_record_procedure_repair_faulted";

    /// <summary>The job's payload cannot be read. The job is poisoned.</summary>
    public const string PayloadInvalid = "decision_record_procedure_repair_payload_invalid";
}

/// <summary>Whether a writer refusal met by the repair job can be cleared by a retry (ontology-r1 K1, 2026-11-16).</summary>
public enum RecordProcedureRefusalClass
{
    /// <summary>A fault, a missing privilege or a resolver failure that time or an administrator can clear: the job is retried.</summary>
    Retryable,

    /// <summary>The request or its subject is wrong in a way a retry does not change: logged, metered and completed.</summary>
    Permanent,
}

/// <summary>
/// ebilling-r1 task 084h-3 (EB-D-239 Q10; design notes/084h section 7.3): queues the repair of a per-record Procedure change whose inline
/// Decision Record failed after the change committed. It implements 084h-2's <see cref="IRecordProcedureDecisionEnqueuer"/> as an ADR-004
/// job for <see cref="RecordProcedureDecisionJobHandler"/>.
/// </summary>
/// <remarks>
/// <para><b>The payload is the decision, unchanged.</b> The job carries the <see cref="RecordProcedureDecision"/> the inline write was given:
/// the Values, the outcome, the decider and role, the decided-on instant, the reason and the note. The handler rebuilds the same writer request
/// from it, so the repaired record hashes as the inline one would have, and it never re-reads the override row (row ids recur across episodes;
/// design section 7.3). The reason and the note are bounded text (2000 characters each), not blobs (ADR-004).</para>
/// <para><b>Throws on failure.</b> The interface's contract: a failed or timed-out enqueue throws, and the Procedure service answers
/// NotRecorded and logs it (084h-2 <c>RecordFailedNotQueued</c>). This class meters it once
/// (<see cref="RecordProcedureDecisionRepairReason.EnqueueFailed"/>). Logs hold ids only.</para>
/// <para><b>Idempotency key (ADR-004).</b> <see cref="RecordProcedureDecisionKey.Of"/>: the writer's own key for the decision, at most 106
/// characters, under the 128-character MessageId limit. The handler's lock and the writer's "already recorded" lookup make a second delivery
/// write nothing.</para>
/// <para>§11: <i>Existing</i> - <see cref="GateDecisionRecordEnqueuer"/>. <i>Extension</i> - it is bound to the gate's payload and job type;
/// this class copies its shape. <i>Cost of doing nothing</i> - a record that fails after the change committed leaves a permanent gap in the
/// decision history that no user can close (a repeat of the change is refused).</para>
/// </remarks>
public sealed class RecordProcedureDecisionEnqueuer : IRecordProcedureDecisionEnqueuer
{
    /// <summary>The job type the main queue routes to <see cref="RecordProcedureDecisionJobHandler"/>.</summary>
    public const string JobTypeName = "RecordProcedureDecision";

    /// <summary>The longest an enqueue may hold the Procedure change's answer (the <see cref="GateDecisionRecordEnqueuer.EnqueueTimeout"/>
    /// value).</summary>
    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(5);

    private readonly JobSubmissionService _jobs;
    private readonly ILogger<RecordProcedureDecisionEnqueuer> _logger;

    public RecordProcedureDecisionEnqueuer(JobSubmissionService jobs, ILogger<RecordProcedureDecisionEnqueuer> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The job that repairs <paramref name="decision"/>: the decision as its payload, keyed by the writer's key.</summary>
    internal static JobContract JobFor(RecordProcedureDecision decision) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = JobTypeName,
        SubjectId = decision.RecordId.ToString("D", CultureInfo.InvariantCulture),
        CorrelationId = decision.DecisionId.ToString("D", CultureInfo.InvariantCulture),
        IdempotencyKey = RecordProcedureDecisionKey.Of(decision.CoreTable, decision.RecordId, decision.DecisionId),
        Payload = JsonSerializer.SerializeToDocument(decision),
    };

    /// <inheritdoc />
    /// <exception cref="TimeoutException">The enqueue took longer than <see cref="EnqueueTimeout"/> (the job may or may not have been sent).</exception>
    public async Task EnqueueAsync(RecordProcedureDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);

        try
        {
            var job = JobFor(decision);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EnqueueTimeout);
            try
            {
                await _jobs.SubmitJobAsync(job, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                _logger.LogWarning(RecordProcedureDecisionJobEvents.EnqueueTimedOut,
                    "Decision Record {DecisionId} ({ActionCode}) on {CoreTable} {RecordId}: queuing its repair took longer than {TimeoutSeconds} s and was abandoned.",
                    decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, EnqueueTimeout.TotalSeconds);
                throw new TimeoutException("Queuing the Decision Record repair job timed out.");
            }

            _logger.LogInformation(RecordProcedureDecisionJobEvents.Queued,
                "Decision Record {DecisionId} ({ActionCode}) on {CoreTable} {RecordId}: repair job {JobId} queued.",
                decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, job.JobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The caller (the Procedure service) logs the failure with its ids; this is the one count of it.
            OntologyWriterTelemetry.RecordFailure(RecordProcedureDecisionRepairReason.EnqueueFailed);
            throw;
        }
    }
}

/// <summary>
/// ebilling-r1 task 084h-3 (EB-D-239 Q10; design notes/084h sections 7.3, 16 items 3-4): the ADR-004 <see cref="IJobHandler"/> that writes
/// the Decision Record of a per-record Procedure change whose inline write failed after the change committed. It calls the same recorder
/// entry the inline write called (<see cref="BillingDecisionRecorder.RecordProcedureAsync"/>) with the queued decision, so the request, its
/// key and its hash are the inline ones.
/// </summary>
/// <remarks>
/// <para><b>No re-read of the override row.</b> Everything the record says comes from the payload; the handler makes no read of
/// <c>sprk_procedureassignment</c>. A later episode of the same row id (a remove and re-add) therefore cannot change what this decision
/// records (design section 7.3, review item 4).</para>
/// <para><b>Exactly one record per decision.</b> (1) The writer's key from (core record, decision id). (2) A processing lock under that key,
/// owned by this job id, as <see cref="GateDecisionRecordJobHandler"/> takes it. (3) The writer's durable "already recorded" lookup: when
/// the inline create did land (a create timeout), the job answers AlreadyRecorded and writes nothing.</para>
/// <para><b>Outcomes (ontology-r1 K1).</b> Written, already recorded, and a copy in flight are <c>Success</c>. Every writer refusal is
/// classified in <see cref="RefusalClasses"/>: a retryable one is <c>Failure</c> (abandoned, redelivered, dead-lettered after the last
/// delivery); a permanent one is logged at Error, metered (<see cref="RecordProcedureDecisionRepairReason.Abandoned"/>) and completed, never
/// retried. An exception that is not a refusal is <c>Failure</c>. An unreadable payload is <c>Poisoned</c>.</para>
/// <para><b>Logs.</b> Ids, codes and exception type names only, never the reason or the note text (and never an exception object, whose
/// message a Dataverse fault could fill with a column value).</para>
/// <para>Placement (ADR-004, ADR-052): a queue consumer on the existing main-queue seam, in the BFF (B2: it runs the BFF's writer and
/// recorder; B3: low volume, only failed writes). §11: <i>Existing</i> - <see cref="GateDecisionRecordJobHandler"/>. <i>Extension</i> - it
/// is bound to the gate entry and payload; this class copies its shape. <i>Cost of doing nothing</i> - see
/// <see cref="RecordProcedureDecisionEnqueuer"/>.</para>
/// </remarks>
public sealed class RecordProcedureDecisionJobHandler : IJobHandler
{
    /// <summary>How long a job holds the decision's processing lock (the gate job's value).</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Every <see cref="DecisionRecordRefusalReason"/>, classified for this job (ontology-r1 K1). A reason missing here fails
    /// <c>RecordProcedureDecisionJobTests</c>; at run time an unlisted reason is retried, so the message is never dropped.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RecordProcedureRefusalClass> RefusalClasses =
        new Dictionary<string, RecordProcedureRefusalClass>(StringComparer.Ordinal)
        {
            // Raised on this path. A fault, a privilege or a resolver failure: time or an administrator can clear it.
            [DecisionRecordRefusalReason.RecordProcedureLookupFailed] = RecordProcedureRefusalClass.Retryable, // lookup, writer identity or decider contact
            [DecisionRecordRefusalReason.FilingFailed] = RecordProcedureRefusalClass.Retryable,                // record-type read fault
            [DecisionRecordRefusalReason.OwnerResolutionFailed] = RecordProcedureRefusalClass.Retryable,       // the owner could not be read
            [DecisionRecordRefusalReason.CreateFailed] = RecordProcedureRefusalClass.Retryable,                // a landed create is AlreadyRecorded next time
            [DecisionRecordRefusalReason.DataverseAccessDenied] = RecordProcedureRefusalClass.Retryable,       // an administrator can grant it

            // Raised on this path. The request or its subject is wrong in a way a retry does not change.
            [DecisionRecordRefusalReason.ReviewInvalid] = RecordProcedureRefusalClass.Permanent,               // invalid, or the key holds other content
            [DecisionRecordRefusalReason.FactSnapshotMissing] = RecordProcedureRefusalClass.Permanent,
            [DecisionRecordRefusalReason.RecordClassUnderivable] = RecordProcedureRefusalClass.Permanent,      // not a Procedure code
            [DecisionRecordRefusalReason.SubjectUnsupported] = RecordProcedureRefusalClass.Permanent,          // not a filing core table
            [DecisionRecordRefusalReason.SubjectUnfileable] = RecordProcedureRefusalClass.Permanent,
            [DecisionRecordRefusalReason.CoreUnsupported] = RecordProcedureRefusalClass.Permanent,
            [DecisionRecordRefusalReason.OwnerMissing] = RecordProcedureRefusalClass.Permanent,
            [DecisionRecordRefusalReason.OwnerRefused] = RecordProcedureRefusalClass.Permanent,                // e.g. the core record was deleted

            // Not raised on this path (another entry's reason). Classified by what each means, so a future change cannot leave one open.
            [DecisionRecordRefusalReason.BillingRecordLookupFailed] = RecordProcedureRefusalClass.Retryable,   // a lookup fault
            [DecisionRecordRefusalReason.GateRecordLookupFailed] = RecordProcedureRefusalClass.Retryable,      // a lookup fault
            [DecisionRecordRefusalReason.ConditionClearedLookupFailed] = RecordProcedureRefusalClass.Retryable, // a lookup fault
            [DecisionRecordRefusalReason.GateRecordFailed] = RecordProcedureRefusalClass.Retryable,            // an unclassified fault
            [DecisionRecordRefusalReason.GateEnqueueFailed] = RecordProcedureRefusalClass.Retryable,           // a queue fault
            [DecisionRecordRefusalReason.SecureOwnerMismatch] = RecordProcedureRefusalClass.Retryable,         // metered after a create; a retry finds the record
            [DecisionRecordRefusalReason.ParticipantShareFailed] = RecordProcedureRefusalClass.Retryable,      // a share fault after a create
            [DecisionRecordRefusalReason.FilingReownFailed] = RecordProcedureRefusalClass.Retryable,           // a re-own fault after a create
            [DecisionRecordRefusalReason.GateSkippedNoUnitTeam] = RecordProcedureRefusalClass.Permanent,       // a skip by design
            [DecisionRecordRefusalReason.GateSkippedOwnerDisabled] = RecordProcedureRefusalClass.Permanent,    // a skip by design
            [DecisionRecordRefusalReason.ConditionClearedSubjectMissing] = RecordProcedureRefusalClass.Permanent, // the subject is gone
        };

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<RecordProcedureDecisionJobHandler> _logger;

    public RecordProcedureDecisionJobHandler(
        BillingDecisionRecorder recorder,
        IIdempotencyService idempotency,
        ILogger<RecordProcedureDecisionJobHandler> logger)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => RecordProcedureDecisionEnqueuer.JobTypeName;

    /// <summary>The class of a writer refusal for this job; an unlisted reason is retried (fail safe: the message is never dropped).</summary>
    internal static RecordProcedureRefusalClass ClassOf(string reason) =>
        RefusalClasses.TryGetValue(reason, out var refusalClass) ? refusalClass : RecordProcedureRefusalClass.Retryable;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        if (!TryReadPayload(job, out var decision))
        {
            _logger.LogError(RecordProcedureDecisionJobEvents.PayloadInvalid,
                "Decision Record repair job {JobId} has an unreadable payload; it is poisoned (a retry cannot fix it).", job.JobId);
            OntologyWriterTelemetry.RecordFailure(RecordProcedureDecisionRepairReason.PayloadInvalid);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid Procedure decision payload", job.Attempt, stopwatch.Elapsed);
        }

        var key = RecordProcedureDecisionKey.Of(decision.CoreTable, decision.RecordId, decision.DecisionId);
        var owner = job.JobId.ToString("N", CultureInfo.InvariantCulture);

        if (!await _idempotency.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false))
        {
            // Another copy of the same decision is being written now. It owns the write, and the broker redelivers it if it fails.
            _logger.LogInformation(RecordProcedureDecisionJobEvents.HandlerCompleted,
                "Decision Record repair job {JobId} not run: another job holds decision {DecisionId} ({ActionCode}) on {CoreTable} {RecordId}.",
                job.JobId, decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        try
        {
            var result = await _recorder.RecordProcedureAsync(decision, ct).ConfigureAwait(false);

            _logger.LogInformation(RecordProcedureDecisionJobEvents.HandlerCompleted,
                "Decision Record repair job {JobId} done: decision {DecisionId} ({ActionCode}) on {CoreTable} {RecordId}, record {RecordProcedureRecordId} " +
                "{Result}, durationMs={DurationMs}.",
                job.JobId, decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, result.RecordId,
                result.AlreadyRecorded ? "already recorded" : "written", (long)stopwatch.Elapsed.TotalMilliseconds);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (DecisionRecordRefusedException ex)
        {
            if (ClassOf(ex.Reason) == RecordProcedureRefusalClass.Permanent)
            {
                _logger.LogError(RecordProcedureDecisionJobEvents.HandlerAbandoned,
                    "Decision Record repair job {JobId}: decision {DecisionId} ({ActionCode}) on {CoreTable} {RecordId} was refused for a reason a retry " +
                    "does not change ({Reason}, ownerRefusal={OwnerRefusalCode}); no record was written and the job is completed.",
                    job.JobId, decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, ex.Reason, ex.OwnerRefusalCode);
                OntologyWriterTelemetry.RecordFailure(RecordProcedureDecisionRepairReason.Abandoned);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // The writer has already logged and metered its own refusal; a retry may clear this one.
            _logger.Log(job.IsFinalDelivery ? LogLevel.Error : LogLevel.Warning, RecordProcedureDecisionJobEvents.HandlerRetrying,
                "Decision Record repair job {JobId}: decision {DecisionId} ({ActionCode}) on {CoreTable} {RecordId} did not finish ({Reason}); it is " +
                "redelivered (attempt {Attempt}, delivery {Delivery}) and dead-lettered after the last delivery.",
                job.JobId, decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, ex.Reason, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, ex.Reason, job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Log(job.IsFinalDelivery ? LogLevel.Error : LogLevel.Warning, RecordProcedureDecisionJobEvents.HandlerRetrying,
                "Decision Record repair job {JobId}: decision {DecisionId} ({ActionCode}) on {CoreTable} {RecordId} threw ({ExceptionType}); it is " +
                "redelivered (attempt {Attempt}, delivery {Delivery}) and dead-lettered after the last delivery.",
                job.JobId, decision.DecisionId, decision.ActionCode, decision.CoreTable, decision.RecordId, ex.GetType().Name, job.Attempt, job.DeliveryCount);
            OntologyWriterTelemetry.RecordFailure(RecordProcedureDecisionRepairReason.Faulted);
            return JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
        }
        finally
        {
            // Not with ct: on a graceful stop ct is cancelled and a cancelled token never reaches the cache (task 068, #1086).
            await _idempotency.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The queued decision, or false when the payload is missing, is not JSON of that shape, or names no decision, record or table
    /// (the key cannot be built). Every other check is the writer's, so an invalid decision is refused (permanently) there.</summary>
    private static bool TryReadPayload(JobContract job, out RecordProcedureDecision decision)
    {
        decision = null!;
        if (job.Payload is null) return false;

        RecordProcedureDecision? read;
        try
        {
            read = job.Payload.Deserialize<RecordProcedureDecision>(PayloadOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (read is null || read.DecisionId == Guid.Empty || read.RecordId == Guid.Empty || string.IsNullOrWhiteSpace(read.CoreTable))
        {
            return false;
        }

        decision = read;
        return true;
    }
}
