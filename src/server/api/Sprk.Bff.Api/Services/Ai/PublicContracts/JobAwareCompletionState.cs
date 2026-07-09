using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// ─────────────────────────────────────────────────────────────────────────────
// JobAwareCompletionState v1 — Phase-A0 walking-skeleton contract
// (spec FR-A0-07; design D-F2 / R20; task AIR2-014).
//
// WHAT THIS IS: a versioned, tolerant-reader PROJECTION over the EXISTING Spaarke
// job pipeline — Sprk.Bff.Api.Services.Jobs.JobContract + JobStatus (JobOutcome.cs)
// + the disposition ServiceBusJobProcessor already makes (Complete → Abandon(retry)
// → DeadLetter(poison)). It introduces NO new job model. It exists so OutcomeCards
// can show durable multi-step progress and so the core can enforce the R-2
// ingestion-parity invariant (NFR-12): a bare `sprk_document` row is NOT, by itself,
// a successful document operation — "record created" must be distinguishable from
// "downstream analysis / indexing finished".
//
// CONSUMER-DECLARED STEP SET: this contract is GENERIC over the ordered step set.
// A consumer declares its own ordered steps. Compose declares
// `container → record → profile-analysis → indexing`; a different consumer may
// declare `draft → review → send`. The contract MUST NOT hardcode any one
// consumer's steps — see JobAwareCompletionStateProjector + the contract test.
//
// STORE-BEFORE-RENDER (ADR-040): every input to the projector is a STORED job
// signal. The projector will NOT render `Completed` for a step unless a stored
// JobStatus.Completed backs it — model-claimed progress can never manufacture a
// terminal state. This is the load-bearing invariant behind the negative test.
//
// PLACEMENT (ADR-013 / root CLAUDE.md §10): lives under Services/Ai/PublicContracts/
// so Compose r2 and other CRUD/AI consumers bind to it via the facade, never to
// AI- or job-internal types directly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The nine per-step async completion states a <see cref="JobAwareCompletionState"/>
/// projects the existing job pipeline into. Serialized as stable strings (a
/// tolerant reader tolerates unknown future members without a numeric-reordering
/// hazard).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobAwareState
{
    /// <summary>Submitted / enqueued, not yet picked up. No stored outcome, not started.</summary>
    Queued,

    /// <summary>Picked up and processing. Started, no terminal outcome stored yet.</summary>
    Running,

    /// <summary>
    /// AGGREGATE state only: at least one step reached a terminal-success while at
    /// least one other step is still non-terminal. This is the ingestion-parity
    /// signal — "record exists" but "downstream not finished". Never a single-step
    /// state emitted by the projector.
    /// </summary>
    Partial,

    /// <summary>Terminal success. Backed by a stored <see cref="JobStatus.Completed"/>.</summary>
    Completed,

    /// <summary>
    /// Terminal failure that will NOT be auto-retried (a stored <see cref="JobStatus.Failed"/>
    /// with no attempts remaining, but not dead-lettered). User re-trigger is the recovery.
    /// </summary>
    Failed,

    /// <summary>
    /// Terminal failure, dead-lettered after max attempts. Backed by a stored
    /// <see cref="JobStatus.Poisoned"/> (or the processor's max-attempts dead-letter path).
    /// </summary>
    Poisoned,

    /// <summary>Terminal: cancelled before completion (stored cancellation signal).</summary>
    Cancelled,

    /// <summary>
    /// Non-terminal: a transient <see cref="JobStatus.Failed"/> with attempts remaining —
    /// the processor abandons the message for automatic redelivery.
    /// </summary>
    RetryPending,

    /// <summary>
    /// Non-terminal: paused awaiting a user decision / confirmation gate (ADR-041).
    /// A stored gate signal, not a job-model field.
    /// </summary>
    UserActionRequired,
}

/// <summary>One consumer-declared step and its projected <see cref="JobAwareState"/>.</summary>
public sealed record JobStepCompletion
{
    /// <summary>Stable, consumer-declared step key (e.g. <c>record</c>, <c>indexing</c>, <c>send</c>).</summary>
    [JsonPropertyName("stepName")]
    public required string StepName { get; init; }

    /// <summary>The projected state for this step.</summary>
    [JsonPropertyName("state")]
    public required JobAwareState State { get; init; }

    /// <summary>Optional human/diagnostic detail (never a completion claim — see store-before-render).</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

/// <summary>
/// v1 projection of the existing job pipeline into the nine per-step states over a
/// consumer-declared ordered step set, plus the computed aggregate. Tolerant-reader:
/// deserializers MUST ignore unknown fields (default System.Text.Json behavior) so
/// additive v2 fields don't break v1 consumers.
/// </summary>
public sealed record JobAwareCompletionState
{
    /// <summary>The current contract schema version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Schema version (tolerant reader: consumers read the max version they know).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>The projected job's id (from the existing <see cref="JobContract.JobId"/>).</summary>
    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    /// <summary>The projected job's type (from the existing <see cref="JobContract.JobType"/>).</summary>
    [JsonPropertyName("jobType")]
    public required string JobType { get; init; }

