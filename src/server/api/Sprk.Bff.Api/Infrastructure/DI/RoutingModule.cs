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
        // UNCONDITIONAL (2 / 15 — well under ADR-010 module cap):
        //   1. AddScoped<IConsumerRoutingService, ConsumerRoutingService>
        //      — consumer→playbook routing facade (Phase 1R FR-1R-02).
        //      No feature flag — routing is always-on; Dataverse errors
        //      log + return null (caller graceful-degrades to env var
        //      during the FR-1R-06 deprecation window).
        services.AddScoped<IConsumerRoutingService, ConsumerRoutingService>();

        //   2. AddHostedService<RoutingConsumerTypeHealthCheck>
        //      — Phase 1R S-5C startup health check (per 2026-06-24 code
        //      review): diffs ConsumerTypes.All vs Dataverse distinct
        //      sprk_consumertype values at app start. Emits operator WARN
        //      on mismatch (admin typo on Dataverse side, or missing seed
        //      record). Fail-soft: Dataverse outage during startup logs
        //      info and continues — health check is a deploy-time
        //      diagnostic, NOT a runtime dependency.
        services.AddHostedService<RoutingConsumerTypeHealthCheck>();

        return services;
    }
}
