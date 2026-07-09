namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// ─────────────────────────────────────────────────────────────────────────────
// JobAwareOutcomeProjection — integrate JobAwareCompletionState v1 (task 014) onto
// the OutcomeCard v1 contract (task 011) and ENFORCE the R-2 ingestion-parity
// invariant (spec FR-A1-07 / NFR-12; task AIR2-036).
//
// WHAT THIS IS: the missing LINK between two already-shipped A0 contracts. Task 014
// projects the EXISTING Spaarke job pipeline (JobContract + JobStatus +
// ServiceBusJobProcessor disposition) into a JobAwareCompletionState; task 011 ships
// the OutcomeCard render contract. Nothing bridged them. This projection maps a
// JobAwareCompletionState onto an OutcomeCard's OutcomeStatus + OutcomeCompletion so a
// document-creating / analysis / indexing / Compose save-back operation renders durable
// per-step async progress, and — critically — so a bare `sprk_document` row (its record
// step completed, its downstream indexing still queued/running) can NEVER be rendered as
// a successful document operation.
//
// NO NEW JOB MODEL (CLAUDE.md §11 default-to-reuse; plan §3): this is a pure, allocation-
// light PROJECTION over the task-014 contract. It reads no store, opens no cache, and adds
// no field to either contract. The only inputs are values the task-014 projector already
// derived from STORED job signals (store-before-render — ADR-040). It introduces no second
// state store and no per-surface job-status cache (ADR-040 MUST NOT).
//
// INGESTION PARITY BY CONSTRUCTION (NFR-12): the operation-level OutcomeStatus is DERIVED
// from the job aggregate, never chosen by a caller. `ToOutcomeStatus` maps the ingestion-
// parity aggregate `Partial` (some step done, not all) to OutcomeStatus.Partial — never
// Succeeded. `ForJobAwareOutcome` builds the whole card from the derived status, so a
// consumer building a job-aware card CANNOT declare success while downstream work is
// pending. `EnsureIngestionParity` is the fail-loud guard for the hand-assembled path.
//
// PLACEMENT (ADR-013 / root CLAUDE.md §10): lives under Services/Ai/PublicContracts/ so
// Compose r2 (save-back progress) and other CRUD/AI consumers bind to it via the facade,
// with zero local variant — never to AI- or job-internal types directly. It depends only
// on OutcomeCard + JobAwareCompletionState (both in this facade namespace).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Integrates <see cref="JobAwareCompletionState"/> (task 014) onto the
/// <see cref="OutcomeCard"/> (task 011) and enforces the ingestion-parity invariant
/// (NFR-12): a bare record row whose downstream pipeline has not finished is never rendered
/// as a successful document operation. A pure projection — no new job model, no store, no cache.
/// </summary>
public static class JobAwareOutcomeProjection
{
    /// <summary>
    /// Projects the operation-level job <paramref name="aggregate"/> onto the OutcomeCard's
    /// terminal <see cref="OutcomeStatus"/>. This is the ingestion-parity guard: the aggregate
    /// <see cref="JobAwareState.Partial"/> — "record exists but downstream analysis/indexing has
    /// not finished" — maps to <see cref="OutcomeStatus.Partial"/> and NEVER to
    /// <see cref="OutcomeStatus.Succeeded"/>. Only a fully-<see cref="JobAwareState.Completed"/>
    /// aggregate is Succeeded (NFR-12).
    /// </summary>
    public static OutcomeStatus ToOutcomeStatus(JobAwareState aggregate) => aggregate switch
    {
        // The ONLY path to Succeeded: every declared step reached terminal success.
        JobAwareState.Completed => OutcomeStatus.Succeeded,

        // Terminal non-success — surface as a failed operation.
        JobAwareState.Failed or
        JobAwareState.Poisoned or
        JobAwareState.Cancelled => OutcomeStatus.Failed,

        // Everything else is not-yet-finished (Partial / Running / Queued / RetryPending /
        // UserActionRequired). NFR-12: an unfinished operation is never Succeeded — it is
        // Partial (in-flight / record-exists-but-downstream-pending).
        _ => OutcomeStatus.Partial,
    };

    /// <summary>
    /// Projects one per-step <see cref="JobAwareState"/> onto the OutcomeCard step vocabulary
    /// (<c>pending | running | succeeded | failed</c>, per task-011 <see cref="OutcomeStep"/>).
    /// This is a deliberately COARSE render label for the card's progress strip; the honest,
    /// fine-grained nine-state detail (retry-pending vs poisoned vs user-action-required) is
    /// preserved on the <see cref="JobAwareCompletionState"/> itself and surfaced via
    /// <see cref="JobAwareCompletionStateView.Recovery"/>. A non-terminal in-flight step
    /// (running / retry-pending / awaiting user action) renders as <c>running</c>; a queued
    /// step renders as <c>pending</c>; any terminal non-success renders as <c>failed</c>.
    /// </summary>
    public static string ToStepStatus(JobAwareState state) => state switch
    {
        JobAwareState.Completed => "succeeded",
        JobAwareState.Failed or
        JobAwareState.Poisoned or
        JobAwareState.Cancelled => "failed",
        JobAwareState.Queued => "pending",
        // Running, RetryPending, UserActionRequired (and defensively Partial) are in-flight.
        _ => "running",
    };

