namespace Spaarke.Scheduling;

/// <summary>
/// Identifies the trigger source for a scheduled-job run.
/// Recorded on every <c>sprk_backgroundjobrun</c> row for audit and observability.
/// </summary>
/// <remarks>Per spec.md FR-2.1 (R3 Background-Job Infrastructure).</remarks>
public enum JobRunTrigger
{
    /// <summary>Triggered by the in-process <c>ScheduledJobHost</c> on its cron cadence.</summary>
    Scheduled = 1,

    /// <summary>Triggered manually by a SystemAdmin via <c>POST /api/admin/jobs/{jobId}/trigger</c>.</summary>
    ManualAdmin = 2,

    /// <summary>Triggered once at host startup (e.g., one-shot bootstrap jobs).</summary>
    OnStartup = 3
}
