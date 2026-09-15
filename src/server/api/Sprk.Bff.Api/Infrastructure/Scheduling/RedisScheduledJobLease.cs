using Microsoft.Extensions.Options;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.Scheduling;

/// <summary>
/// Redis-backed <see cref="IScheduledJobLease"/>: one dispatch per schedule across every BFF instance and deployment
/// slot (ADR-036 A1 rule 1), over the BFF's existing Redis connection (ADR-009) — no new store or package.
/// </summary>
/// <remarks>
/// <para><b>Keys</b> — prefixed with <see cref="RedisOptions.InstanceName"/>, like every raw-Redis key the BFF writes
/// (see <c>SessionFilesCleanupJob</c>):</para>
/// <list type="bullet">
///   <item><c>{prefix}scheduler:lease:{jobId}</c> — the lease. Value = the holder's token; expiry = the lease
///     duration. Taken with <c>SET NX PX</c> (<see cref="IDatabaseAsync.LockTakeAsync"/>) — atomic, never
///     check-then-set. Extended and released only by its holder: <see cref="IDatabaseAsync.LockExtendAsync"/> and
///     <see cref="IDatabaseAsync.LockReleaseAsync"/> compare the token inside a Redis transaction.</item>
///   <item><c>{prefix}scheduler:last-fire:{jobId}</c> — UTC ticks of the last dispatched occurrence. Read and written
///     only while holding the lease, so that read-compare-write cannot race.</item>
/// </list>
/// <para><b>Failures</b> — every Redis failure surfaces as <see cref="ScheduledJobLeaseUnavailableException"/>; the
/// host then applies the owner decision of 2026-09-14 (retry briefly, then do not dispatch; record the tick failed).</para>
/// </remarks>
public sealed class RedisScheduledJobLease : IScheduledJobLease
{
    /// <summary>
    /// How long the last-dispatched-occurrence marker is kept. It only has to outlast the skew between instances waking
    /// for the same occurrence; a later occurrence is always greater, so an expired marker costs nothing.
    /// </summary>
    internal static readonly TimeSpan OccurrenceMarkerRetention = TimeSpan.FromDays(1);

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisScheduledJobLease(IConnectionMultiplexer redis, IOptions<RedisOptions> redisOptions)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        ArgumentNullException.ThrowIfNull(redisOptions);
        _keyPrefix = (redisOptions.Value.InstanceName ?? string.Empty) + "scheduler:";
    }

    /// <inheritdoc />
    public bool IsDistributed => true;

    internal RedisKey LeaseKey(string jobId) => _keyPrefix + "lease:" + jobId;

    internal RedisKey OccurrenceKey(string jobId) => _keyPrefix + "last-fire:" + jobId;

    /// <inheritdoc />
    public async Task<ScheduledJobLeaseGrant> TryAcquireAsync(
        string jobId,
        DateTimeOffset? occurrenceUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        cancellationToken.ThrowIfCancellationRequested();

        var db = Database(jobId);
        var leaseKey = LeaseKey(jobId);
        var token = Guid.NewGuid().ToString("N");

        bool taken;
        try
        {
            taken = await db.LockTakeAsync(leaseKey, token, duration).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            throw Unavailable(jobId, "take", ex);
        }

        if (!taken)
        {
            return ScheduledJobLeaseGrant.Held;
        }

        if (occurrenceUtc is not { } occurrence)
        {
            return ScheduledJobLeaseGrant.Granted(token);
        }

        try
        {
            var occurrenceKey = OccurrenceKey(jobId);
            var last = await db.StringGetAsync(occurrenceKey).ConfigureAwait(false);
            if (last.HasValue && (long)last >= occurrence.UtcTicks)
            {
                await db.LockReleaseAsync(leaseKey, token).ConfigureAwait(false);
                return ScheduledJobLeaseGrant.AlreadyDispatched;
            }

            await db.StringSetAsync(occurrenceKey, occurrence.UtcTicks, expiry: OccurrenceMarkerRetention)
                .ConfigureAwait(false);
            return ScheduledJobLeaseGrant.Granted(token);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // Give the lease back rather than leave it until it expires; if Redis is down this fails too, and the
            // expiry covers it.
            try
            {
                await db.LockReleaseAsync(leaseKey, token).ConfigureAwait(false);
            }
            catch (Exception releaseEx) when (IsStoreFailure(releaseEx))
            {
                // The lease expires on its own.
            }

            throw Unavailable(jobId, "record the occurrence for", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenewAsync(string jobId, string token, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            return await Database(jobId).LockExtendAsync(LeaseKey(jobId), token, duration).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            throw Unavailable(jobId, "renew", ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string jobId, string token, CancellationToken cancellationToken)
    {
        try
        {
            await Database(jobId).LockReleaseAsync(LeaseKey(jobId), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            throw Unavailable(jobId, "release", ex);
        }
    }

    private IDatabase Database(string jobId)
    {
        if (!_redis.IsConnected)
        {
            throw new ScheduledJobLeaseUnavailableException(
                $"Redis is not connected — cannot use the scheduler lease for '{jobId}'.");
        }

        return _redis.GetDatabase();
    }

    private static bool IsStoreFailure(Exception ex) =>
        ex is RedisException or TimeoutException or ObjectDisposedException;

    private static ScheduledJobLeaseUnavailableException Unavailable(string jobId, string action, Exception ex) =>
        new($"Redis could not {action} the scheduler lease for '{jobId}': {ex.Message}", ex);
}