    /// <summary>
    /// Projects a <see cref="JobAwareCompletionState"/> into a job-aware
    /// <see cref="OutcomeCompletion"/> for an <see cref="OutcomeCard"/>. The consumer-declared
    /// ORDERED step set is preserved (task 014 is generic over steps — no consumer's step set is
    /// hardcoded). <paramref name="stepLabels"/> supplies human-readable labels keyed by step name;
    /// a step with no supplied label falls back to a humanized form of its key.
    /// </summary>
    public static OutcomeCompletion ToOutcomeCompletion(
        JobAwareCompletionState state,
        IReadOnlyDictionary<string, string>? stepLabels = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var steps = state.Steps
            .Select(s => new OutcomeStep(
                Key: s.StepName,
                Label: ResolveLabel(s.StepName, stepLabels),
                Status: ToStepStatus(s.State)))
            .ToList();

        return OutcomeCompletion.JobAware(steps);
    }

    /// <summary>
    /// Builds a complete job-aware <see cref="OutcomeCard"/> from a stored ledger outcome and a
    /// <see cref="JobAwareCompletionState"/>. The operation-level <see cref="OutcomeStatus"/> and
    /// the per-step <see cref="OutcomeCompletion"/> are DERIVED from <paramref name="state"/> — the
    /// caller does NOT get to declare the status. This makes the ingestion-parity invariant
    /// (NFR-12) hold BY CONSTRUCTION: a card built for a document-create whose indexing step is
    /// still queued carries <see cref="OutcomeStatus.Partial"/>, never Succeeded. Store-before-render
    /// (ADR-040) is inherited from <see cref="OutcomeCard.ForStoredOutcome"/> (non-empty
    /// <paramref name="ledgerOutputKey"/>) and from the task-014 projector (every step state is
    /// backed by a stored job signal).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="ledgerOutputKey"/> is null/blank (ADR-040).</exception>
    /// <exception cref="InvalidOperationException"><paramref name="link"/> is model-claimed (task 011 guard).</exception>
    public static OutcomeCard ForJobAwareOutcome(
        string ledgerOutputKey,
        JobAwareCompletionState state,
        OutcomeSummary summary,
        OutcomeCardLink? link = null,
        IReadOnlyList<NextStepChip>? nextSteps = null,
        IReadOnlyDictionary<string, string>? stepLabels = null,
        string? traceRef = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(summary);

        var status = ToOutcomeStatus(state.Aggregate);
        var completion = ToOutcomeCompletion(state, stepLabels);

        return OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: ledgerOutputKey,
            status: status,
            summary: summary,
            link: link,
            nextSteps: nextSteps,
            completion: completion,
            traceRef: traceRef);
    }

    /// <summary>
    /// Fail-loud ingestion-parity guard (NFR-12) for the hand-assembled path — for a consumer that
    /// builds an <see cref="OutcomeCard"/> directly (not via <see cref="ForJobAwareOutcome"/>) yet
    /// holds the backing <see cref="JobAwareCompletionState"/>. Throws when the card declares
    /// <see cref="OutcomeStatus.Succeeded"/> while the job aggregate is not
    /// <see cref="JobAwareState.Completed"/> — i.e. a bare record row would be rendered as a
    /// successful document operation. Callers SHOULD prefer <see cref="ForJobAwareOutcome"/>, which
    /// makes this violation unrepresentable.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The declared status is Succeeded but the operation has not durably finished (NFR-12 breach).
    /// </exception>
    public static void EnsureIngestionParity(OutcomeStatus declaredStatus, JobAwareCompletionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (declaredStatus == OutcomeStatus.Succeeded && state.Aggregate != JobAwareState.Completed)
        {
            throw new InvalidOperationException(
                "Ingestion-parity invariant (NFR-12) breach: an OutcomeCard declares Succeeded while " +
                $"the job aggregate is '{state.Aggregate}' (not Completed). A bare record row whose " +
                "downstream analysis/indexing has not finished MUST NOT be rendered as a successful " +
                "document operation. Derive the status via JobAwareOutcomeProjection.ToOutcomeStatus " +
                "or build the card via ForJobAwareOutcome.");
        }
    }

    /// <summary>
    /// Convenience for the canonical ingestion-parity read: the named record step reached terminal
    /// success (the record EXISTS) but the overall operation has NOT durably finished (downstream
    /// analysis/indexing still pending). Mirrors
    /// <see cref="JobAwareCompletionStateView.RecordCreatedButDownstreamPending"/> so a caller
    /// holding only the state (not the view) can make the same distinction.
    /// </summary>
    public static bool RecordExistsButOperationUnfinished(JobAwareCompletionState state, string recordStepName)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new JobAwareCompletionStateView(state).RecordCreatedButDownstreamPending(recordStepName);
    }

    private static string ResolveLabel(string stepName, IReadOnlyDictionary<string, string>? stepLabels)
    {
        if (stepLabels is not null && stepLabels.TryGetValue(stepName, out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return Humanize(stepName);
    }

    /// <summary>
    /// Turns a consumer-declared step key (e.g. <c>profile-analysis</c>, <c>profile_analysis</c>)
    /// into a human label (<c>Profile analysis</c>) when no explicit label was supplied.
    /// </summary>
    private static string Humanize(string stepName)
    {
        if (string.IsNullOrWhiteSpace(stepName))
        {
            return stepName;
        }

        var spaced = stepName.Replace('-', ' ').Replace('_', ' ').Trim();
        if (spaced.Length == 0)
        {
            return stepName;
        }

        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
