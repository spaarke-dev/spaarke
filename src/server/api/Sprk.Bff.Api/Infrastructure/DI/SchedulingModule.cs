using Microsoft.Extensions.Hosting;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the in-process Spaarke.Scheduling background-job framework
/// (R3 Part 2 — FR-2.1 through FR-2.8). Registers the job registry, run-history store, host
/// options, and the canonical <see cref="ScheduledJobHost"/> singleton (which doubles as a
/// <see cref="IHostedService"/>) unconditionally so the admin endpoints (R3 task 020 / 021 / 022)
/// can resolve their dependencies on every BFF startup and the cron loop actually fires.
/// </summary>
/// <remarks>
/// <para><b>Asymmetric-registration compliance (bff-extensions.md §F.1)</b>:
/// The <c>/api/admin/jobs/*</c> endpoints map UNCONDITIONALLY in
/// <see cref="EndpointMappingExtensions"/>. Their dependencies
/// (<see cref="ScheduledJobRegistry"/> + <see cref="IBackgroundJobStore"/>) and the
/// <see cref="ScheduledJobHost"/> therefore MUST also register unconditionally — failing to do
/// so would reproduce the RB-T028-03/04/05/06 pattern (filed 2026-05-31, fixed by
/// `sdap.bff.api-test-suite-repair-r2` task 011 via the 18-service Null-Object migration).
/// This module is intentionally NOT feature-gated.</para>
///
/// <para><b>Backing store choice</b>:
/// Wires <see cref="InMemoryBackgroundJobStore"/> as the <see cref="IBackgroundJobStore"/>
/// implementation for the early-wave (P1–P3) BFF deployments. A Dataverse-backed store using
/// <c>sprk_backgroundjob</c> / <c>sprk_backgroundjobrun</c> (tasks 015/016) will land in a
/// later wave; until then, run history is process-local (lost on App Service restart) which is
/// acceptable for the R3 P3 admin-surface validation goal. The Dataverse-backed swap is a
/// single-line change here.</para>
///
/// <para><b>Hosted-service upgrade (R3 task 023)</b>:
/// Tasks 020 + 021 registered <see cref="ScheduledJobHost"/> as a Singleton (NOT
/// <c>AddHostedService</c>) because no production cron job existed yet — admin manual-trigger
/// could resolve the host without spinning a cron loop. Task 023 (PlaybookSchedulerJob) is the
/// first production cron job, so this module now ALSO registers the same singleton as a
/// <see cref="IHostedService"/>. The pattern
/// <c>services.AddHostedService(sp =&gt; sp.GetRequiredService&lt;ScheduledJobHost&gt;())</c>
/// re-uses the existing singleton rather than constructing a second instance — critical
/// because <see cref="ScheduledJobHost.TriggerNowAsync"/> tracks in-flight runs in
/// <c>_inFlight</c>, and admin triggers + scheduled triggers MUST share the same state.</para>
///
/// <para><b>Notification-playbook-scheduler seed (R3 task 023 / D2 / FR-2.8)</b>:
/// On module registration we seed a single <see cref="BackgroundJobDefinition"/> for
/// <see cref="PlaybookSchedulerJob.JobIdConstant"/> (<c>"notification-playbook-scheduler"</c>)
/// with cron <c>0 * * * *</c> (every hour at minute 0). This matches the legacy
/// <c>PlaybookSchedulerService.DefaultTickInterval = TimeSpan.FromHours(1)</c> exactly so
/// operators see no cadence regression (NFR-04). The seed lives in DI registration — when the
/// Dataverse-backed store lands, this seed will move to a one-shot Dataverse upsert at startup
/// (or a solution-import seed row) instead. Re-running <see cref="InMemoryBackgroundJobStore.AddOrReplaceJob"/>
/// on every startup is idempotent.</para>
///
/// <para><b>Lifetime choice</b>:
/// <see cref="ScheduledJobRegistry"/> + <see cref="InMemoryBackgroundJobStore"/> +
/// <see cref="ScheduledJobHostOptions"/> + <see cref="ScheduledJobHost"/> are all
/// <c>Singleton</c>. The registry mutates only at startup (via <c>Register</c>); the store
/// is a concurrent-dictionary keyed by jobId; the host owns the per-tick scheduling state.
/// Per-request scoping would corrupt all three.</para>
///
/// <para><b><see cref="PlaybookSchedulerJob"/> registration</b>:
/// Registered as a singleton and immediately added to the <see cref="ScheduledJobRegistry"/>
/// during DI registration via <see cref="RegisterPlaybookSchedulerJob"/>. The legacy
/// <c>PlaybookSchedulerService</c> BackgroundService (formerly registered in
/// <c>AnalysisServicesModule.AddPlaybookServices</c>) has been DELETED per R3 task 023 / D2 —
/// this job is the canonical replacement.</para>
/// </remarks>
public static class SchedulingModule
{
    /// <summary>
    /// Adds the in-process <c>Spaarke.Scheduling</c> registry + run-history store + host options
    /// + the <see cref="ScheduledJobHost"/> singleton + hosted-service registration + seed for the
    /// <see cref="PlaybookSchedulerJob.JobIdConstant"/> definition. Unconditional per
    /// bff-extensions.md §F.1 asymmetric-registration rule.
    /// </summary>
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        // ── Framework primitives ──────────────────────────────────────────────────────────────

