namespace Spaarke.Scheduling;

/// <summary>
/// Persistence abstraction for <see cref="ScheduledJobHost"/> — reads job definitions
/// (one row per registered job) and writes run-history records for each execution.
/// </summary>
/// <remarks>
/// <para>
/// Per spec.md FR-2.3 / FR-2.4 / FR-2.5 (R3 Background-Job Infrastructure). Decouples the host
/// from the eventual <c>sprk_backgroundjob</c> / <c>sprk_backgroundjobrun</c> Dataverse entities
/// (tasks 015 / 016 land in a later wave). Task 013 ships the in-process host + this abstraction
/// plus an in-memory implementation; the Dataverse-backed implementation lands when the entities
/// exist (task 023 PlaybookSchedulerService migration is the first real consumer).
/// </para>
/// <para>
/// Permitted as an interface under ADR-010: there are at least two implementations from
/// day one (<see cref="InMemoryBackgroundJobStore"/> for tests + early-wave usage, and a future
/// Dataverse-backed store), so the testing/swap-seam justification is concrete.
/// </para>
/// </remarks>
public interface IBackgroundJobStore
{
    /// <summary>
    /// Load the current set of registered job definitions. Called by
    /// <see cref="ScheduledJobHost"/> at startup and again on the hourly refresh tick to pick up
    /// new / disabled / cron-changed jobs without a host restart.
    /// </summary>
    /// <remarks>Returned definitions whose <see cref="BackgroundJobDefinition.Enabled"/> is
    /// <c>false</c> are visible (so they can still be inspected via the admin endpoints in P3)
    /// but the host MUST NOT schedule them.</remarks>
    Task<IReadOnlyList<BackgroundJobDefinition>> LoadJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Record the start of a run for the given job. Returns the persistent run id that will be
    /// passed back to <see cref="RecordRunCompleteAsync"/> on completion / failure.
    /// </summary>
    /// <param name="jobId">Stable job id (matches <see cref="BackgroundJobDefinition.JobId"/>).</param>
    /// <param name="trigger">What caused this run (Scheduled / ManualAdmin / OnStartup) per NFR-08.</param>
    /// <param name="correlationId">Distributed-trace correlation id flowed through to all downstream telemetry per NFR-08.</param>
    /// <param name="scheduledFireUtc">
    /// The cron-derived scheduled fire time for this tick (for <see cref="JobRunTrigger.Scheduled"/>
    /// runs), or <c>null</c> for <see cref="JobRunTrigger.ManualAdmin"/> / <see cref="JobRunTrigger.OnStartup"/>
    /// (which don't participate in tick-level idempotency). Persisted so
    /// <see cref="HasRunForScheduledTimeAsync"/> can detect duplicate dispatch after host restart
    /// (FR-2.3 idempotency requirement; tasks 015/016 add <c>sprk_backgroundjobrun.sprk_scheduledfireon</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Guid> RecordRunStartAsync(
        string jobId,
        JobRunTrigger trigger,
        string correlationId,
        DateTimeOffset? scheduledFireUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Record the completion (success or failure) of a run that was previously announced
    /// via <see cref="RecordRunStartAsync"/>.
    /// </summary>
    /// <param name="runId">Run id returned by <see cref="RecordRunStartAsync"/>.</param>
    /// <param name="result">Outcome captured by the host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordRunCompleteAsync(
        Guid runId,
        JobRunResult result,
        CancellationToken cancellationToken);

    /// <summary>
    /// Idempotency probe used by <see cref="ScheduledJobHost"/> BEFORE dispatching a scheduled
    /// tick. If a prior run exists for this <paramref name="jobId"/> + <paramref name="scheduledFireUtc"/>
    /// pair (regardless of completion status), the host MUST skip the dispatch to avoid
    /// duplicate execution after host restart (FR-2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations match equality on <paramref name="scheduledFireUtc"/> at second-precision
    /// or better — the host derives this from the cron occurrence (which has minute or
    /// second precision depending on schedule), so there is no fuzzy comparison needed.
    /// </para>
    /// <para>
    /// Returns <c>false</c> for non-scheduled triggers (manual admin / on-startup), which are
    /// not tick-deduplicated (the admin chose to retrigger; on-startup is by-design one-shot).
    /// </para>
    /// </remarks>
    /// <param name="jobId">Stable job id (matches <see cref="BackgroundJobDefinition.JobId"/>).</param>
    /// <param name="scheduledFireUtc">The cron-derived scheduled fire time being checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if a run already exists for this scheduled time, <c>false</c> otherwise.</returns>
    Task<bool> HasRunForScheduledTimeAsync(
        string jobId,
        DateTimeOffset scheduledFireUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieve the most-recent run records for <paramref name="jobId"/>, ordered newest-first,
    /// capped at <paramref name="limit"/> entries. Surfaces both completed and in-progress runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in R3 task 020 (admin endpoints) to back <c>GET /api/admin/jobs/{jobId}/status</c>
    /// and <c>GET /api/admin/jobs/{jobId}/history</c> (task 022). The Dataverse-backed
    /// implementation (task 023+) queries <c>sprk_backgroundjobrun</c> ordered by
    /// <c>sprk_startedon DESC</c>; the in-memory implementation snapshots the in-process run
    /// dictionary and sorts by <see cref="BackgroundJobRunRecord.StartedOn"/>.
    /// </para>
    /// <para>
    /// Returns an empty list (not <c>null</c>) when the job has never executed or has no
    /// recorded runs. <paramref name="limit"/> values &lt;= 0 are clamped to 1 by implementations.
    /// </para>
    /// </remarks>
    /// <param name="jobId">Stable job id (matches <see cref="BackgroundJobDefinition.JobId"/>).</param>
    /// <param name="limit">Maximum number of run records to return (newest-first). Implementations clamp to 1 if &lt;= 0.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<BackgroundJobRunRecord>> GetRecentRunsAsync(
        string jobId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mutate the <see cref="BackgroundJobDefinition.Enabled"/> flag for the named job
    /// (admin enable/disable surface — R3 task 022). Returns <c>true</c> if the definition
    /// was found + updated, <c>false</c> if no definition row exists for <paramref name="jobId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in R3 task 022 to back <c>POST /api/admin/jobs/{jobId}/enable</c> and
    /// <c>POST /api/admin/jobs/{jobId}/disable</c>. The endpoint pairs this call with
    /// <see cref="ScheduledJobHost.RefreshDefinitionsAsync"/> so the new state takes effect
    /// on the next scheduling-loop tick (not the hourly refresh — spec FR-2.6 implies
    /// runtime re-evaluation in the host).
    /// </para>
    /// <para>
    /// The Dataverse-backed implementation (task 023+) updates the <c>sprk_enabled</c>
    /// column on <c>sprk_backgroundjob</c>; the in-memory implementation mutates the in-process
    /// dictionary entry by allocating a replacement <see cref="BackgroundJobDefinition"/> with
    /// the new <c>Enabled</c> value (records are immutable so we use <c>with</c>-expression).
    /// </para>
    /// <para>
    /// Returns <c>false</c> (not throws) when the jobId is unknown so the endpoint can produce
    /// a clean ProblemDetails 404 without exception-mapping plumbing.
    /// </para>
    /// </remarks>
    /// <param name="jobId">Stable job id (matches <see cref="BackgroundJobDefinition.JobId"/>).</param>
    /// <param name="enabled">New value for <see cref="BackgroundJobDefinition.Enabled"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if a definition was found + updated; <c>false</c> if no definition exists for the jobId.</returns>
    Task<bool> SetEnabledAsync(
        string jobId,
        bool enabled,
        CancellationToken cancellationToken);
}

