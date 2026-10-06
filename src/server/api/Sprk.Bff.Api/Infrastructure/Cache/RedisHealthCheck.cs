using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// The <c>redis</c> check on <c>/healthz</c>: one async <c>PING</c> on the app's singleton
/// <see cref="IConnectionMultiplexer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it replaced the inline lambda in <c>TelemetryModule</c> (2026-10-05).</b> The lambda called
/// <c>services.BuildServiceProvider()</c> on every probe — once a minute — and never disposed it, so each
/// probe leaked a whole service container (the dev BFF's working set grew 17–20 MB/hour until each
/// restart). It then ran the synchronous <c>SetString</c>/<c>GetString</c>/<c>Remove</c> on a fresh
/// <c>RedisCache</c>, whose blocking batch path stalled 500–2000 ms in about 30% of probes for at least
/// 90 days, on the classic cache and on Azure Managed Redis alike. Those stalls were most of the
/// <c>op=set</c> samples in <c>cache.redis_call_duration_ms</c>, which feeds the ADR-009 latency alert.
/// </para>
/// <para>
/// A <c>PING</c> proves what the probe is for — the connection is up and the managed-identity sign-in
/// holds — without writing a key, without a second <c>IDistributedCache</c> outside
/// <see cref="ITenantCache"/>, and without touching the cache metrics.
/// </para>
/// </remarks>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _multiplexer;

    /// <param name="multiplexer">The app's multiplexer; <c>null</c> when Redis is disabled (in-memory mode).</param>
    public RedisHealthCheck(IConnectionMultiplexer? multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_multiplexer is null)
        {
            return HealthCheckResult.Healthy("Redis is disabled (using in-memory cache for development)");
        }

        try
        {
            var roundTrip = await _multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy(
                "Redis cache is available and responsive",
                new Dictionary<string, object> { ["pingMs"] = Math.Round(roundTrip.TotalMilliseconds, 1) });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis cache is unavailable", ex);
        }
    }
}
