// R3 Part 1 — User-Record Membership Resolution (DI module)
// Task 012 (2026-06-21): Registers MembershipOptions binding.
// Task 031 (2026-06-21): Adds IIdentityNormalizationService singleton.
// Task 032 (2026-06-21): Adds OrganizationMembershipResolver (registered as
// both IOrganizationMembershipResolver canonical + IIdentityOrganizationResolver
// task-031 seam).
// Task 030 (2026-06-21): Adds IMembershipFieldDiscoveryService singleton.
// Task 033 (2026-06-21): Adds IMembershipResolverService singleton (orchestration).
// Task 081 (2026-06-22): Adds IMembershipEventPublisher singleton — real impl
// when Membership:EventPublisher:Enabled=true, NullMembershipEventPublisher
// (ADR-032 P2 Quiet no-op) otherwise. The registration is SYMMETRIC
// (always exactly one impl bound to the interface) per
// bff-extensions.md §F.1 — endpoints can unconditionally inject
// IMembershipEventPublisher without worrying about kill-switch state.
// Task 084 (2026-06-22): Adds IMembershipJunctionUpdater (Scoped) +
// SYMMETRIC IHostedService registration for the Service Bus subscription
// consumer. Real MembershipJunctionUpdaterHost is registered when
// Membership:JunctionUpdater:Enabled=true; NullMembershipJunctionUpdaterHost
// (ADR-032 hosted-service-peer pattern) is registered otherwise. Default
// remains the Null peer until task 071's topic is operator-deployed.
// Remaining registrations (endpoint mappings) arrive in later P4 tasks (035-036).
//
// ADR-010 (DI Minimalism): Feature-module pattern — one Add{Module}() per
// feature area, called from Program.cs.
// bff-extensions.md §A: BFF-touching addition. Placement = BFF (membership
// resolution is request-scoped, has TTFB budget against BFF state, and is
// consumed by AI playbook nodes + endpoints in the same request lifecycle).

