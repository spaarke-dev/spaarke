namespace Spaarke.Scheduling;

/// <summary>
/// Retry-with-exponential-backoff policy applied by <see cref="ScheduledJobHost"/> to a
/// failing <see cref="IScheduledJob.ExecuteAsync"/> invocation. POCO — defaults shipped with
/// the host options; per-job override is not supported in R3 (every job inherits the host
/// default to keep operational surface predictable per ADR-010 minimalism).
/// </summary>
/// <remarks>
/// <para><b>Spec coverage:</b></para>
/// <list type="bullet">
///   <item>FR-2.3 — "Handles failure, retry (with backoff), idempotency."</item>
///   <item>NFR-08 — Final failure record carries the captured <see cref="JobRunResult.ErrorMessage"/>
///     surfaced through <see cref="IBackgroundJobStore.RecordRunCompleteAsync"/> and ultimately
///     <c>sprk_backgroundjobrun.sprk_errormessage</c> (entities ship in tasks 015/016).</item>
/// </list>
/// <para><b>Algorithm:</b> classical exponential backoff with NO jitter — the in-process scheduler
/// has a single caller per job per tick (versus distributed HTTP clients where jitter prevents
/// thundering herds), so deterministic delays are easier to reason about for tests + ops.
/// Formula: <c>delay = BaseDelay * 2^(attemptNumber-1)</c>, capped at <see cref="MaxDelay"/>.</para>
/// <para>Distinct from <c>.claude/patterns/api/resilience.md</c> (Polly + jitter) because:
/// <list type="bullet">
///   <item>Scheduled-job execution is in-process, not HTTP — Polly's middleware shape is overkill.</item>
///   <item>No Retry-After header semantics (no external rate-limited service).</item>
///   <item>The host already retries the entire tick on the next cron cadence — short retry burst
///     is the right tool to absorb transient blips; long backoff is the cron schedule itself.</item>
/// </list></para>
/// </remarks>
public sealed class JobRetryPolicy
{
    /// <summary>Total attempts including the first call. Default 3 = first call + 2 retries.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Delay before the second attempt. Default 5s.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound on any single retry delay. Default 2 minutes.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Compute the delay to wait BEFORE the given attempt number. Attempt 1 is the initial
    /// call (no delay) — callers compute delay for attempt 2, 3, ... only.
    /// Formula: <c>BaseDelay * 2^(attemptNumber-1)</c>, capped at <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attemptNumber">1-based attempt number that is about to start. Must be &gt;= 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="attemptNumber"/> &lt; 1.</exception>
    public TimeSpan ComputeDelay(int attemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1, nameof(attemptNumber));

        // Attempt 1 has no prior delay; attempt 2 = BaseDelay; attempt 3 = BaseDelay * 2; etc.
        // We compute via doubles to avoid TimeSpan tick overflow on absurd attempt numbers,
        // then cap at MaxDelay.
        if (attemptNumber == 1)
        {
            return TimeSpan.Zero;
        }

        var exponent = attemptNumber - 2; // attempt 2 -> exponent 0 -> BaseDelay * 1
        var multiplier = Math.Pow(2, exponent);
        var rawTicks = BaseDelay.Ticks * multiplier;

        if (double.IsInfinity(rawTicks) || rawTicks >= MaxDelay.Ticks)
        {
            return MaxDelay;
        }

        return new TimeSpan((long)rawTicks);
    }
}