        // Registry — populated below by RegisterPlaybookSchedulerJob via the singleton resolver
        // pattern (deferred to first request to ensure all dependencies are constructed).
        services.AddSingleton<ScheduledJobRegistry>();

        // Run-history store. Single instance shared by the ScheduledJobHost + admin endpoints.
        // Concrete also registered so feature modules can resolve the seeding surface
        // (AddOrReplaceJob / SeedRunRecord / RunRecords) directly.
        services.AddSingleton<InMemoryBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(sp => sp.GetRequiredService<InMemoryBackgroundJobStore>());

        // ScheduledJobHostOptions — default values are spec.md verbatim (FR-2.3 hourly refresh,
        // NFR-07 30s drain). Future projects can call PostConfigure to override.
        services.AddSingleton<ScheduledJobHostOptions>();

        // ScheduledJobHost — singleton instance shared by admin endpoints (R3 task 021) AND
        // the hosted-service registration below. Both surfaces MUST see the same in-flight
        // tracking dictionary (NFR-07 drain).
        services.AddSingleton<ScheduledJobHost>();

        // Task 023 (R3 FR-2.8) — promote ScheduledJobHost to a HostedService so the cron loop
        // actually runs. The forwarding resolver preserves the singleton identity (no second
        // instance is constructed). This MUST come after the singleton registration above so
        // GetRequiredService<ScheduledJobHost>() resolves the singleton, not a transient.
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>());

        // ── R3 task 023 — PlaybookSchedulerJob (first production cron consumer) ──────────────

        // PlaybookSchedulerJob handler — singleton (Spaarke.Scheduling contract) + registered
        // both as concrete and as IScheduledJob (forwarded) so feature consumers can resolve
        // either type. Constructor deps (IServiceScopeFactory, IConfiguration, ILogger) are
        // built-in framework services available unconditionally; per-tick work resolves
        // IGenericEntityService + IPlaybookOrchestrationService from a fresh scope.
        services.AddSingleton<PlaybookSchedulerJob>();
        services.AddSingleton<IScheduledJob>(sp => sp.GetRequiredService<PlaybookSchedulerJob>());

        // Hosted bootstrap that (a) seeds the InMemoryBackgroundJobStore definition row for
        // notification-playbook-scheduler and (b) registers the handler in ScheduledJobRegistry
        // BEFORE the ScheduledJobHost's first tick. Order matters: the bootstrap MUST run before
        // ScheduledJobHost.ExecuteAsync's first RefreshDefinitionsAsync, otherwise the host
        // sees no handler and skips dispatch.
        //
        // .NET hosted services start in registration order, so we add the bootstrap BEFORE the
        // host's hosted-service registration above… except we already added the host. To keep
        // both registrations and still guarantee bootstrap-first ordering, we manually insert
        // the bootstrap descriptor at the front of the hosted-services list.
        var bootstrapDescriptor = ServiceDescriptor.Singleton<IHostedService, SchedulingBootstrapHostedService>();
        // .NET runs hosted services in registration order; if the bootstrap is appended AFTER
        // the host the host's first tick races with seeding. AddHostedService just appends, so
        // we inject ourselves at index 0 of the collection.
        services.Insert(0, bootstrapDescriptor);

        return services;
    }

    /// <summary>
    /// One-shot startup hosted service that (1) registers <see cref="PlaybookSchedulerJob"/>
    /// with <see cref="ScheduledJobRegistry"/> and (2) seeds the
    /// <see cref="PlaybookSchedulerJob.JobIdConstant"/> definition in
    /// <see cref="InMemoryBackgroundJobStore"/>. Runs synchronously on host startup BEFORE
    /// <see cref="ScheduledJobHost"/>'s first tick (we inject this descriptor at index 0 of
    /// the hosted-services list).
    /// </summary>
    /// <remarks>
    /// <para><b>Why a hosted service and not a constructor side-effect</b>: registering with
    /// <see cref="ScheduledJobRegistry"/> from a constructor would require the registry to be
    /// resolved at PlaybookSchedulerJob construction time — workable but couples the handler's
    /// ctor to a framework primitive. The hosted-service pattern is the canonical .NET way to
    /// run startup logic with full DI access. Same pattern used by other feature modules that
    /// seed configuration tables on first boot.</para>
    /// <para><b>Idempotency</b>: <see cref="ScheduledJobRegistry.Register"/> throws on duplicate;
    /// the bootstrap catches and ignores. <see cref="InMemoryBackgroundJobStore.AddOrReplaceJob"/>
    /// is naturally idempotent. So a host restart re-runs both calls without harm.</para>
    /// <para><b>Cron schedule</b>: <c>0 * * * *</c> (every hour at minute 0). Matches the legacy
    /// <c>PlaybookSchedulerService.DefaultTickInterval = TimeSpan.FromHours(1)</c> exactly so
    /// NFR-04 (preserve cadence) holds. The per-playbook schedule-config check inside
    /// <see cref="PlaybookSchedulerJob.IsPlaybookDue"/> remains the final gate for whether
    /// individual playbooks dispatch on a given tick.</para>
    /// </remarks>
    internal sealed class SchedulingBootstrapHostedService : IHostedService
    {
        private readonly ScheduledJobRegistry _registry;
        private readonly InMemoryBackgroundJobStore _store;
        private readonly PlaybookSchedulerJob _playbookSchedulerJob;
        private readonly ILogger<SchedulingBootstrapHostedService> _logger;

        public SchedulingBootstrapHostedService(
            ScheduledJobRegistry registry,
            InMemoryBackgroundJobStore store,
            PlaybookSchedulerJob playbookSchedulerJob,
            ILogger<SchedulingBootstrapHostedService> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _playbookSchedulerJob = playbookSchedulerJob ?? throw new ArgumentNullException(nameof(playbookSchedulerJob));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // (1) Register handler — idempotent on host restart (catch duplicate).
            try
            {
                _registry.Register(_playbookSchedulerJob);
                _logger.LogInformation(
                    "Registered IScheduledJob '{JobId}' with ScheduledJobRegistry",
                    PlaybookSchedulerJob.JobIdConstant);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            {
                // Duplicate registration = host restart with persistent registry; safe to ignore.
                _logger.LogDebug(
                    "IScheduledJob '{JobId}' already registered — skipping",
                    PlaybookSchedulerJob.JobIdConstant);
            }

            // (2) Seed definition row in the in-memory store. Cron is fixed at 0 * * * * to
            // preserve the legacy 1h tick cadence (NFR-04). Enabled by default; admins can
            // disable via POST /api/admin/jobs/{jobId}/disable (R3 task 022).
            var definition = new BackgroundJobDefinition(
                JobId: PlaybookSchedulerJob.JobIdConstant,
                DisplayName: _playbookSchedulerJob.DisplayName,
                Description: _playbookSchedulerJob.Description,
                Enabled: true,
                CronSchedule: "0 * * * *",
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
