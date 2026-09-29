namespace Spaarke.Scheduling;

/// <summary>
/// The mutual-exclusion lease <see cref="ScheduledJobHost"/> takes around every dispatch of a job — a scheduled
/// tick or a manual trigger — so a job runs on one instance at a time, and each cron occurrence is dispatched once
/// across every instance and deployment slot (ADR-036 A1 rule 1).
/// </summary>
/// <remarks>
/// <para><b>It belongs to the host, never to the job.</b> An <see cref="IScheduledJob"/> must not depend on it
/// (ADR-036 A1 rule 7); <c>WorkloadPlacementGuardTests.ScheduledJobsAreHostNeutral</c> enforces that.</para>
/// <para><b>Two guarantees, one call.</b> <see cref="TryAcquireAsync"/> takes a per-job lease with an atomic
/// set-if-absent-with-expiry and, for a scheduled tick, records the occurrence it is about to dispatch — refusing one
/// already dispatched. The lease alone is not enough: a short run can finish and release before a slower instance
/// wakes for the same occurrence, which would then run it again. Recording the last dispatched occurrence is the same
/// device the Azure Functions timer trigger uses (its schedule status "Last").</para>
/// <para><b>Liveness.</b> The lease is taken for a finite duration and kept alive with <see cref="RenewAsync"/> while
/// the run — every retry attempt and backoff included — is in flight. A holder that dies stops renewing, and the lease
/// expires on its own, so a later tick is never blocked for good.</para>
/// <para>An interface under ADR-010 because two implementations exist from day one: a distributed one (Redis, in the
/// BFF) and <see cref="ProcessLocalScheduledJobLease"/>.</para>
/// </remarks>
public interface IScheduledJobLease
{
    /// <summary>
    /// <c>true</c> when every instance shares the lease (a distributed store). <c>false</c> when it is exclusive
    /// within this process only — then every instance dispatches every tick, and the host warns once.
    /// </summary>
    bool IsDistributed { get; }

    /// <summary>
    /// Takes the lease for <paramref name="jobId"/>. For a scheduled tick, also records
    /// <paramref name="occurrenceUtc"/> as dispatched, or refuses it if that occurrence (or a later one) already was.
    /// </summary>
    /// <param name="jobId">The job whose lease to take.</param>
    /// <param name="occurrenceUtc">The cron occurrence being dispatched; <c>null</c> for a manual trigger.</param>
    /// <param name="duration">How long the lease lives unless renewed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The grant — a holder token when <see cref="ScheduledJobLeaseStatus.Granted"/>.</returns>
    /// <exception cref="ScheduledJobLeaseUnavailableException">The store cannot be reached.</exception>
    Task<ScheduledJobLeaseGrant> TryAcquireAsync(
        string jobId,
        DateTimeOffset? occurrenceUtc,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extends a lease this holder owns. Returns <c>false</c> when it no longer does — it expired, or another holder
    /// took it.
    /// </summary>
    /// <exception cref="ScheduledJobLeaseUnavailableException">The store cannot be reached.</exception>
    Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>Releases the lease if, and only if, <paramref name="token"/> still holds it.</summary>
    /// <exception cref="ScheduledJobLeaseUnavailableException">The store cannot be reached.</exception>
    Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IScheduledJobLease.TryAcquireAsync"/>.</summary>
public enum ScheduledJobLeaseStatus
{
    /// <summary>The lease is ours; dispatch.</summary>
    Granted = 1,

    /// <summary>Another run of this job holds the lease — on this instance or another.</summary>
    HeldElsewhere = 2,

    /// <summary>This occurrence, or a later one, was already dispatched (by another instance).</summary>
    OccurrenceAlreadyDispatched = 3,
}

/// <summary>Outcome of <see cref="IScheduledJobLease.TryAcquireAsync"/>: a status, and a holder token when granted.</summary>
/// <param name="Status">Whether the lease was granted, and if not, why.</param>
/// <param name="Token">The holder token to renew and release with; <c>null</c> unless granted.</param>
public readonly record struct ScheduledJobLeaseGrant(ScheduledJobLeaseStatus Status, string? Token)
{
    /// <summary>The lease was granted to <paramref name="token"/>.</summary>
    public static ScheduledJobLeaseGrant Granted(string token) => new(ScheduledJobLeaseStatus.Granted, token);

    /// <summary>Another run holds the lease.</summary>
    public static ScheduledJobLeaseGrant Held { get; } = new(ScheduledJobLeaseStatus.HeldElsewhere, null);

    /// <summary>The occurrence was already dispatched.</summary>
    public static ScheduledJobLeaseGrant AlreadyDispatched { get; } =
        new(ScheduledJobLeaseStatus.OccurrenceAlreadyDispatched, null);
}

/// <summary>
/// The lease store is configured but cannot be reached. For a scheduled tick the host retries the acquire briefly,
/// then does not dispatch and records the tick as failed (owner decision, 2026-09-14 — task 103); a manual trigger
/// fails at once.
/// </summary>
public sealed class ScheduledJobLeaseUnavailableException : Exception
{
    /// <summary>Construct with a message and the store failure that caused it.</summary>
    public ScheduledJobLeaseUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown by <see cref="ScheduledJobHost.TriggerNowAsync"/> when the job is already running — a scheduled tick or
/// another manual trigger holds its lease (ADR-036 A1 rule 1). The admin endpoint maps it to 409 Conflict.
/// </summary>
public sealed class ScheduledJobBusyException : Exception
{
    /// <summary>The job that is already running.</summary>
    public string JobId { get; }

    /// <summary>Construct with the standard message.</summary>
    public ScheduledJobBusyException(string jobId)
        : base($"Scheduled job '{jobId}' is already running — a scheduled tick or another manual trigger holds its lease. Try again when that run completes.")
    {
        JobId = jobId;
    }
}
