using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What raised an event-triggered Signal evaluation (task 032; D-12, D-48).</summary>
public enum SignalTriggerKind
{
    /// <summary>A communication was classified (the triage category is persisted): a fee or scope email may now hold.</summary>
    Classification,

    /// <summary>A <c>sprk_budgetrevision</c> was created: the <c>notExists</c> half of Path B may now be false.</summary>
    BudgetRevision,

    /// <summary>A communication was filed to a core record (association confirmed, D-48).</summary>
    AssociationConfirmed,
}

/// <summary>Stable <see cref="EventId"/>s for the event triggers (task 032). Kept beside the trigger, not in
/// <see cref="OntologyWriterEvents"/>, so a parallel task adding an id there cannot collide with these.</summary>
public static class SignalTriggerEvents
{
    /// <summary>Error: the trigger could not be queued. The event is NOT lost - the nightly pass is the backstop - but it is no
    /// longer live, so it is logged with the exception (App Insights <c>exceptions</c>), never swallowed quietly.</summary>
    public static readonly EventId EnqueueFailed = new(50340, nameof(EnqueueFailed));

    /// <summary>Error: a queued trigger could not finish; the message is abandoned and redelivered, and dead-lettered after
    /// the last delivery (never dropped).</summary>
    public static readonly EventId HandlerFailed = new(50341, nameof(HandlerFailed));

    /// <summary>Information: one trigger finished. Counts only; no fact values, no sentence.</summary>
    public static readonly EventId HandlerCompleted = new(50342, nameof(HandlerCompleted));

    /// <summary>Warning: an enqueue exceeded <see cref="SignalEvaluationTrigger.EnqueueTimeout"/> and was abandoned (the nightly pass covers it).</summary>
    public static readonly EventId EnqueueTimedOut = new(50343, nameof(EnqueueTimedOut));
}

/// <summary>
/// The enqueue side of the event triggers (task 032; spec FR-13, FR-14a; D-12, D-48): ONE place that turns "this happened to
/// this record" into an ADR-004 job for <see cref="SignalEvaluationTriggerHandler"/>. The classification path, the budget-revision
/// create and the association-confirmed filing path all call this; none of them evaluates anything.
/// </summary>
/// <remarks>
/// <para><b>Never throws.</b> The callers sit on paths that must not fail because a Signal could not be refreshed live (enrichment,
/// a decision that has already written a revision). A failed enqueue is logged at Error with the exception and returns false;
/// the nightly pass re-evaluates the subject within a day, which is why a failure here is a delay, not a lost event.</para>
/// <para><b>Idempotency key (ADR-004).</b> <c>signal-eval-{kind}-{subjectId}-{sourceId}</c>, deterministic from the event.
/// The handler is idempotent by construction (it writes only what changed, D-61), so a redelivered or repeated trigger is safe
/// and is NOT marked processed: a second classification of the same communication must evaluate again.</para>
/// <para>§11: <i>Existing</i> - <see cref="JobSubmissionService"/> queues jobs; nothing queues a Signal evaluation.
/// <i>Extension</i> - it is reused as is; this class only fixes the payload shape for three callers. <i>Cost of doing nothing</i> - each caller would hand-roll the payload and
/// the failure handling, and the three would drift.</para>
/// </remarks>
public class SignalEvaluationTrigger
{
    /// <summary>The job type the main queue routes to <see cref="SignalEvaluationTriggerHandler"/>.</summary>
    public const string JobTypeName = "SignalEvaluationTrigger";

    /// <summary>The longest an enqueue may hold its caller. A degraded Service Bus must not stall a request (the classification
    /// path, the decision commit route); on timeout the enqueue is logged at Warning and abandoned, and the nightly pass covers it.</summary>
    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(5);

    private readonly JobSubmissionService _jobs;
    private readonly ILogger<SignalEvaluationTrigger> _logger;

    public SignalEvaluationTrigger(JobSubmissionService jobs, ILogger<SignalEvaluationTrigger> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Queues an evaluation of the Decide-lane policies for one subject (<paramref name="subjectEntity"/>,
    /// <paramref name="subjectId"/>). <paramref name="sourceId"/> is the record that raised it (the communication or the
    /// revision) and only names the job. Returns whether the job was queued.</summary>
    public virtual async Task<bool> EnqueueAsync(
        SignalTriggerKind kind, string subjectEntity, Guid subjectId, Guid sourceId, string? correlationId = null, CancellationToken ct = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectEntity);
            if (subjectId == Guid.Empty) throw new ArgumentException("A subject id is required.", nameof(subjectId));

            var kindName = KindName(kind);
            var job = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = JobTypeName,
                SubjectId = subjectId.ToString("D", CultureInfo.InvariantCulture),
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("D") : correlationId,
                IdempotencyKey = $"signal-eval-{kindName}-{subjectId:D}-{sourceId:D}",
                Payload = JsonSerializer.SerializeToDocument(new TriggerPayload(kindName, subjectEntity, subjectId, sourceId)),
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EnqueueTimeout);
            try
            {
                await _jobs.SubmitJobAsync(job, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                _logger.LogWarning(SignalTriggerEvents.EnqueueTimedOut,
                    "[SIGNAL-TRIGGER] queuing the {Kind} trigger for {SubjectEntity} {SubjectId} (source {SourceId}) took longer than " +
                    "{TimeoutSeconds} s and was abandoned; the nightly pass will pick it up.",
                    kindName, subjectEntity, subjectId, sourceId, EnqueueTimeout.TotalSeconds);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Swallowed on purpose (the caller must not fail), so it is logged with the exception: App Insights `exceptions`.
            _logger.LogError(SignalTriggerEvents.EnqueueFailed, ex,
                "[SIGNAL-TRIGGER] could not queue the {Kind} trigger for {SubjectEntity} {SubjectId} (source {SourceId}); " +
                "the nightly pass will pick it up ({ExceptionType}).",
                KindName(kind), subjectEntity, subjectId, sourceId, ex.GetType().Name);
            return false;
        }
    }

    internal static string KindName(SignalTriggerKind kind) => kind switch
    {
        SignalTriggerKind.Classification => "classification",
        SignalTriggerKind.BudgetRevision => "budget-revision",
        SignalTriggerKind.AssociationConfirmed => "association-confirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown trigger kind."),
    };

