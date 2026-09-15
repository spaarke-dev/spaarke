using Microsoft.Extensions.Logging;

namespace Spaarke.Scheduling;

/// <summary>
/// A lease <see cref="ScheduledJobHost"/> holds for one run: renewed every third of its duration until disposed, then
/// released. Spans the whole run, every retry attempt and backoff included (ADR-036 A1 rule 1).
/// </summary>
/// <remarks>
/// <para><b>Renewal refused</b> — the lease lapsed (say, the store restarted) or another holder took it. The hold
/// re-takes it if it is free, since then nobody else is running the job. If another holder has it, the hold cancels
/// <see cref="LostToken"/>, so two runs do not overlap.</para>
/// <para><b>Store unreachable while renewing</b> — the run continues. No other instance can take the lease while the
/// store is down, and the next renewal re-checks ownership once it is back.</para>
/// </remarks>
internal sealed class ScheduledJobLeaseHold : IAsyncDisposable
{
    private readonly IScheduledJobLease _lease;
    private readonly string _jobId;
    private readonly TimeSpan _duration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stopRenewing = new();
    private readonly Task _renewal;
    private string _token;

    internal ScheduledJobLeaseHold(
        IScheduledJobLease lease,
        string jobId,
        string token,
        TimeSpan duration,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _lease = lease;
        _jobId = jobId;
        _token = token;
        _duration = duration;
        _timeProvider = timeProvider;
        _logger = logger;
        _renewal = Task.Run(RenewUntilStoppedAsync);
    }

    /// <summary>Cancelled when another holder took the lease during the run.</summary>
    public CancellationToken LostToken => _lost.Token;

    /// <summary>Whether another holder took the lease during the run.</summary>
    public bool IsLost => _lost.IsCancellationRequested;

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

            try
            {
                if (await _lease.RenewAsync(_jobId, _token, _duration, stop).ConfigureAwait(false))
                {
                    continue;
                }

                var again = await _lease.TryAcquireAsync(_jobId, occurrenceUtc: null, _duration, stop).ConfigureAwait(false);
                if (again is { Status: ScheduledJobLeaseStatus.Granted, Token: { } token })
                {
                    _token = token;
                    _logger.LogWarning(
                        "Scheduler lease for '{JobId}' lapsed during the run and was re-taken — no other run held it",
                        _jobId);
                    continue;
                }

                _logger.LogError(
                    "Scheduler lease for '{JobId}' was taken by another holder during the run — cancelling this run so two runs do not overlap (ADR-036 A1 rule 1)",
                    _jobId);
                _lost.Cancel();
                return;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not renew the scheduler lease for '{JobId}' — the run continues; renewal is retried in {Interval}",
                    _jobId, interval);
            }
        }
    }

    /// <summary>Stops renewing, then releases the lease unless another holder took it.</summary>
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
