namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// List-view DTO returned by <c>GET /api/admin/jobs</c> — one row per registered background job.
/// Combines job definition (from <see cref="Spaarke.Scheduling.IBackgroundJobStore.LoadJobsAsync"/>)
/// with the most-recent run summary and the next computed cron occurrence (via Cronos).
/// </summary>
/// <remarks>
/// Per R3 spec.md FR-2.6 (admin endpoints under <c>/api/admin/jobs/*</c>). Behind the existing
/// <c>SystemAdmin</c> policy per Q6 owner clarification (NOT a new <c>PlatformAdmin</c> policy).
/// </remarks>
/// <param name="JobId">Stable job id matching <see cref="Spaarke.Scheduling.IScheduledJob.JobId"/>.</param>
/// <param name="DisplayName">Human-readable name for admin UI surfacing.</param>
/// <param name="Description">Short description of what the job does.</param>
/// <param name="Enabled">Whether the job is currently enabled for scheduled execution.</param>
/// <param name="CronSchedule">Cron expression parsed by Cronos (5-field minute-precision or 6-field seconds).</param>
/// <param name="LastRunStartedOn">Start time of the most recent run, or <c>null</c> if never executed.</param>
/// <param name="LastRunCompletedOn">Completion time of the most recent run, or <c>null</c> if never completed (or still running).</param>
/// <param name="LastRunStatus">Outcome of the most recent run (<c>"Succeeded"</c>, <c>"Failed"</c>, <c>"InProgress"</c>, or <c>null</c> if never executed).</param>
/// <param name="NextScheduledOn">Next cron occurrence computed from <see cref="CronSchedule"/>, or <c>null</c> if disabled or cron-unparseable.</param>
public sealed record JobStatusSummary(
    string JobId,
    string DisplayName,
    string Description,
    bool Enabled,
    string CronSchedule,
    DateTimeOffset? LastRunStartedOn,
    DateTimeOffset? LastRunCompletedOn,
    string? LastRunStatus,
    DateTimeOffset? NextScheduledOn);
