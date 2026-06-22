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
// Task 085 (2026-06-22): Adds MembershipReconciliationJob singleton +
// IScheduledJob forwarded registration + a startup HostedService that seeds
// the `membership-reconciliation` BackgroundJobDefinition row in the
// in-memory store + registers the handler with ScheduledJobRegistry.
// Mirrors the SchedulingModule.SchedulingBootstrapHostedService pattern
// (PlaybookSchedulerJob seed precedent). The recon job is INDEPENDENT of
// the Service Bus topic (task 071) — it writes the junction directly via
// IMembershipJunctionUpdater (reusing task 084's handler), so it ships
// enabled-by-default and provides the 24h-max-staleness backstop for
// maker-portal-only mutation paths (sprk_assigned*, sprk_task,
// sprk_opportunity — per event-source inventory §3A/§3D/§3E).
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

        // Task 085: MembershipReconciliationJob — nightly source-of-truth
        // junction reconciliation (FR-2P2.7). Mirrors PlaybookSchedulerJob's
        // lifetime pattern (Singleton with IServiceScopeFactory.CreateScope
        // per ExecuteAsync — see SchedulingModule.cs notes). Registered as
        // both concrete + IScheduledJob (forwarded) so the registry can
        // resolve it during seed-time bootstrap.
        services.Configure<MembershipReconciliationOptions>(
            configuration.GetSection(MembershipReconciliationOptions.SectionName));

        services.AddSingleton<MembershipReconciliationJob>();
        // Forwarded IScheduledJob registration. Note: SchedulingModule already
        // registers PlaybookSchedulerJob as IScheduledJob via the same forwarding
        // pattern; both registrations participate as enumerable IScheduledJob
        // implementations (the registry resolves by JobId, not by collection
        // index, so registration order is irrelevant).
        services.AddSingleton<IScheduledJob>(sp =>
            sp.GetRequiredService<MembershipReconciliationJob>());

        // Bootstrap hosted service — registers the handler in ScheduledJobRegistry
        // + seeds the BackgroundJobDefinition row in the in-memory store BEFORE
        // ScheduledJobHost's first cron tick. Mirrors
        // SchedulingModule.SchedulingBootstrapHostedService pattern. Idempotent
        // on host restart.
        services.AddHostedService<MembershipReconciliationBootstrapHostedService>();

        return services;
    }

    /// <summary>
    /// One-shot startup hosted service that (1) registers
    /// <see cref="MembershipReconciliationJob"/> with
    /// <see cref="ScheduledJobRegistry"/> and (2) seeds the
    /// <see cref="MembershipReconciliationJob.JobIdConstant"/> definition in
    /// <see cref="InMemoryBackgroundJobStore"/>. Runs synchronously on host
    /// startup; safe to invoke after
    /// <see cref="SchedulingModule.SchedulingBootstrapHostedService"/> (the
    /// scheduling module's bootstrap is inserted at index 0 of the hosted-services
    /// list; this one is appended in normal order and therefore runs AFTER, which
    /// is correct — the registry + store must exist before this bootstrap can
    /// register/seed against them).
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotency</b>: ScheduledJobRegistry.Register throws on duplicate;
    /// the bootstrap catches "already registered" exceptions and ignores them.
    /// InMemoryBackgroundJobStore.AddOrReplaceJob is naturally idempotent. So a
    /// host restart re-runs both calls without harm.</para>
    /// <para><b>Cron schedule</b>: <c>0 2 * * *</c> (daily at 02:00 UTC) by
    /// default per <see cref="MembershipReconciliationOptions.CronSchedule"/>.
    /// Operators MAY override the cron + Enabled state via the
    /// <c>Membership:Reconciliation</c> appsettings section before host start;
    /// changes after host start require an admin endpoint to toggle the row's
    /// Enabled flag (existing R3 task 022 endpoint).</para>
    /// <para><b>Independence from Service Bus topic deploy</b>: this bootstrap
    /// runs unconditionally regardless of <c>Membership:EventPublisher:Enabled</c>
    /// or <c>Membership:JunctionUpdater:Enabled</c>. The recon job reuses task
    /// 084's <see cref="IMembershipJunctionUpdater"/> directly (the handler is
    /// always registered, irrespective of the host kill-switch — see task 084's
    /// "Handler is ALWAYS registered" comment above).</para>
    /// </remarks>
    internal sealed class MembershipReconciliationBootstrapHostedService : IHostedService
    {
        private readonly ScheduledJobRegistry _registry;
        private readonly InMemoryBackgroundJobStore _store;
        private readonly MembershipReconciliationJob _job;
        private readonly Microsoft.Extensions.Options.IOptionsMonitor<MembershipReconciliationOptions> _options;
        private readonly ILogger<MembershipReconciliationBootstrapHostedService> _logger;

        public MembershipReconciliationBootstrapHostedService(
            ScheduledJobRegistry registry,
            InMemoryBackgroundJobStore store,
            MembershipReconciliationJob job,
            Microsoft.Extensions.Options.IOptionsMonitor<MembershipReconciliationOptions> options,
            ILogger<MembershipReconciliationBootstrapHostedService> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // (1) Register handler — idempotent on host restart.
            try
            {
                _registry.Register(_job);
                _logger.LogInformation(
                    "Registered IScheduledJob '{JobId}' with ScheduledJobRegistry",
                    MembershipReconciliationJob.JobIdConstant);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "IScheduledJob '{JobId}' already registered — skipping",
                    MembershipReconciliationJob.JobIdConstant);
            }

            // (2) Seed definition row. Cron + Enabled honor the configured
            // options at startup. Operators flipping the flag after start use
            // the admin endpoints (task 022).
            var opts = _options.CurrentValue;
            var cron = string.IsNullOrWhiteSpace(opts.CronSchedule) ? "0 2 * * *" : opts.CronSchedule.Trim();

            var definition = new BackgroundJobDefinition(
                JobId: MembershipReconciliationJob.JobIdConstant,
                DisplayName: _job.DisplayName,
                Description: _job.Description,
                Enabled: opts.Enabled,
                CronSchedule: cron,
                ConfigJson: null);
            _store.AddOrReplaceJob(definition);
            _logger.LogInformation(
                "Seeded BackgroundJobDefinition '{JobId}' (cron='{Cron}', enabled={Enabled})",
                definition.JobId, definition.CronSchedule, definition.Enabled);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
