using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for OpenTelemetry, health checks, and circuit breaker services (ADR-010).
/// </summary>
public static class TelemetryModule
{
    /// <summary>
    /// Adds OpenTelemetry metrics/tracing, health checks (Redis), and circuit breaker registry.
    /// </summary>
    public static IServiceCollection AddTelemetryModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // OpenTelemetry
        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("Sprk.Bff.Api.Ai");
                metrics.AddMeter("Sprk.Bff.Api.Rag");
                metrics.AddMeter("Sprk.Bff.Api.Cache");
                metrics.AddMeter("Sprk.Bff.Api.CircuitBreaker");
                metrics.AddMeter("Sprk.Bff.Api.Finance");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource("Sprk.Bff.Api.Ai");
                tracing.AddSource("Sprk.Bff.Api.Rag");
                tracing.AddSource("Sprk.Bff.Api.Finance");
            });

        // Circuit Breaker Registry
        services.AddSingleton<Sprk.Bff.Api.Infrastructure.Resilience.ICircuitBreakerRegistry,
            Sprk.Bff.Api.Infrastructure.Resilience.CircuitBreakerRegistry>();

        // AI Search Resilience Options
        services
            .AddOptions<AiSearchResilienceOptions>()
            .Bind(configuration.GetSection(AiSearchResilienceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Resilient Search Client
        services.AddSingleton<Sprk.Bff.Api.Infrastructure.Resilience.IResilientSearchClient,
            Sprk.Bff.Api.Infrastructure.Resilience.ResilientSearchClient>();
        Console.WriteLine("\u2713 Circuit breaker registry enabled");

        // Health Checks - Redis availability monitoring
        var redisEnabled = configuration.GetValue<bool>("Redis:Enabled");
        services.AddHealthChecks()
            .AddCheck("redis", () =>
            {
                if (!redisEnabled)
                {
                    return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        "Redis is disabled (using in-memory cache for development)");
                }

                try
                {
#pragma warning disable ASP0000
                    var cache = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
#pragma warning restore ASP0000
                    var testKey = "_health_check_";
                    var testValue = DateTimeOffset.UtcNow.ToString("O");

                    cache.SetString(testKey, testValue, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
                    });

                    var retrieved = cache.GetString(testKey);
                    cache.Remove(testKey);

                    if (retrieved == testValue)
                    {
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Redis cache is available and responsive");
                    }

                    return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Redis cache returned unexpected value");
                }
                catch (Exception ex)
                {
                    return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Redis cache is unavailable", ex);
                }
            });

        return services;
    }
}
