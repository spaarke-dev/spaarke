using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for consumer→playbook routing services
/// (chat-routing-redesign-r1 Phase 1R).
/// </summary>
/// <remarks>
/// <para>
/// Hosts the Phase 1R <see cref="IConsumerRoutingService"/> facade and its
/// concrete <see cref="ConsumerRoutingService"/> implementation. Separated
/// from <see cref="AiModule"/> because:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ADR-010 minimalism</b>: <see cref="AiModule"/> is at its 15/15
///     unconditional-registration ceiling per the audit block at the bottom
///     of that file. Adding a 16th line would violate the per-module cap.
///     Feature-module-per-concern is the established pattern
///     (<c>AiChatModule</c>, <c>AiSafetyModule</c>, etc.).
///   </item>
///   <item>
///     <b>ADR-013 facade boundary</b>: <see cref="IConsumerRoutingService"/>
///     is consumed by CRUD-side services (<c>MatterPreFillService</c>,
///     <c>WorkspaceFileEndpoints</c>, etc., per spec FR-1R-05) via the
///     <c>Services/Ai/PublicContracts/</c> facade. A dedicated routing
///     module keeps the consumer→playbook routing concern observable in
///     the DI composition root.
///   </item>
/// </list>
/// <para>
/// <b>Registration shape</b>: <see cref="ConsumerRoutingService"/> is
/// registered Scoped to match the request lifetime of its consumers and to
/// keep <see cref="Microsoft.Extensions.Logging.ILogger{T}"/> +
/// <see cref="Spaarke.Dataverse.IGenericEntityService"/> + correlation
/// state aligned with the calling endpoint.
/// </para>
/// <para>
/// <b>No Null-Object peer</b> is registered: routing is always-on (not behind
/// a feature flag), and on Dataverse error the implementation logs +
/// returns <c>null</c> (graceful-degrade per ADR-032 quiet-no-op semantics
/// applied in-method rather than via a Null peer). The 6 consumer migrations
/// in tasks 028c/028d will treat <c>null</c> as "fall back to typed-options
/// env var" during the deprecation window (FR-1R-06).
/// </para>
/// </remarks>
public static class RoutingModule
{
    /// <summary>
    /// Registers Phase 1R routing services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRoutingModule(this IServiceCollection services)
    {
        // UNCONDITIONAL (3 / 15 — well under ADR-010 module cap):
        //   1. AddScoped<IConsumerRoutingService, ConsumerRoutingService>
        //      — consumer→playbook routing facade (Phase 1R FR-1R-02).
        //      No feature flag — routing is always-on; Dataverse errors
        //      log + return null (caller graceful-degrades to env var
        //      during the FR-1R-06 deprecation window).
        services.AddScoped<IConsumerRoutingService, ConsumerRoutingService>();

        //   2. AddHostedService<RoutingConsumerTypeHealthCheck>
        //      — startup log surface for the ADR-039 boot reconciliation
        //      (originally Phase 1R S-5C, extended by
        //      spaarke-ai-architecture-redesign-r1 task 005 / FR-P0-04):
        //      reconciles ConsumerTypes.All vs Binding rows AND the
        //      sprk_analysistool row <-> registered-handler bijection at app
        //      start. Drift logs at Error. Fail-soft on transient Dataverse
        //      errors: the hosted service never blocks host startup.
        services.AddHostedService<RoutingConsumerTypeHealthCheck>();

        //   3. Health-check pipeline registration (FR-P0-04, ADR-039): the
        //      same class doubles as an IHealthCheck surfaced at /healthz
        //      (mapped in EndpointMappingExtensions). Catalog drift =>
        //      Unhealthy — the deploy gate that makes closed-catalog drift
        //      impossible to ship silently. Never false-fails: compound AI
        //      gate OFF => Healthy-with-description; ToolFramework OFF =>
        //      bijection dimension skipped; transient Dataverse error =>
        //      Degraded.
        services.AddHealthChecks()
            .AddCheck<RoutingConsumerTypeHealthCheck>(
                "ai-catalog-reconciliation",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ai", "catalog", "startup" });

        return services;
    }
}