    internal sealed record TriggerPayload(string Kind, string SubjectEntity, Guid SubjectId, Guid SourceId);
}

/// <summary>
/// The ADR-004 <see cref="IJobHandler"/> behind the three event triggers (task 032; D-12, D-48). It evaluates the Decide-lane
/// policies for ONE subject by calling <see cref="SignalReevaluationJob.EvaluateSubjectAsync"/> - the nightly job's own
/// evaluate, plan and write path, so the episode-scoped key, the re-raise rule, secure-child ownership (D-33) and the
/// "never close a Resolved Signal" rule apply exactly as nightly. It contains no evaluation logic of its own.
/// </summary>
/// <remarks>
/// <para><b>No silent failure.</b> A payload that cannot be read is Poisoned (a retry cannot fix it) and logged at Error. A
/// pass that could not read or write something is a Failure: the message is abandoned and redelivered, then dead-lettered after
/// the last delivery by <c>ServiceBusJobProcessor</c>. Every such path logs with <see cref="SignalTriggerEvents.HandlerFailed"/>.</para>
/// <para><b>Placement (ADR-004, ADR-052).</b> A queue consumer on the existing main-queue seam; the evaluator is not called from
/// the classification path, so enrichment latency is not coupled to evaluation.</para>
/// <para>§11: <i>Existing</i> - the 58 other handlers are unrelated domains. <i>Extension</i> - the seam and the evaluator are
/// reused; only the glue is new. <i>Cost of doing nothing</i> - a fee-change email surfaces the next morning, and a Work Item keeps
/// asserting "no budget revision" after one was recorded.</para>
/// </remarks>
public sealed class SignalEvaluationTriggerHandler : IJobHandler
{
    private readonly SignalReevaluationJob _job;
    private readonly ILogger<SignalEvaluationTriggerHandler> _logger;

    public SignalEvaluationTriggerHandler(SignalReevaluationJob job, ILogger<SignalEvaluationTriggerHandler> logger)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => SignalEvaluationTrigger.JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        if (!TryReadPayload(job, out var kind, out var subjectEntity, out var subjectId))
        {
            _logger.LogError(SignalTriggerEvents.HandlerFailed,
                "[SIGNAL-TRIGGER] job {JobId} has an unreadable payload; it is poisoned (a retry cannot fix it).", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid signal trigger payload", job.Attempt, stopwatch.Elapsed);
        }

        try
        {
            var result = await _job.EvaluateSubjectAsync(subjectEntity, subjectId, ct).ConfigureAwait(false);
            if (result.NeedsRetry)
            {
                _logger.LogError(SignalTriggerEvents.HandlerFailed,
                    "[SIGNAL-TRIGGER] {Kind} trigger for {SubjectEntity} {SubjectId} did not finish (policyFailures={PolicyFailures}, " +
                    "refused={Refused}); it will be redelivered (attempt {Attempt}, delivery {Delivery}).",
                    kind, subjectEntity, subjectId, result.PolicyFailures, result.Refused, job.Attempt, job.DeliveryCount);
                return JobOutcome.Failure(job.JobId, JobType,
                    $"policyFailures={result.PolicyFailures}, refused={result.Refused}", job.Attempt, stopwatch.Elapsed);
            }

            _logger.LogInformation(SignalTriggerEvents.HandlerCompleted,
                "[SIGNAL-TRIGGER] {Kind} trigger for {SubjectEntity} {SubjectId} done: policies={Policies} hits={Hits} " +
                "created={Created} updated={Updated} closed={Closed} invalidPolicies={Invalid} scopedNotEvaluated={Scoped} " +
                "durationMs={DurationMs} correlationId={CorrelationId}",
                kind, subjectEntity, subjectId, result.PoliciesEvaluated, result.Hits, result.Created, result.Updated,
                result.Closed, result.InvalidPolicies, result.ScopedNotEvaluated, (long)stopwatch.Elapsed.TotalMilliseconds,
                job.CorrelationId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(SignalTriggerEvents.HandlerFailed, ex,
                "[SIGNAL-TRIGGER] {Kind} trigger for {SubjectEntity} {SubjectId} threw ({ExceptionType}); it will be redelivered " +
                "(attempt {Attempt}, delivery {Delivery}).",
                kind, subjectEntity, subjectId, ex.GetType().Name, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
        }
    }

    private static bool TryReadPayload(JobContract job, out string kind, out string subjectEntity, out Guid subjectId)
    {
        kind = string.Empty;
        subjectEntity = string.Empty;
        subjectId = Guid.Empty;
        if (job.Payload?.RootElement is not { ValueKind: JsonValueKind.Object } root) return false;

        kind = Read(root, "kind") ?? string.Empty;
        subjectEntity = Read(root, "subjectEntity") ?? string.Empty;
        return IsKnownKind(kind)
               && subjectEntity.Length > 0
               && Guid.TryParse(Read(root, "subjectId"), out subjectId)
               && subjectId != Guid.Empty;
    }

    private static string? Read(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsKnownKind(string kind) =>
        Enum.GetValues<SignalTriggerKind>().Any(k => string.Equals(SignalEvaluationTrigger.KindName(k), kind, StringComparison.Ordinal));
}
