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
// P2 Quiet no-op — re-keyed by unified-access-control-r2 task 132 (see the
// registration below for why the old gate could never select the real one):
//   - Redis:Enabled=true → real MembershipCacheInvalidator: BFF write-path
//     access evictions always active; junction pub/sub publish + the
//     subscriber hosted service only with Membership:CacheInvalidator:Enabled.
//   - Else (in-memory cache) → NullMembershipCacheInvalidator (logs once, no
//     Redis calls, no subscriber). The 2-min cache TTLs (task 132) are the
//     correctness backstop; eviction is the latency optimization.
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

        // SYMMETRIC registration per bff-extensions.md §F.1 + ADR-032 P2 Quiet no-op — RE-KEYED by
        // unified-access-control-r2 task 132 (defect C12):
        //
        //   Redis:Enabled=true  → real MembershipCacheInvalidator. Its BFF write-path access evictions (team/BU/owner
        //                         changes) are ALWAYS active; the junction pub/sub PUBLISH and its subscriber hosted
        //                         service follow Membership:CacheInvalidator:Enabled (the channel switch).
        //   Redis:Enabled=false → NullMembershipCacheInvalidator (in-memory cache — Development/Testing only, CacheModule
        //                         refuses it anywhere else). Nothing can be pattern-evicted; it logs that once.
        //
        // ⚠️ WHY the old gate was replaced, not just extended. It was `Enabled && services.Any(IConnectionMultiplexer)`,
        // but Program.cs calls AddMembership BEFORE AddCacheModule, so no multiplexer was ever registered at this point
        // and the real invalidator could never be selected — in any environment, flag or no flag (and once CacheModule
        // ran, a multiplexer — real or NullConnectionMultiplexer — is always present, so the check meant nothing
        // either). Turning the flag on in Bicep would therefore have changed nothing. Redis:Enabled is the value
        // CacheModule itself decides by, read from the same configuration, so this gate cannot disagree with it.
        //
        // Correctness of the access evictions therefore does NOT depend on a kill switch (ADR-032 tension, task 132
        // notes): every deployed environment runs Redis (customer.bicep, model1-shared.bicep, model2-full.bicep set
        // Redis__Enabled=true; CacheModule fails startup without it outside Development/Testing).
        var cacheInvalidatorEnabled = configuration
            .GetSection(MembershipCacheInvalidatorOptions.SectionName)
            .GetValue<bool>("Enabled");
        var redisIsTheCache = configuration
            .GetSection(Configuration.RedisOptions.SectionName)
            .GetValue<bool>("Enabled");

        if (redisIsTheCache)
        {
            services.AddSingleton<MembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(sp =>
                sp.GetRequiredService<MembershipCacheInvalidator>());

            if (cacheInvalidatorEnabled)
            {
                // Subscriber hosted service — runs on every BFF instance, evicts
                // cache entries on channel messages. Singleton + IHostedService.
                // Only with the channel switch on: with it off nothing publishes, so there is nothing to consume.
                services.AddSingleton<MembershipCacheInvalidationSubscriber>();
                services.AddHostedService(sp =>
                    sp.GetRequiredService<MembershipCacheInvalidationSubscriber>());
            }
        }
        else
        {
            // P2 Null-Object: see ADR-032.
            services.AddSingleton<NullMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(sp =>
                sp.GetRequiredService<NullMembershipCacheInvalidator>());
        }

        // unified-access-control-r2 task 132 (main-session round 55): the invalidator chosen above — real, or the Null peer
        // with the in-memory cache — is ALSO the share-write observer DataverseWebApiService notifies after every POA share
        // write (GraphModule passes it to the client's constructor). Unconditional, so the client always has one (ADR-032
        // symmetric; bff-extensions.md §F.1): the eviction is a property of the write, not of the caller.
        services.AddSingleton<Spaarke.Dataverse.IRecordShareWriteObserver>(sp =>
            sp.GetRequiredService<IMembershipCacheInvalidator>());

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
        // IServiceScopeFactory.CreateScope per ExecuteAsync, like the former PlaybookSchedulerJob (removed, D-100).
        services.Configure<MembershipReconciliationOptions>(
            configuration.GetSection(MembershipReconciliationOptions.SectionName));

        // Registered through AddScheduledJob (ADR-036 A1 rule 6). Cron + Enabled
        // come from Membership:Reconciliation at startup, as the deleted bootstrap
        // read them; a change needs a restart. An unparseable CronSchedule now
        // FAILS STARTUP (it used to disable only this job, with a log line) —
        // deliberately: a deployment error surfaces at the slot's health check,
        // the way CacheModule treats a broken Redis setting. The admin enable/disable applies to
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
