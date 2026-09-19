using Microsoft.Extensions.Logging;

namespace Spaarke.Scheduling;

/// <summary>
/// A lease <see cref="ScheduledJobHost"/> holds for one run: renewed every third of its duration until disposed, then
/// released. Spans the whole run, every retry attempt and backoff included (ADR-036 A1 rule 1).
/// </summary>
/// <remarks>
/// The hold cancels the run (<see cref="LostToken"/>, with <see cref="LostReason"/>) whenever it can no longer promise
/// that nobody else is running the job:
/// <list type="bullet">
///   <item><b>Renewal refused</b> — the lease lapsed (say, the store restarted) or another holder took it. If it is
///     free the hold re-takes it — then nobody else was running the job. If another holder has it, the run is cancelled
///     so two runs do not overlap.</item>
///   <item><b>Store unreachable while renewing</b> — the run continues while the lease is still live, and is cancelled
///     once renewal has failed for a full lease duration: from then on the lease has expired and another instance may
///     take it. (Only this instance may have lost the store — a network blip, SNAT exhaustion.)</item>
///   <item><b>Run too long</b> — past <see cref="ScheduledJobHostOptions.MaxRunDuration"/> the run is cancelled and
///     renewal stops, so a hung run — one that ignores cancellation — blocks the job for at most that plus one lease
///     duration, never indefinitely.</item>
/// </list>
/// A cancelled hold never releases: the lease may already belong to someone else, and it expires on its own.
/// </remarks>
internal sealed class ScheduledJobLeaseHold : IAsyncDisposable
{
    /// <summary>Run cancelled because renewal failed for a full lease duration.</summary>
    internal const string RenewalFailedMessage =
        "Cancelled: the scheduler lease could not be renewed for a full lease duration, so it has expired (ADR-036 A1 rule 1)";

    private readonly IScheduledJobLease _lease;
    private readonly string _jobId;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _maxRunDuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stopRenewing = new();
    private readonly DateTimeOffset _startedAt;
    private readonly Task _renewal;
    private DateTimeOffset _lastConfirmed;
    private string _token;
    private string? _lostReason;

    internal ScheduledJobLeaseHold(
        IScheduledJobLease lease,
        string jobId,
        string token,
        TimeSpan duration,
        TimeSpan maxRunDuration,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _lease = lease;
        _jobId = jobId;
        _token = token;
        _duration = duration;
        _maxRunDuration = maxRunDuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _startedAt = _lastConfirmed = timeProvider.GetUtcNow();
        _renewal = Task.Run(RenewUntilStoppedAsync);
    }

    /// <summary>Cancelled when the run must stop because the hold can no longer guarantee exclusivity.</summary>
    public CancellationToken LostToken => _lost.Token;

    /// <summary>Whether the hold cancelled the run.</summary>
    public bool IsLost => _lost.IsCancellationRequested;

    /// <summary>Why the hold cancelled the run; <c>null</c> while it has not.</summary>
    public string? LostReason => Volatile.Read(ref _lostReason);

    /// <summary>A third of the duration, so two renewals can fail before the lease expires.</summary>
    internal static TimeSpan RenewalInterval(TimeSpan duration) => TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));

    private async Task RenewUntilStoppedAsync()
    {
        var interval = RenewalInterval(_duration);
        var stop = _stopRenewing.Token;

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _timeProvider, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_timeProvider.GetUtcNow() - _startedAt >= _maxRunDuration)
            {
                _logger.LogError(
                    "Scheduled job '{JobId}' has run longer than MaxRunDuration ({MaxRunDuration}) — cancelling it and no longer renewing its lease, which expires within {Duration} (ADR-036 A1 rule 1)",
                    _jobId, _maxRunDuration, _duration);
                Lose($"Cancelled: the run exceeded MaxRunDuration ({_maxRunDuration}); its scheduler lease is no longer renewed (ADR-036 A1 rule 1)");
                return;
            }

            try
            {
                if (await _lease.RenewAsync(_jobId, _token, _duration, stop).ConfigureAwait(false))
                {
                    _lastConfirmed = _timeProvider.GetUtcNow();
                    continue;
                }

                var again = await _lease.TryAcquireAsync(_jobId, occurrenceUtc: null, _duration, stop).ConfigureAwait(false);
                if (again is { Status: ScheduledJobLeaseStatus.Granted, Token: { } token })
                {
                    _token = token;
                    _lastConfirmed = _timeProvider.GetUtcNow();
                    _logger.LogWarning(
                        "Scheduler lease for '{JobId}' lapsed during the run and was re-taken — no other run held it",
                        _jobId);
                    continue;
                }

                _logger.LogError(
                    "Scheduler lease for '{JobId}' was taken by another holder during the run — cancelling this run so two runs do not overlap (ADR-036 A1 rule 1)",
                    _jobId);
                Lose(ScheduledJobHost.LeaseLostMessage);
                return;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (_timeProvider.GetUtcNow() - _lastConfirmed >= _duration)
                {
                    _logger.LogError(
                        ex,
                        "Could not renew the scheduler lease for '{JobId}' for {Duration} — it has expired and another instance may take it, so this run is cancelled (ADR-036 A1 rule 1)",
                        _jobId, _duration);
                    Lose(RenewalFailedMessage);
                    return;
                }

                _logger.LogWarning(
                    ex,
                    "Could not renew the scheduler lease for '{JobId}' — the lease is still live; renewal is retried in {Interval}",
                    _jobId, interval);
            }
        }
    }

    private void Lose(string reason)
    {
        Volatile.Write(ref _lostReason, reason);
        try
        {
            _lost.Cancel();
        }
        catch (Exception ex)
        {
            // A cancellation callback threw; the token is cancelled regardless.
            _logger.LogWarning(ex, "A cancellation callback for scheduled job '{JobId}' threw", _jobId);
        }
    }

    /// <summary>Stops renewing, then releases the lease unless the hold cancelled the run.</summary>
    public async ValueTask DisposeAsync()
    {
        _stopRenewing.Cancel();
        try
        {
            await _renewal.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Scheduler lease renewal loop for '{JobId}' ended with an error", _jobId);
        }

        if (!_lost.IsCancellationRequested)
        {
            try
            {
                await _lease.ReleaseAsync(_jobId, _token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not release the scheduler lease for '{JobId}' — it expires on its own within {Duration}",
                    _jobId, _duration);
            }
        }

        _stopRenewing.Dispose();
        _lost.Dispose();
    }
}
