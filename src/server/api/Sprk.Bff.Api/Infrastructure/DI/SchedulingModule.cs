using Microsoft.Extensions.Options;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Scheduling;
using Sprk.Bff.Api.Services.Ai;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for the in-process Spaarke.Scheduling background-job framework
/// (R3 Part 2 — FR-2.1 through FR-2.8; ADR-036 as amended by A1). Registers the job registry, run-history store,
/// host options, the dispatch lease and the canonical <see cref="ScheduledJobHost"/> singleton (which doubles as an
/// <see cref="IHostedService"/>), all unconditionally, so the admin endpoints resolve their dependencies on every
/// BFF startup and the cron loop actually fires.
/// </summary>
/// <remarks>
/// <para><b>Asymmetric-registration compliance (bff-extensions.md §F.1)</b>: the <c>/api/admin/jobs/*</c> endpoints
/// map UNCONDITIONALLY in <see cref="EndpointMappingExtensions"/>, so their dependencies register unconditionally too.
/// The lease is symmetric the same way: Redis enabled → <see cref="RedisScheduledJobLease"/>; Redis off (Development /
/// Testing only — <see cref="CacheModule"/> refuses to start a deployed environment without Redis) →
/// <see cref="ProcessLocalScheduledJobLease"/>, and the host warns once that dispatch is not distributed.</para>
///
/// <para><b>Registering a job</b> (ADR-036 A1 rule 6): <c>services.AddScheduledJob&lt;TJob&gt;(cron, enabled)</c>,
/// from whichever module owns the job, in any order. <see cref="ScheduledJobRegistry"/> and
/// <see cref="InMemoryBackgroundJobStore"/> read every registration in their constructors — there is no bootstrap
/// hosted service, and nothing depends on hosted-service start order. (Task 103 deleted the three per-job bootstraps
/// and the index-0 insertion they needed.)</para>
///
/// <para><b>Backing store</b>: <see cref="InMemoryBackgroundJobStore"/> — run history is process-local and definitions
/// are re-seeded on every start. The Dataverse-backed store (<c>sprk_backgroundjob</c> / <c>sprk_backgroundjobrun</c>)
/// is the ADR-036 target state.</para>
///
/// <para><b>Host identity</b>: <see cref="ScheduledJobHost"/> is registered once as a singleton and forwarded to
/// <c>AddHostedService</c>, so the admin trigger and the cron loop share the same in-flight tracking (NFR-07).</para>
///
/// <para><b>Slot guard</b> (ADR-036 A1 rule 2): <c>Scheduling:RunScheduledJobs=false</c> — set slot-sticky on
/// non-production deployment slots by <c>scripts/Deploy-BffApi.ps1 -UseSlotDeploy</c> — stops the cron loop on that
/// host. Absent means on.</para>
///
/// <para><b><see cref="PlaybookSchedulerJob"/></b> (R3 task 023, the first production cron job) runs at
/// <c>0 * * * *</c>, matching the legacy <c>PlaybookSchedulerService</c> one-hour tick exactly (NFR-04). Each
/// playbook's own <c>IsPlaybookDue</c> check stays the final gate per tick.</para>
/// </remarks>
public static class SchedulingModule
{
    /// <summary>
    /// Configuration key for the slot guard (ADR-036 A1 rule 2); App Service app setting
    /// <c>Scheduling__RunScheduledJobs</c>.
    /// </summary>
    internal const string RunScheduledJobsSetting = "Scheduling:RunScheduledJobs";

    /// <summary>
    /// Adds the <c>Spaarke.Scheduling</c> registry, run-history store, host options, dispatch lease, the
    /// <see cref="ScheduledJobHost"/> singleton + hosted-service registration, and
    /// <see cref="PlaybookSchedulerJob"/>. Unconditional per bff-extensions.md §F.1.
    /// </summary>
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        // Both fill themselves from every AddScheduledJob registration (their DI constructors).
        services.AddSingleton<ScheduledJobRegistry>();
        services.AddSingleton<InMemoryBackgroundJobStore>();
        services.AddSingleton<IBackgroundJobStore>(sp => sp.GetRequiredService<InMemoryBackgroundJobStore>());

        services.AddSingleton(sp => new ScheduledJobHostOptions
        {
            RunScheduledJobs = sp.GetRequiredService<IConfiguration>().GetValue(RunScheduledJobsSetting, defaultValue: true),
        });

        services.AddSingleton<IScheduledJobLease>(sp =>
        {
            var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>();
            return redisOptions.Value.Enabled
                ? new RedisScheduledJobLease(sp.GetRequiredService<IConnectionMultiplexer>(), redisOptions)
                : new ProcessLocalScheduledJobLease();
        });

        services.AddSingleton<ScheduledJobHost>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>());

        services.AddScheduledJob<PlaybookSchedulerJob>("0 * * * *");

        return services;
    }
}