using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for the user-record membership resolution feature
/// (R3 Part 1). Currently binds <see cref="MembershipOptions"/> from
/// configuration; service registrations follow in later P4 tasks.
/// </summary>
public static class MembershipModule
{
    /// <summary>
    /// Registers <see cref="MembershipOptions"/> bound to the
    /// <c>"Membership"</c> configuration section. Defaults are conservative
    /// (empty lists) so apps that never opt into the membership feature still
    /// resolve <c>IOptions&lt;MembershipOptions&gt;</c> cleanly.
    /// </summary>
    public static IServiceCollection AddMembership(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options binding only — no validation gate here. The discovery
        // service (task 030) will validate the contents at first-use.
        services.Configure<MembershipOptions>(
            configuration.GetSection(MembershipOptions.SectionName));

        // Task 032: organization-membership resolver. One concrete satisfies
        // both consumer-facing interfaces:
        //   - IOrganizationMembershipResolver: canonical (PersonIdentity-aware) contract
        //   - IIdentityOrganizationResolver: task 031's IEnumerable seam consumed by
        //     IdentityNormalizationService
        // Registered as singleton (ADR-010) — the resolver holds no per-request
        // state; the once-per-process "no mapping configured" log latch is
        // intentionally singleton-scoped.
        services.AddSingleton<OrganizationMembershipResolver>();
        services.AddSingleton<IOrganizationMembershipResolver>(
            sp => sp.GetRequiredService<OrganizationMembershipResolver>());
        services.AddSingleton<IIdentityOrganizationResolver>(
            sp => sp.GetRequiredService<OrganizationMembershipResolver>());

        // Task 031: identity normalization. Singleton (per ADR-010, holds no
        // per-request state — Redis cache is the only mutable surface, and
        // IDistributedCache itself is thread-safe). Consumes IDataverseService
        // (registered elsewhere), IDistributedCache (CacheModule),
        // IEnumerable<IIdentityOrganizationResolver> (registered above —
        // empty enumerable is acceptable; service returns empty OrganizationIds),
        // IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IIdentityNormalizationService, IdentityNormalizationService>();

        // Task 030: metadata-driven Lookup-field discovery. Singleton (per
        // ADR-010, holds no per-request state — IDistributedCache is the only
        // mutable surface and is thread-safe). Consumes IDataverseService
        // (unwrapped to ServiceClient for RetrieveEntityRequest, matches the
        // existing Services.Dataverse.MetadataService pattern), IDistributedCache
        // (CacheModule), IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IMembershipFieldDiscoveryService, MembershipFieldDiscoveryService>();

        // Task 033: top-level orchestration. Singleton (per ADR-010, holds no
        // per-request state — IDistributedCache is the only mutable surface and is
        // thread-safe). Consumes IMembershipFieldDiscoveryService (above),
        // IIdentityNormalizationService (above), IDataverseService (registered
        // elsewhere — used for FetchExpression queries against the target entity),
        // IDistributedCache (CacheModule), IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IMembershipResolverService, MembershipResolverService>();

        // Task 081: MembershipEventPublisher (FR-2P2.6 + Q2 fire-and-forget).
        // Options bound from "Membership:EventPublisher" section.
        services
            .Configure<MembershipEventPublisherOptions>(
                configuration.GetSection(MembershipEventPublisherOptions.SectionName));

        // ADR-032 SYMMETRIC registration. Exactly one impl is always bound
        // to IMembershipEventPublisher — minimal-API param inference can
        // resolve the dependency in EVERY config state without runtime
        // null-checks at endpoint sites.
        //
        // Branch rationale:
        //   Enabled=true  → real MembershipEventPublisher (singleton). Publishes
        //                   to Service Bus topic per MembershipEventPublisherOptions.TopicName.
        //                   Requires ServiceBusClient (registered by JobProcessingModule
        //                   from ConnectionStrings:ServiceBus).
        //   Enabled=false → NullMembershipEventPublisher (P2 Quiet no-op). Logs +
        //                   returns; no Service Bus interaction; no Azure
        //                   dependency. Default state until task 071 deploys
        //                   the topic + operator flips the flag.
        var publisherEnabled = configuration
            .GetSection(MembershipEventPublisherOptions.SectionName)
            .GetValue<bool>("Enabled");

        if (publisherEnabled)
        {
            // P1-style: real impl registered as singleton. Resolves
            // ServiceBusClient from the shared registration in
            // JobProcessingModule (a single SB client per host, per Azure
            // SDK best practice).
            services.AddSingleton<MembershipEventPublisher>();
            services.AddSingleton<IMembershipEventPublisher>(sp =>
                sp.GetRequiredService<MembershipEventPublisher>());
        }
        else
        {
            // P2 Null-Object: see ADR-032. Logs + returns immediately on
            // PublishAsync. Constructor takes only ILogger — no
            // feature-gated transitive deps.
            services.AddSingleton<NullMembershipEventPublisher>();
            services.AddSingleton<IMembershipEventPublisher>(sp =>
                sp.GetRequiredService<NullMembershipEventPublisher>());
        }

        // Task 084: Subscription consumer (consumer side).
        // Options bound from "Membership:JunctionUpdater" section (distinct
        // from "Membership:EventPublisher" so the publisher + consumer
        // kill-switches can be flipped independently).
        services.Configure<MembershipJunctionUpdaterOptions>(
            configuration.GetSection(MembershipJunctionUpdaterOptions.SectionName));

        // Handler is ALWAYS registered (no kill-switch). Task 085's
        // MembershipReconciliationJob reuses it directly, regardless of
        // whether the Service Bus consumer host is enabled. Scoped per
        // IDataverseService lifetime (matches ADR-010 standard pattern).
        services.AddScoped<IMembershipJunctionUpdater, MembershipJunctionUpdater>();

        // TimeProvider — used by the handler for sprk_lastsyncedon
        // timestamps. Registered TryAdd-style so existing registrations
        // (InsightsIngestModule, WorkspaceModule) win and tests can inject
        // a FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // SYMMETRIC hosted-service registration per bff-extensions.md §F.1.
        // Branch rationale:
        //   Enabled=true  → real MembershipJunctionUpdaterHost. Connects
        //                   to the topic + subscription via
        //                   DefaultAzureCredential (ADR-028); runs the
        //                   message pump; honors NFR-07 30s drain on stop.
        //   Enabled=false → NullMembershipJunctionUpdaterHost (ADR-032
        //                   hosted-service-peer pattern). Logs once on
        //                   start; performs no Service Bus work.
        //                   Default state until operator deploys task 071's
        //                   topic and flips the flag.
        var junctionUpdaterEnabled = configuration
            .GetSection(MembershipJunctionUpdaterOptions.SectionName)
            .GetValue<bool>("Enabled");

        if (junctionUpdaterEnabled)
        {
            services.AddSingleton<MembershipJunctionUpdaterHost>();
            services.AddHostedService(sp =>
                sp.GetRequiredService<MembershipJunctionUpdaterHost>());
        }
        else
        {
            services.AddHostedService<NullMembershipJunctionUpdaterHost>();
        }

        return services;
    }
}
