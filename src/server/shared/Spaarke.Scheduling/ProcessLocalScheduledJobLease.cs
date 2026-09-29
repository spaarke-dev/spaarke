namespace Spaarke.Scheduling;

/// <summary>
/// <see cref="IScheduledJobLease"/> that is exclusive within this process only. Used when no distributed lease store
/// is configured — single-instance development — and as <see cref="ScheduledJobHost"/>'s default.
/// </summary>
/// <remarks>
/// Within one process it keeps the same promises as the distributed lease: a manual trigger and a scheduled tick of a
/// job never overlap, and an occurrence is dispatched once. Across instances it promises nothing, which is why
/// <see cref="IsDistributed"/> is <c>false</c> and the host warns once. No expiry is needed: the lease dies with the
/// process that holds it.
/// </remarks>
public sealed class ProcessLocalScheduledJobLease : IScheduledJobLease
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _holders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastOccurrence = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsDistributed => false;

    /// <inheritdoc />
    public Task<ScheduledJobLeaseGrant> TryAcquireAsync(
        string jobId,
        DateTimeOffset? occurrenceUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_holders.ContainsKey(jobId))
            {
                return Task.FromResult(ScheduledJobLeaseGrant.Held);
            }

            if (occurrenceUtc is { } occurrence)
            {
                if (_lastOccurrence.TryGetValue(jobId, out var last) && last >= occurrence)
                {
                    return Task.FromResult(ScheduledJobLeaseGrant.AlreadyDispatched);
                }

                _lastOccurrence[jobId] = occurrence;
            }

            var token = Guid.NewGuid().ToString("N");
            _holders[jobId] = token;
            return Task.FromResult(ScheduledJobLeaseGrant.Granted(token));
        }
    }

    /// <inheritdoc />
    public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_holders.TryGetValue(jobId, out var holder) && holder == token);
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_holders.TryGetValue(jobId, out var holder) && holder == token)
            {
                _holders.Remove(jobId);
            }
        }

        return Task.CompletedTask;
    }
}
