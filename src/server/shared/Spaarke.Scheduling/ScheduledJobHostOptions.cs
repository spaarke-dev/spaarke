namespace Spaarke.Scheduling;

/// <summary>
/// Operational knobs for <see cref="ScheduledJobHost"/>. Tuned per spec.md FR-2.3 (hourly
/// definition refresh) and NFR-07 (30-second graceful shutdown propagation).
/// </summary>
/// <remarks>
/// Defaults match spec.md verbatim; tests override via constructor injection to keep test
/// runtime sub-second. Permitted as a POCO under ADR-010 — no IOptions wrapper required
/// (the host accepts the instance directly).
/// </remarks>
public sealed class ScheduledJobHostOptions
{
    /// <summary>How often to re-query the <see cref="IBackgroundJobStore"/> for definition changes
    /// (new / disabled / cron-changed jobs). Default: <c>TimeSpan.FromHours(1)</c> per FR-2.3.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum time <see cref="ScheduledJobHost.StopAsync"/> will wait for in-flight jobs to drain
    /// after signalling cancellation. Default: <c>TimeSpan.FromSeconds(30)</c> per NFR-07.</summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum time the host will sleep between scheduling-loop ticks. Defends against
    /// pathological cron expressions whose next-fire is far in the future and keeps the refresh
    /// granular. Default: equal to <see cref="RefreshInterval"/>.</summary>
    public TimeSpan MaxLoopSleep { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Retry-with-backoff policy applied to a failing job execution before recording
    /// final failure. Default: <see cref="JobRetryPolicy"/> defaults (3 attempts, 5s base delay,
    /// 2min cap). Per spec.md FR-2.3.</summary>
    public JobRetryPolicy RetryPolicy { get; set; } = new();
}
