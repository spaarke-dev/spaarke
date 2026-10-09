using OpenTelemetry.Trace;
using Sprk.Bff.Api.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
                metrics.AddMeter(Sprk.Bff.Api.Api.Events.EventEndpoints.MeterName); // task 097: event audit-log write failures
                // AI Safety meter (AIPU2-020): Prompt Shield blocked_total + latency_ms
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.PromptShieldTelemetry.MeterName);
                // AI Capabilities meter (AIPU2-011): ai_capability_manifest_refresh_total
                metrics.AddMeter("Sprk.Bff.Api.AiCapabilities");
                // AI Latency meter (AIPU2-066): TTFT, TBT, TTLT, prompt tokens, routing latency
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.AiLatencyTelemetry.MeterName);
                // Insights Engine Widgets r1 meter (project ai-spaarke-insights-engine-widgets-r1 task 050):
                // widget.insightcard.invoked + widget.insightcard.duration with bounded dimensions per NFR-06.
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.InsightWidgetsTelemetry.MeterName);
                // Event Rules meter (FR-P1-03 / NFR-09 "enforced AND telemetered") — registration
                // added by spaarke-ai-architecture-redesign-r1 task 054 (FR-P4-05): the meter existed
                // since task 022 but was never AddMeter'd, so eventpath.execution /
                // eventpath.bound_denial were silently dropped from the App Insights export.
                metrics.AddMeter(Sprk.Bff.Api.Services.Ai.EventRules.EventRulesTelemetry.MeterName);
                // Cosmos persistence write-failure counter (spaarkeai-compose-r7 R-5, 2026-08-18):
                // cosmos.write_failures{container} makes a SILENT total write outage (swallowed at
                // Warning per ADR-015 D-06) alertable. Without this, the ttl:null → 400 regression
                // froze History + memory for 11 days with no failing request and no read signal.
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.CosmosPersistenceTelemetry.MeterName);
                // Compose save-outcome counter (spaarkeai-compose-r8 task 013, FR-S10): one increment
                // per terminal save state, tagged outcome + bounded cause. R5/R6/R7 each shipped with
                // the save button dead for some document class and the discovery mechanism was owner
                // UAT — there was no metric that could have gone red. MUST stay registered: an
                // unregistered meter is silently dropped from the App Insights export (the trap the AI
                // redesign's task 054 found for the Event Rules meter, which existed unregistered).
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.ComposeSaveTelemetry.MeterName);
                // Signal-writer failure counter (spaarke-ontology-platform-r1 task 030 rework, owner
                // directive 2026-10-04): ontology.writer.failures{reason}. The writer fails closed by
                // design, so a broken credential or a refused write must be observable, not silently
                // indistinguishable from "no conditions found". MUST stay registered — an unregistered
                // meter is silently dropped from the App Insights export (the same trap the two comments
                // above this one each record for a different meter).
                metrics.AddMeter(Sprk.Bff.Api.Telemetry.OntologyWriterTelemetry.MeterName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource("Sprk.Bff.Api.Ai");
                tracing.AddSource("Sprk.Bff.Api.Rag");
                tracing.AddSource("Sprk.Bff.Api.Finance");
                // Insights Engine Widgets r1 ActivitySource (task 050): distributed-trace spans for
                // InsightSummaryCard invocations through /api/insights/ask.
                tracing.AddSource(Sprk.Bff.Api.Telemetry.InsightWidgetsTelemetry.MeterName);
                // spaarke-redis-cache-remediation-r1 task 040 closure: capture
                // StackExchange.Redis dependency calls so App Insights "Dependencies"
                // surfaces Redis traffic. Without this, the default App Insights SDK
                // does NOT auto-instrument StackExchange.Redis. The Redis instrumentation
                // resolves `IConnectionMultiplexer` from DI at startup; in dev-fallback
                // mode it picks up `NullConnectionMultiplexer` (per ADR-032 symmetric
                // registration) and emits no spans, which is the intended no-op.
                tracing.AddRedisInstrumentation();
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
        // D9-01 (config-deployment): removed Console.WriteLine startup echo that bypassed the
        // ILogger/OTel pipeline. This runs at service-registration time (pre-host-build) where no
        // ILogger is available; the registration itself is the source of truth, so the echo was noise.

        // Health check - Redis availability (/healthz). An async PING on the app's own singleton multiplexer,
        // resolved by the health-check factory; see RedisHealthCheck for why this replaced the inline lambda
        // that built a new service provider on every probe.
        var redisEnabled = configuration.GetValue<bool>("Redis:Enabled");
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "redis",
                sp => new Sprk.Bff.Api.Infrastructure.Cache.RedisHealthCheck(
                    redisEnabled ? sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>() : null),
                failureStatus: null,
                tags: null));

        return services;
    }
}
