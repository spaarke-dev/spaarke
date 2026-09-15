using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Spaarke.Scheduling;

/// <summary>
/// One job registered with <see cref="ScheduledJobServiceCollectionExtensions.AddScheduledJob{TJob}"/>: the handler
/// and the schedule it starts with.
/// </summary>
/// <remarks>
/// <see cref="ScheduledJobRegistry"/> and <see cref="InMemoryBackgroundJobStore"/> read every registration in their
/// dependency-injection constructors, so a job is registered and its definition seeded before anything can resolve
/// the host. That is the whole bootstrap: no per-job hosted service, and no dependence on hosted-service start order
/// (ADR-036 A1 rule 6).
/// </remarks>
/// <param name="Job">The handler.</param>
/// <param name="CronSchedule">Cronos expression, 5-field (minutes) or 6-field (seconds).</param>
/// <param name="Enabled">Whether the job starts enabled.</param>
public sealed record ScheduledJobRegistration(IScheduledJob Job, string CronSchedule, bool Enabled)
{
    /// <summary>The definition this registration seeds.</summary>
    public BackgroundJobDefinition ToDefinition() =>
        new(Job.JobId, Job.DisplayName, Job.Description, Enabled, CronSchedule, ConfigJson: null);
}

/// <summary>Registration helper for scheduled jobs (ADR-036 A1 rule 6).</summary>
public static class ScheduledJobServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TJob"/> as a singleton scheduled job that the host runs on
    /// <paramref name="cronSchedule"/>. The only way a job reaches the scheduler — no per-job bootstrap class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cronSchedule">Cronos expression, e.g. <c>"0 2 * * *"</c> = daily at 02:00 UTC.</param>
    /// <param name="enabled">Whether the job starts enabled.</param>
    /// <exception cref="Cronos.CronFormatException">The schedule does not parse — the app fails at startup rather
    /// than the host skipping the job at its first refresh.</exception>
    public static IServiceCollection AddScheduledJob<TJob>(
        this IServiceCollection services,
        string cronSchedule,
        bool enabled = true)
        where TJob : class, IScheduledJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronSchedule);

        var cron = cronSchedule.Trim();
        ScheduledJobHost.ParseCron(cron);

        services.TryAddSingleton<TJob>();
        services.AddSingleton<ScheduledJobRegistration>(sp =>
            new ScheduledJobRegistration(sp.GetRequiredService<TJob>(), cron, enabled));
        return services;
    }
}