    /// <summary>The consumer-declared ORDERED step set with each step's projected state.</summary>
    [JsonPropertyName("steps")]
    public required IReadOnlyList<JobStepCompletion> Steps { get; init; }

    /// <summary>The computed operation-level aggregate state (see <see cref="ComputeAggregate"/>).</summary>
    [JsonPropertyName("aggregate")]
    public required JobAwareState Aggregate { get; init; }

    /// <summary>When this projection was observed (store-before-render timestamp).</summary>
    [JsonPropertyName("observedAt")]
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>True when the WHOLE operation is durably finished (aggregate == Completed).</summary>
    [JsonIgnore]
    public bool IsComplete => Aggregate == JobAwareState.Completed;

    /// <summary>The terminal / non-terminal state map.</summary>
    public static bool IsTerminal(JobAwareState state) => state is
        JobAwareState.Completed or
        JobAwareState.Failed or
        JobAwareState.Poisoned or
        JobAwareState.Cancelled;

    /// <summary>
    /// Computes the operation-level aggregate from the step states. Attention-requiring
    /// terminal/blocking states win first; otherwise a mix of done + not-yet-done steps
    /// is <see cref="JobAwareState.Partial"/> (the ingestion-parity signal), never
    /// <see cref="JobAwareState.Completed"/> until EVERY step is completed.
    /// </summary>
    public static JobAwareState ComputeAggregate(IReadOnlyList<JobStepCompletion> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return JobAwareState.Queued;
        }

        if (steps.All(s => s.State == JobAwareState.Completed))
        {
            return JobAwareState.Completed;
        }

        // Attention-requiring states surface first (most-severe wins).
        if (steps.Any(s => s.State == JobAwareState.Poisoned))
        {
            return JobAwareState.Poisoned;
        }
        if (steps.Any(s => s.State == JobAwareState.Failed))
        {
            return JobAwareState.Failed;
        }
        if (steps.Any(s => s.State == JobAwareState.Cancelled))
        {
            return JobAwareState.Cancelled;
        }
        if (steps.Any(s => s.State == JobAwareState.UserActionRequired))
        {
            return JobAwareState.UserActionRequired;
        }

        // No blocking state. If ANY step is done but not all → Partial (ingestion parity).
        if (steps.Any(s => s.State == JobAwareState.Completed))
        {
            return JobAwareState.Partial;
        }

        // Nothing done yet: Running if anything is in-flight, else all Queued.
        if (steps.Any(s => s.State is JobAwareState.Running or JobAwareState.RetryPending))
        {
            return JobAwareState.Running;
        }

        return JobAwareState.Queued;
    }
}