/// <summary>
/// Public, stable projection of one run-history row surfaced to consumers via
/// <see cref="IBackgroundJobStore.GetRecentRunsAsync"/>. Mirrors the in-memory
/// <c>InMemoryBackgroundJobStore.RunRecord</c> shape but exposes only the fields needed by
/// admin tooling (no internal correlation guardrails).
/// </summary>
/// <remarks>
/// Per R3 spec.md FR-2.6. The Dataverse-backed store (task 023+) populates this from
/// <c>sprk_backgroundjobrun</c>. <see cref="ErrorMessage"/> + <see cref="ProcessedItems"/> are
/// nullable because they're only known after run completion (and not every job reports an item
/// count). <see cref="Status"/> is canonicalized to <c>"Succeeded"</c> / <c>"Failed"</c> /
/// <c>"InProgress"</c> by implementations.
/// </remarks>
/// <param name="RunId">Persistent run identifier from the run-history store.</param>
/// <param name="JobId">Stable job id this run belongs to.</param>
/// <param name="Trigger">What caused this run (Scheduled / ManualAdmin / OnStartup).</param>
/// <param name="CorrelationId">Distributed-trace correlation id per NFR-08.</param>
/// <param name="StartedOn">When the run started executing.</param>
/// <param name="CompletedOn">When the run completed, or <c>null</c> if still running.</param>
/// <param name="Status">Canonical run outcome (<c>"Succeeded"</c>, <c>"Failed"</c>, or <c>"InProgress"</c>).</param>
/// <param name="ErrorMessage">Final failure message, or <c>null</c> on success or while in-progress.</param>
/// <param name="ProcessedItems">Optional item-processing count (per <see cref="JobRunResult.ProcessedItems"/>).</param>
/// <param name="Duration">Run duration. <see cref="TimeSpan.Zero"/> while in-progress.</param>
/// <param name="ResultJson">
/// Optional handler-specific output JSON blob (per <see cref="JobRunResult.ResultJson"/>; added R3 task 023 for FR-2.8).
/// <c>null</c> while in-progress, on failure-without-payload, or when the handler doesn't surface structured output.
/// Persisted by the Dataverse-backed store to <c>sprk_backgroundjobrun.sprk_resultjson</c>.
/// </param>
public sealed record BackgroundJobRunRecord(
    Guid RunId,
    string JobId,
    JobRunTrigger Trigger,
    string CorrelationId,
    DateTimeOffset StartedOn,
    DateTimeOffset? CompletedOn,
    string Status,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration,
    string? ResultJson = null);

/// <summary>
/// Immutable view of one row in (eventually) <c>sprk_backgroundjob</c>. Returned by
/// <see cref="IBackgroundJobStore.LoadJobsAsync"/> for the <see cref="ScheduledJobHost"/> to schedule.
/// </summary>
/// <param name="JobId">Stable job id (matches the <see cref="IScheduledJob.JobId"/> registered in DI).</param>
/// <param name="DisplayName">Human-readable name for admin UI surfacing.</param>
/// <param name="Description">Short description of what the job does.</param>
/// <param name="Enabled">If <c>false</c>, the host MUST NOT schedule the job (but it remains visible to admin tooling).</param>
/// <param name="CronSchedule">Cron expression parsed by Cronos (e.g., <c>"0 2 * * *"</c> = daily at 02:00 UTC).</param>
/// <param name="ConfigJson">Optional opaque per-job configuration blob; flowed verbatim into <see cref="JobRunContext.Parameters"/>.</param>
public record BackgroundJobDefinition(
    string JobId,
    string DisplayName,
    string Description,
    bool Enabled,
    string CronSchedule,
    string? ConfigJson);
