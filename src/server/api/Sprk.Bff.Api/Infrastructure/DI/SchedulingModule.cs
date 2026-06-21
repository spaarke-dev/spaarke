using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the in-process Spaarke.Scheduling background-job framework
/// (R3 Part 2 — FR-2.1 through FR-2.6). Registers the job registry, run-history store, and
/// host options unconditionally so the admin endpoints (R3 task 020) can resolve their
/// dependencies on every BFF startup.
/// </summary>
/// <remarks>
/// <para><b>Asymmetric-registration compliance (bff-extensions.md §F.1)</b>:
/// The <c>/api/admin/jobs/*</c> endpoints map UNCONDITIONALLY in
/// <see cref="EndpointMappingExtensions"/>. Their dependencies
/// (<see cref="ScheduledJobRegistry"/> + <see cref="IBackgroundJobStore"/>) therefore MUST
/// also register unconditionally — failing to do so would reproduce the RB-T028-03/04/05/06
/// pattern (filed 2026-05-31, fixed by `sdap.bff.api-test-suite-repair-r2` task 011 via the
/// 18-service Null-Object migration). This module is intentionally NOT feature-gated.</para>
///
/// <para><b>Backing store choice</b>:
/// Wires <see cref="InMemoryBackgroundJobStore"/> as the <see cref="IBackgroundJobStore"/>
/// implementation for the early-wave (P1–P3) BFF deployments. Tasks 015/016 deliver the
/// <c>sprk_backgroundjob</c> / <c>sprk_backgroundjobrun</c> Dataverse entities; task 023+
/// will introduce a Dataverse-backed store that swaps this registration. The in-memory store
/// loses run history on host restart — acceptable for P3 admin-surface validation; production
/// hardening is the task-023 deliverable.</para>
///
/// <para><b>Lifetime choice</b>:
/// Both services are <c>Singleton</c>. <see cref="ScheduledJobRegistry"/> mutates only at
/// startup (via <c>Register</c>) and is read concurrently afterward — its
/// <c>ConcurrentDictionary</c> backing is thread-safe. <see cref="InMemoryBackgroundJobStore"/>
/// is also a singleton concurrent-dictionary store; per-request scoping would lose all
/// run history between requests.</para>
///
/// <para><b>What this module does NOT register as a HostedService</b>:
/// <see cref="ScheduledJobHost"/> is registered as a Singleton (so admin endpoints can
/// inject it for manual triggers per R3 task 021) but NOT via <c>AddHostedService</c>. The
/// downstream BackgroundService activation is deferred to whichever future task first
/// requires automatic cron dispatch — e.g., task 023 <c>PlaybookSchedulerService</c>
/// migration, which will call <c>services.AddHostedService(sp =&gt; sp.GetRequiredService&lt;ScheduledJobHost&gt;())</c>.
/// Until then the host instance is a passive dispatcher used solely by
/// <c>POST /api/admin/jobs/{jobId}/trigger</c> (no cron loop runs).</para>
/// </remarks>
public static class SchedulingModule
{
    /// <summary>
    /// Adds the in-process <c>Spaarke.Scheduling</c> registry + run-history store + the
    /// <see cref="ScheduledJobHost"/> singleton so the admin endpoints (R3 tasks 020 + 021)
    /// can resolve their dependencies. Unconditional per bff-extensions.md §F.1
    /// asymmetric-registration rule.
    /// </summary>
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        // Registry — populated at DI registration time by feature modules calling
        // ScheduledJobRegistry.Register(IScheduledJob) during their AddXxx() extension.
        services.AddSingleton<ScheduledJobRegistry>();

        // Run-history store. Single instance shared by the ScheduledJobHost + admin
        // endpoints. Concrete also registered so future code that wants the seeding
        // surface (AddOrReplaceJob / SeedRunRecord / RunRecords) can resolve it directly.
        services.AddSingleton<InMemoryBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(sp => sp.GetRequiredService<InMemoryBackgroundJobStore>());

        // ScheduledJobHostOptions — default values are spec.md verbatim (FR-2.3 hourly
        // refresh, NFR-07 30s drain). Future projects can call PostConfigure to override.
        services.AddSingleton<ScheduledJobHostOptions>();

        // ScheduledJobHost — registered as singleton (NOT AddHostedService) so the admin
        // trigger endpoint (R3 task 021) can resolve it for out-of-band dispatches via
        // TriggerNowAsync without starting the cron BackgroundService loop. When the first
        // production cron job migrates to Spaarke.Scheduling (task 023+), that project
        // will register the SAME instance as IHostedService via:
        //     services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>());
        // bff-extensions.md §F.1 compliance: /trigger maps unconditionally → host must
        // register unconditionally too. Mirrors the RB-T028-03/04/05/06 anti-pattern fix.
        services.AddSingleton<ScheduledJobHost>();

        return services;
    }
}
