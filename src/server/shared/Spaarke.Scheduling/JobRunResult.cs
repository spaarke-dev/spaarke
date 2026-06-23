namespace Spaarke.Scheduling;

/// <summary>
/// Outcome of a single <see cref="IScheduledJob"/> execution. Persisted to
/// <c>sprk_backgroundjobrun</c> by the host for run-history audit and admin inspection.
/// </summary>
/// <remarks>
/// <para>
/// Per spec.md FR-2.1 (R3 Background-Job Infrastructure). <see cref="ProcessedItems"/>
/// is optional — jobs that don't have a meaningful item count return <c>null</c>.
/// </para>
/// <para>
/// <see cref="ResultJson"/> (added R3 task 023, FR-2.8) is an optional opaque JSON blob the
/// handler can use to surface structured per-run output for the admin UI / history queries
/// (e.g., the migrated <c>PlaybookSchedulerJob</c> records each fanned-out child playbook's
/// correlationId here so operators can join parent ↔ children). Persisted verbatim to
/// <c>sprk_backgroundjobrun.sprk_resultjson</c>. Implementations MUST keep the payload small
/// (admin UI surface) — for large outputs, write to a separate audit store and put a pointer here.
/// </para>
/// </remarks>
/// <param name="Success">Whether the run completed successfully (no exception, handler returned).</param>
/// <param name="ErrorMessage">Final error message on failure (<c>null</c> on success).</param>
/// <param name="ProcessedItems">Optional item-processing count for jobs with a meaningful unit count.</param>
/// <param name="Duration">Total wall-clock duration of the run (sum across retry attempts for retried jobs).</param>
/// <param name="ResultJson">
/// Optional handler-specific output JSON blob (NFR-08 / FR-2.8). <c>null</c> if the handler doesn't
/// surface structured output. Persisted to <c>sprk_backgroundjobrun.sprk_resultjson</c>.
/// </param>
public record JobRunResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration,
    string? ResultJson = null);
