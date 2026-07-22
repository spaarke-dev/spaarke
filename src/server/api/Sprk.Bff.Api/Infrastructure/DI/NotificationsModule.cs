using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the notification-spine Layer-C SignalR delivery leg (ADR-010 feature module,
/// spec FR-04). Registers the <see cref="SignalRDeliveryService"/> UNCONDITIONALLY (ADR-032) — the
/// real Serverless impl when Azure SignalR is configured, the Null-Object no-op when it is not — so
/// the unconditionally-mapped negotiate endpoint's handler always resolves its service parameter and
/// ASP.NET minimal-API metadata generation never aborts at startup (ADR-032 asymmetric-registration
/// rule; CLAUDE.md §10 bullet 6).
/// </summary>
/// <remarks>
/// Layer B's <c>OutboxService</c> is registered elsewhere (task 012, AnalysisServicesModule B1
/// promotion) — this module owns ONLY the delivery leg + its options. The enable is config-driven at
/// registration time: <c>Notifications:SignalR</c> absent/disabled ⇒ Null-Object.
/// </remarks>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options so IOptions<SignalRDeliveryOptions> is available to the real service.
        services.Configure<SignalRDeliveryOptions>(configuration.GetSection(SignalRDeliveryOptions.SectionName));

        // Shared bidirectional systemuserid ⇄ oid resolver (ADR-028 cross-reference; CLAUDE.md §11
        // consolidation point). Registered UNCONDITIONALLY as a Singleton — it wraps the singleton
        // IDataverseService + IDistributedCache and is injected into the singleton SignalRDeliveryService
        // (real impl) and, later, the poll endpoint (task 022). Concrete class behind an interface is
        // justified: multiple independent consumers need BOTH lookup directions from one cached service.
        // ADR-010 home: this module owns the delivery leg, whose real SignalRDeliveryService is the first
        // consumer, so the resolver is registered alongside it.
        services.AddSingleton<ISystemUserIdentityResolver, SystemUserIdentityResolver>();

        // Config-driven kill-switch (ADR-032): read the section point-in-time to decide which impl to
        // register. Absent connection string OR Enabled=false ⇒ Null-Object.
        var options = configuration.GetSection(SignalRDeliveryOptions.SectionName).Get<SignalRDeliveryOptions>()
                      ?? new SignalRDeliveryOptions();

        if (options.IsConfigured)
        {
            // Real Serverless delivery (Management SDK). Deps: IOptions + ILoggerFactory (both framework).
            services.AddSingleton<SignalRDeliveryService>();
        }
        else
        {
            // ADR-032 Null-Object: quiet no-op ping (P2), fail-fast negotiate → 503 (P3). Minimal deps
            // (logger only). Registered under the SAME concrete type the endpoint + producers inject,
            // so the endpoint maps + resolves identically whether SignalR is on or off.
            services.AddSingleton<SignalRDeliveryService>(sp =>
                new NullSignalRDeliveryService(sp.GetRequiredService<ILogger<SignalRDeliveryService>>()));
        }

        return services;
    }
}