/// <summary>
/// A STORED per-step signal — the only input the projector accepts. Every field is a
/// value the pipeline has PERSISTED (an outcome, a delivery/started flag, an attempt
/// count, a cancellation or gate signal). There is deliberately NO "claimed complete"
/// input: store-before-render (ADR-040) means a step cannot be rendered
/// <see cref="JobAwareState.Completed"/> without a stored <see cref="JobStatus.Completed"/>.
/// </summary>
public readonly record struct StoredStepSignal
{
    /// <summary>Consumer-declared step key.</summary>
    public required string StepName { get; init; }

    /// <summary>
    /// The stored job outcome status for this step, or <c>null</c> when no outcome has
    /// been persisted yet (queued or running). This is the EXISTING three-value
    /// <see cref="JobStatus"/> — no new job model.
    /// </summary>
    public JobStatus? StoredStatus { get; init; }

    /// <summary>Whether the pipeline has recorded that processing started (message picked up).</summary>
    public bool Started { get; init; }

    /// <summary>Stored current attempt (from <see cref="JobContract.Attempt"/>).</summary>
    public int Attempt { get; init; }

    /// <summary>Stored max attempts (from <see cref="JobContract.MaxAttempts"/>).</summary>
    public int MaxAttempts { get; init; }

    /// <summary>Stored cancellation signal for this step.</summary>
    public bool CancellationRequested { get; init; }

    /// <summary>Stored confirmation-gate / user-action-required pause (ADR-041).</summary>
    public bool AwaitingUserAction { get; init; }

    /// <summary>Optional diagnostic detail carried through to the rendered step.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// The reference PRODUCER: projects a REAL <see cref="JobContract"/> plus the consumer's
/// stored per-step signals into a <see cref="JobAwareCompletionState"/>. No new job model —
/// it reads the existing <see cref="JobContract"/> and the existing <see cref="JobStatus"/>
/// and mirrors the disposition ServiceBusJobProcessor already makes.
/// </summary>
public static class JobAwareCompletionStateProjector
{
    /// <summary>
    /// Projects one stored step signal into a per-step <see cref="JobAwareState"/>.
    /// The mapping mirrors ServiceBusJobProcessor.ProcessMessageAsync:
    /// Completed → Completed; Failed with attempts remaining → RetryPending (abandon/redeliver);
    /// Failed at max attempts → Failed; Poisoned → Poisoned; no stored outcome → Running/Queued.
    /// </summary>
    public static JobAwareState ProjectStep(StoredStepSignal signal)
    {
        // A cancellation stored before a terminal-success cancels the step.
        if (signal.CancellationRequested && signal.StoredStatus != JobStatus.Completed)
        {
            return JobAwareState.Cancelled;
        }

        // A stored gate pause (no stored terminal outcome) surfaces as user-action-required.
        if (signal.AwaitingUserAction && signal.StoredStatus is null)
        {
            return JobAwareState.UserActionRequired;
        }

        return signal.StoredStatus switch
        {
            JobStatus.Completed => JobAwareState.Completed,
            JobStatus.Poisoned => JobAwareState.Poisoned,
            // Mirrors the processor: attempts remaining → abandon (retry); otherwise give up.
            JobStatus.Failed => signal.Attempt < signal.MaxAttempts
                ? JobAwareState.RetryPending
                : JobAwareState.Failed,
            // No stored outcome yet: started → running, else queued.
            null => signal.Started ? JobAwareState.Running : JobAwareState.Queued,
            _ => JobAwareState.Queued,
        };
    }

    /// <summary>
    /// Projects a real <see cref="JobContract"/> and its consumer-declared ordered step
    /// signals into a <see cref="JobAwareCompletionState"/>. The step ORDER of
    /// <paramref name="steps"/> is preserved (consumer-declared); no consumer's step set
    /// is hardcoded.
    /// </summary>
    public static JobAwareCompletionState Project(
        JobContract job,
        IReadOnlyList<StoredStepSignal> steps,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(steps);

        var stepStates = steps
            .Select(s => new JobStepCompletion
            {
                StepName = s.StepName,
                State = ProjectStep(s),
                Detail = s.Detail,
            })
            .ToList();

        return new JobAwareCompletionState
        {
            Version = JobAwareCompletionState.CurrentVersion,
            JobId = job.JobId,
            JobType = job.JobType,
            Steps = stepStates,
            Aggregate = JobAwareCompletionState.ComputeAggregate(stepStates),
            ObservedAt = observedAt,
        };
    }
}

/// <summary>
/// Recovery affordance a consumer surfaces for a non-successful aggregate.
/// </summary>
/// <param name="Kind">Machine key: <c>auto-retry | user-retry | escalate | confirm</c>.</param>
/// <param name="Message">User-facing recovery message.</param>
public sealed record JobRecoveryAffordance(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// The reference CONSUMER: a thin, allocation-light view that renders a
/// <see cref="JobAwareCompletionState"/> for progress, failure recovery, and — critically —
/// the ingestion-parity distinction (record-exists vs. downstream-finished). Compose r2
/// can consume this with zero local variant.
/// </summary>
public sealed class JobAwareCompletionStateView
{
    private readonly JobAwareCompletionState _state;

    public JobAwareCompletionStateView(JobAwareCompletionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>Number of steps that reached terminal success.</summary>
    public int CompletedStepCount => _state.Steps.Count(s => s.State == JobAwareState.Completed);

    /// <summary>Total declared steps.</summary>
    public int TotalStepCount => _state.Steps.Count;

    /// <summary>A simple progress label, e.g. <c>"2 of 4 steps complete"</c>.</summary>
    public string ProgressLabel => $"{CompletedStepCount} of {TotalStepCount} steps complete";

    /// <summary>True when the WHOLE operation is durably finished.</summary>
    public bool DownstreamFinished => _state.IsComplete;

    /// <summary>True when the named step reached terminal success (the record exists).</summary>
    public bool RecordExists(string recordStepName) =>
        _state.Steps.Any(s => s.StepName == recordStepName && s.State == JobAwareState.Completed);

    /// <summary>
    /// The ingestion-parity guard (NFR-12): the record exists (its step completed) but the
    /// overall operation has NOT finished — the consumer MUST NOT report success.
    /// </summary>
    public bool RecordCreatedButDownstreamPending(string recordStepName) =>
        RecordExists(recordStepName) && !DownstreamFinished;

    /// <summary>A recovery affordance for a non-successful aggregate, or <c>null</c> when progressing/complete.</summary>
    public JobRecoveryAffordance? Recovery => _state.Aggregate switch
    {
        JobAwareState.RetryPending => new("auto-retry", "Retrying automatically…"),
        JobAwareState.Failed => new("user-retry", "This step failed. Retry?"),
        JobAwareState.Poisoned => new("escalate", "This step could not be completed after retries."),
        JobAwareState.Cancelled => new("user-retry", "This operation was cancelled. Start again?"),
        JobAwareState.UserActionRequired => new("confirm", "This step needs your confirmation to continue."),
        _ => null,
    };
}
