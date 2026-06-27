using System.Collections.Concurrent;

namespace Spaarke.Scheduling;

/// <summary>
/// In-memory registry mapping <see cref="IScheduledJob.JobId"/> → <see cref="IScheduledJob"/>
/// instance. Singleton-scoped; populated at DI registration time so the
/// <see cref="ScheduledJobHost"/> can look up handlers by the <c>sprk_handlertype</c> /
/// <see cref="BackgroundJobDefinition.JobId"/> key from <c>sprk_backgroundjob</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per spec.md FR-2.1 (R3 Background-Job Infrastructure). Lookups are O(1) and the public surface
/// is intentionally minimal — register at startup, resolve at dispatch time, enumerate for
/// the admin <c>GET /api/admin/jobs</c> endpoint (P3 task 020).
/// </para>
/// <para>Thread-safe (ConcurrentDictionary). Duplicate registrations for the same JobId throw —
/// a single host instance MUST own one canonical handler per JobId.</para>
/// </remarks>
public sealed class ScheduledJobRegistry
{
    private readonly ConcurrentDictionary<string, IScheduledJob> _jobs = new(StringComparer.Ordinal);

    /// <summary>Register a job instance. Throws if another job with the same <see cref="IScheduledJob.JobId"/> is already registered.</summary>
    public void Register(IScheduledJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(job.JobId);

        if (!_jobs.TryAdd(job.JobId, job))
        {
            throw new InvalidOperationException(
                $"Scheduled job '{job.JobId}' is already registered. Each JobId must map to exactly one IScheduledJob instance.");
        }
    }

    /// <summary>Lookup a registered job by id, or <c>null</c> if not registered.</summary>
    public IScheduledJob? Resolve(string jobId)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>Enumerate every registered job. Snapshot — safe to enumerate while other registrations occur.</summary>
    public IReadOnlyCollection<IScheduledJob> EnumerateAll() => _jobs.Values.ToArray();

    /// <summary>Count of registered jobs (cheap; primarily for diagnostics + admin surfacing).</summary>
    public int Count => _jobs.Count;
}
