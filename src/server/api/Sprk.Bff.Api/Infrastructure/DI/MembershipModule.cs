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
// Task 085 (2026-06-22): Adds MembershipReconciliationJob — registered since
// unified-access-control-r2 task 103 (2026-09-14) through AddScheduledJob
// (ADR-036 A1 rule 6); its per-job bootstrap hosted service is gone. The recon job is INDEPENDENT of
// the Service Bus topic (task 071) — it writes the junction directly via
// IMembershipJunctionUpdater (reusing task 084's handler), so it ships
// enabled-by-default and provides the 24h-max-staleness backstop for
// maker-portal-only mutation paths (sprk_assigned*, sprk_task,
// sprk_opportunity — per event-source inventory §3A/§3D/§3E).
// Task 086 (2026-06-22): Adds IMembershipCacheInvalidator (FR-2P2.8 +
// AC-1P2.7). SYMMETRIC registration per bff-extensions.md §F.1 + ADR-032
// P2 Quiet no-op:
//   - Membership:CacheInvalidator:Enabled=true AND IConnectionMultiplexer
//     resolvable → real MembershipCacheInvalidator + subscriber hosted
//     service that evicts MembershipResolverService cache entries on
//     channel `membership-cache-invalidate`.
//   - Else → NullMembershipCacheInvalidator (logs once, no Redis calls,
//     no subscriber). The 5-min cache TTL on MembershipResolverService is
//     the correctness backstop; pub/sub is the latency optimization.
// MembershipJunctionUpdater (task 084) consumes IMembershipCacheInvalidator
// unconditionally; the recon job (task 085) reuses the same handler so
// invalidations fire from both paths automatically.
// Remaining registrations (endpoint mappings) arrive in later P4 tasks (035-036).
//
// ADR-010 (DI Minimalism): Feature-module pattern — one Add{Module}() per
// feature area, called from Program.cs.
// bff-extensions.md §A: BFF-touching addition. Placement = BFF (membership
// resolution is request-scoped, has TTFB budget against BFF state, and is
// consumed by AI playbook nodes + endpoints in the same request lifecycle).

using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Scheduling;
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

        // R7 W12 task 130 (2026-06-30): post-configure step that seeds the
        // canonical Spaarke identity tables + audit-field exclusions when the
        // bound configuration left them empty. Required because the
        // "Membership" appsettings section is absent in every deployed
        // environment (only defined in the GITIGNORED
        // appsettings.Development.json.template); without this, the discovery
        // service silently classifies every membership-bearing lookup as
        // "target-table-not-in-identity-list" and the resolver returns zero
        // results for every user. The post-configure runs AFTER any operator
        // binding, and ONLY seeds when the bound list is empty — operator
        // config replaces the defaults cleanly. See
        // MembershipOptionsDefaults XML doc for the bug analysis.
        services.AddSingleton<
            Microsoft.Extensions.Options.IPostConfigureOptions<MembershipOptions>,
            MembershipOptionsDefaults>();

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

        // Task 086: Membership cache invalidator (FR-2P2.8 + AC-1P2.7).
        // Options bound from "Membership:CacheInvalidator" section.
        services.Configure<MembershipCacheInvalidatorOptions>(
            configuration.GetSection(MembershipCacheInvalidatorOptions.SectionName));

        // SYMMETRIC registration per bff-extensions.md §F.1 + ADR-032 P2
        // Quiet no-op. Real impl wins only when BOTH:
        //   (a) Membership:CacheInvalidator:Enabled=true (operator
        //       explicitly opted in), AND
        //   (b) IConnectionMultiplexer is registered in the container
        //       (CacheModule only registers it when Redis:Enabled=true).
        // Either gate fails → Null peer wins. This guarantees that
        // local-dev / CI environments without Redis still resolve
        // IMembershipCacheInvalidator cleanly via minimal-API param
        // inference.
        var cacheInvalidatorEnabled = configuration
            .GetSection(MembershipCacheInvalidatorOptions.SectionName)
            .GetValue<bool>("Enabled");
        var redisRegistered = services.Any(d =>
            d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));

        if (cacheInvalidatorEnabled && redisRegistered)
        {
            services.AddSingleton<MembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(sp =>
                sp.GetRequiredService<MembershipCacheInvalidator>());

            // Subscriber hosted service — runs on every BFF instance, evicts
            // cache entries on channel messages. Singleton + IHostedService.
            services.AddSingleton<MembershipCacheInvalidationSubscriber>();
            services.AddHostedService(sp =>
                sp.GetRequiredService<MembershipCacheInvalidationSubscriber>());
        }
        else
        {
            services.AddSingleton<NullMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(sp =>
                sp.GetRequiredService<NullMembershipCacheInvalidator>());
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
        // The handler injects IMembershipCacheInvalidator (registered
        // SYMMETRICALLY above per task 086) — invalidation fires from
        // both the SB consumer path AND the recon path (task 085 reuses
        // this same handler) automatically.
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

        // Task 085: MembershipReconciliationJob — nightly source-of-truth
        // junction reconciliation (FR-2P2.7). Singleton with
        // IServiceScopeFactory.CreateScope per ExecuteAsync, like PlaybookSchedulerJob.
        services.Configure<MembershipReconciliationOptions>(
            configuration.GetSection(MembershipReconciliationOptions.SectionName));

        // Registered through AddScheduledJob (ADR-036 A1 rule 6). Cron + Enabled
        // come from Membership:Reconciliation at startup, as the deleted bootstrap
        // read them; a change needs a restart. The admin enable/disable applies to
        // the instance that served it until DataverseBackgroundJobStore exists
        // (ADR-036 A1 §2).
        var reconciliation = configuration
            .GetSection(MembershipReconciliationOptions.SectionName)
            .Get<MembershipReconciliationOptions>() ?? new MembershipReconciliationOptions();
        services.AddScheduledJob<MembershipReconciliationJob>(
            string.IsNullOrWhiteSpace(reconciliation.CronSchedule) ? "0 2 * * *" : reconciliation.CronSchedule,
            reconciliation.Enabled);

        return services;
    }
}
