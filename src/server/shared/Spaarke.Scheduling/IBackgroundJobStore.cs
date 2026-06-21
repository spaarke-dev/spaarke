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
}

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
