using Cronos;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Spaarke.Scheduling.Tests;

/// <summary>
/// ADR-036 A1 rule 6 (task 103): <c>AddScheduledJob&lt;TJob&gt;</c> is the only way a job reaches the scheduler. These
/// tests pin what replaced the three per-job bootstrap hosted services: the registry and the store fill themselves
/// from the registrations, so a job registered this way is runnable, with the schedule it was given.
/// </summary>
public class AddScheduledJobTests
{
    [Fact]
    public async Task AddScheduledJob_RegistryAndStoreBuiltByTheContainer_HoldTheJobWithItsSchedule()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScheduledJobRegistry>();
        services.AddSingleton<InMemoryBackgroundJobStore>();
        services.AddScheduledJob<NightlyJob>("0 2 * * *", enabled: false);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ScheduledJobRegistry>();
        var store = provider.GetRequiredService<InMemoryBackgroundJobStore>();

        registry.Resolve(NightlyJob.Id).Should().BeSameAs(provider.GetRequiredService<NightlyJob>(),
            "the registry holds the singleton the container built");
        var definition = (await store.LoadJobsAsync(CancellationToken.None)).Single();
        definition.JobId.Should().Be(NightlyJob.Id);
        definition.CronSchedule.Should().Be("0 2 * * *");
        definition.Enabled.Should().BeFalse();
        definition.DisplayName.Should().Be("Nightly");
    }

    [Fact]
    public void AddScheduledJob_UnparseableSchedule_FailsAtRegistration()
    {
        var register = () => new ServiceCollection().AddScheduledJob<NightlyJob>("every night");

        register.Should().Throw<CronFormatException>("a bad schedule fails the app at startup, not silently at the first refresh");
    }

    [Fact]
    public void AddScheduledJob_TwoJobsWithOneJobId_FailWhenTheRegistryIsBuilt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScheduledJobRegistry>();
        services.AddScheduledJob<NightlyJob>("0 2 * * *");
        services.AddScheduledJob<NightlyJobTwin>("0 3 * * *");

        using var provider = services.BuildServiceProvider();
        var build = () => provider.GetRequiredService<ScheduledJobRegistry>();

        build.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    private sealed class NightlyJob : IScheduledJob
    {
        public const string Id = "nightly";
        public string JobId => Id;
        public string DisplayName => "Nightly";
        public string Description => "Runs at night";

        public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
    }

    private sealed class NightlyJobTwin : IScheduledJob
    {
        public string JobId => NightlyJob.Id;
        public string DisplayName => "Nightly twin";
        public string Description => "Claims the same id";

        public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
    }
}
