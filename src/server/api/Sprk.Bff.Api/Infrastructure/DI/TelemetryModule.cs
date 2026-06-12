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
                // AI Safety meter (AIPU2-020): Prompt Shield blocked_total + latency_ms
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.PromptShieldTelemetry.MeterName);
                // AI Capabilities meter (AIPU2-011): ai_capability_manifest_refresh_total
                metrics.AddMeter("Sprk.Bff.Api.AiCapabilities");
                // AI Latency meter (AIPU2-066): TTFT, TBT, TTLT, prompt tokens, routing latency
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.AiLatencyTelemetry.MeterName);
                // R5 Summarize meter (D1-08 task 008): r5.summarize.invocation + r5.session_files.index_size
                // Stable downstream contract for Phase 3 D3-03 dashboards.
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.R5SummarizeTelemetry.MeterName);
                // Insights Engine Widgets r1 meter (project ai-spaarke-insights-engine-widgets-r1 task 050):
                // widget.insightcard.invoked + widget.insightcard.duration with bounded dimensions per NFR-06.
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.InsightWidgetsTelemetry.MeterName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource("Sprk.Bff.Api.Ai");
                tracing.AddSource("Sprk.Bff.Api.Rag");
                tracing.AddSource("Sprk.Bff.Api.Finance");
                // R5 Summarize ActivitySource (D1-08 task 008): distributed-trace spans for Summarize-for-Chat invocations.
                tracing.AddSource(Sprk.Bff.Api.Telemetry.R5SummarizeTelemetry.MeterName);
                // Insights Engine Widgets r1 ActivitySource (task 050): distributed-trace spans for
                // InsightSummaryCard invocations through /api/insights/ask.
                tracing.AddSource(Sprk.Bff.Api.Telemetry.InsightWidgetsTelemetry.MeterName);
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
