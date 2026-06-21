using FluentAssertions;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.Scheduling.Tests;

public class InMemoryBackgroundJobStoreTests
{
    [Fact]
    public async Task LoadJobsAsync_ReturnsAllSeededDefinitions()
    {
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition("job-a", "A", "First", true, "0 2 * * *", null));
        store.AddOrReplaceJob(new BackgroundJobDefinition("job-b", "B", "Second", false, "0 3 * * *", "{}"));

        var loaded = await store.LoadJobsAsync(CancellationToken.None);

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(d => d.JobId == "job-a" && d.Enabled);
        loaded.Should().Contain(d => d.JobId == "job-b" && !d.Enabled);
    }

    [Fact]
    public async Task RecordRunStart_ThenComplete_RoundTripsState()
    {
        var store = new InMemoryBackgroundJobStore();

        var runId = await store.RecordRunStartAsync("job-x", JobRunTrigger.Scheduled, "corr-1", CancellationToken.None);
        runId.Should().NotBe(Guid.Empty);

        store.RunRecords.Should().HaveCount(1);
        var started = store.RunRecords.Single();
        started.JobId.Should().Be("job-x");
        started.Trigger.Should().Be(JobRunTrigger.Scheduled);
        started.CorrelationId.Should().Be("corr-1");
        started.CompletedAtUtc.Should().BeNull();
        started.Result.Should().BeNull();

        var result = new JobRunResult(true, null, 42, TimeSpan.FromMilliseconds(123));
        await store.RecordRunCompleteAsync(runId, result, CancellationToken.None);

        var done = store.RunRecords.Single();
        done.RunId.Should().Be(runId);
        done.CompletedAtUtc.Should().NotBeNull();
        done.Result.Should().NotBeNull();
        done.Result!.Success.Should().BeTrue();
        done.Result.ProcessedItems.Should().Be(42);
    }

    [Fact]
    public async Task RemoveJob_StopsAppearingInLoad()
    {
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition("ephemeral", "E", "", true, "0 * * * *", null));

        store.RemoveJob("ephemeral").Should().BeTrue();
        (await store.LoadJobsAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task RecordRunStart_NullCorrelation_Throws()
    {
        var store = new InMemoryBackgroundJobStore();
        var act = async () => await store.RecordRunStartAsync("j", JobRunTrigger.Scheduled, "", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
