using System.Collections.Concurrent;

namespace Spaarke.Scheduling.Tests;

/// <summary>
/// Test double for a distributed <see cref="IScheduledJobLease"/> — one instance shared by several hosts plays the
/// role of Redis. Expiry runs on the test's <see cref="TimeProvider"/>, with Redis's semantics: set-if-absent with
/// an expiry, extend and release only by the holder's token.
/// </summary>
internal sealed class FakeDistributedLease : IScheduledJobLease
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (string Token, DateTimeOffset ExpiresAt)> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastOccurrence = new(StringComparer.Ordinal);
    private int _acquireAttempts;
    private int _renewals;
    private int _failNextAcquires;

    public FakeDistributedLease(TimeProvider time) => _time = time;

    public bool IsDistributed => true;

    /// <summary>Every call throws <see cref="ScheduledJobLeaseUnavailableException"/> — the store is down.</summary>
    public volatile bool Unavailable;

    /// <summary>Renewals throw — a holder whose renewals never arrive, as if its process had died.</summary>
    public volatile bool RefuseRenewals;

    public int AcquireAttempts => Volatile.Read(ref _acquireAttempts);

    public int Renewals => Volatile.Read(ref _renewals);

    public ConcurrentQueue<TimeSpan> RequestedDurations { get; } = new();

    /// <summary>Every grant, with the virtual time it was made — assert against this, not the test thread's clock.</summary>
    public ConcurrentQueue<(string JobId, string Token, DateTimeOffset At)> Grants { get; } = new();

    /// <summary>The lease lapses — as if Redis restarted empty — without anyone else taking it.</summary>
    public void Expire(string jobId)
    {
        lock (_gate)
        {
            _leases.Remove(jobId);
        }
    }

    /// <summary>The next <paramref name="count"/> acquires find the store down; later ones reach it.</summary>
    public void FailNextAcquires(int count) => Volatile.Write(ref _failNextAcquires, count);

    /// <summary>The live holder's token, or <c>null</c> when nobody holds the lease.</summary>
    public string? HolderOf(string jobId)
    {
        lock (_gate)
        {
            return Live(jobId, out var lease) ? lease.Token : null;
        }
    }

    /// <summary>Records an occurrence as dispatched — as if another instance had run it and already released the lease.</summary>
    public void MarkDispatched(string jobId, DateTimeOffset occurrence)
    {
        lock (_gate)
        {
            _lastOccurrence[jobId] = occurrence;
        }
    }

    /// <summary>Another holder takes the lease out from under the current one.</summary>
    public void Steal(string jobId)
    {
        lock (_gate)
        {
            _leases[jobId] = ("thief", _time.GetUtcNow() + TimeSpan.FromHours(1));
        }
    }

    public Task<ScheduledJobLeaseGrant> TryAcquireAsync(
        string jobId,
        DateTimeOffset? occurrenceUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _acquireAttempts);
        RequestedDurations.Enqueue(duration);
        if (Unavailable || Interlocked.Decrement(ref _failNextAcquires) >= 0)
        {
            throw new ScheduledJobLeaseUnavailableException("fake lease store is down");
        }

        lock (_gate)
        {
            if (Live(jobId, out _))
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
            var now = _time.GetUtcNow();
            _leases[jobId] = (token, now + duration);
            Grants.Enqueue((jobId, token, now));
            return Task.FromResult(ScheduledJobLeaseGrant.Granted(token));
        }
    }

    public Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (Unavailable || RefuseRenewals)
        {
            throw new ScheduledJobLeaseUnavailableException("fake lease store is down");
        }

        lock (_gate)
        {
            Interlocked.Increment(ref _renewals);
            if (!Live(jobId, out var lease) || lease.Token != token)
            {
                return Task.FromResult(false);
            }

            _leases[jobId] = (token, _time.GetUtcNow() + duration);
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new ScheduledJobLeaseUnavailableException("fake lease store is down");
        }

        lock (_gate)
        {
            if (Live(jobId, out var lease) && lease.Token == token)
            {
                _leases.Remove(jobId);
            }
        }

        return Task.CompletedTask;
    }

    private bool Live(string jobId, out (string Token, DateTimeOffset ExpiresAt) lease)
    {
        if (_leases.TryGetValue(jobId, out lease) && lease.ExpiresAt > _time.GetUtcNow())
        {
            return true;
        }

        _leases.Remove(jobId);
        lease = default;
        return false;
    }
}
