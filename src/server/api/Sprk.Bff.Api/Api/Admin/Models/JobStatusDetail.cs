namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Detail-view DTO returned by <c>GET /api/admin/jobs/{jobId}/status</c> — surfaces the same
/// summary fields as <see cref="JobStatusSummary"/> plus the last <c>N</c> run records (default 10).
/// </summary>
/// <remarks>
/// Per R3 spec.md FR-2.6 (admin endpoints under <c>/api/admin/jobs/*</c>). Behind the existing
/// <c>SystemAdmin</c> policy per Q6 owner clarification. The recent-runs slice supports AC-2.7
/// (failed-job surfacing) — the latest entry's <see cref="JobRunDetail.ErrorMessage"/> carries
/// the most-recent failure reason.
/// </remarks>
/// <param name="JobId">Stable job id matching <see cref="Spaarke.Scheduling.IScheduledJob.JobId"/>.</param>
/// <param name="DisplayName">Human-readable name for admin UI surfacing.</param>
/// <param name="Description">Short description of what the job does.</param>
/// <param name="Enabled">Whether the job is currently enabled for scheduled execution.</param>
/// <param name="CronSchedule">Cron expression parsed by Cronos.</param>
/// <param name="LastRunStartedOn">Start time of the most recent run, or <c>null</c> if never executed.</param>
/// <param name="LastRunCompletedOn">Completion time of the most recent run.</param>
/// <param name="LastRunStatus">Outcome of the most recent run.</param>
/// <param name="NextScheduledOn">Next cron occurrence computed via Cronos.</param>
/// <param name="RecentRuns">Last 10 (most-recent-first) run records for this job.</param>
public sealed record JobStatusDetail(
    string JobId,
    string DisplayName,
    string Description,
    bool Enabled,
    string CronSchedule,
    DateTimeOffset? LastRunStartedOn,
    DateTimeOffset? LastRunCompletedOn,
    string? LastRunStatus,
    DateTimeOffset? NextScheduledOn,
    IReadOnlyList<JobRunDetail> RecentRuns);
