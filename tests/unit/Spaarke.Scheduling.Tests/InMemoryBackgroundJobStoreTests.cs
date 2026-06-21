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

        var scheduledFire = new DateTimeOffset(2026, 6, 21, 2, 0, 0, TimeSpan.Zero);
        var runId = await store.RecordRunStartAsync(
            "job-x", JobRunTrigger.Scheduled, "corr-1", scheduledFire, CancellationToken.None);
        runId.Should().NotBe(Guid.Empty);

        store.RunRecords.Should().HaveCount(1);
        var started = store.RunRecords.Single();
        started.JobId.Should().Be("job-x");
        started.Trigger.Should().Be(JobRunTrigger.Scheduled);
        started.CorrelationId.Should().Be("corr-1");
        started.ScheduledFireUtc.Should().Be(scheduledFire);
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
    public async Task HasRunForScheduledTimeAsync_TrueWhenPriorRunExists()
    {
        var store = new InMemoryBackgroundJobStore();
        var scheduledFire = new DateTimeOffset(2026, 6, 21, 2, 0, 0, TimeSpan.Zero);

        await store.RecordRunStartAsync(
            "recur-job", JobRunTrigger.Scheduled, "c", scheduledFire, CancellationToken.None);

        (await store.HasRunForScheduledTimeAsync("recur-job", scheduledFire, CancellationToken.None))
            .Should().BeTrue("a prior run for this scheduled time IS recorded");

        // Different job id at same time — no match.
        (await store.HasRunForScheduledTimeAsync("other-job", scheduledFire, CancellationToken.None))
            .Should().BeFalse();

        // Same job at different time — no match.
        (await store.HasRunForScheduledTimeAsync("recur-job", scheduledFire.AddMinutes(1), CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task HasRunForScheduledTimeAsync_FalseWhenStoreEmpty()
    {
        var store = new InMemoryBackgroundJobStore();
        (await store.HasRunForScheduledTimeAsync(
            "no-runs", DateTimeOffset.UtcNow, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task HasRunForScheduledTimeAsync_IgnoresRunsWithoutScheduledFire()
    {
        // Manual-trigger runs (no ScheduledFireUtc) MUST NOT match a scheduled idempotency probe.
        var store = new InMemoryBackgroundJobStore();
        await store.RecordRunStartAsync(
            "j", JobRunTrigger.ManualAdmin, "c", scheduledFireUtc: null, CancellationToken.None);

        (await store.HasRunForScheduledTimeAsync(
            "j", new DateTimeOffset(2026, 6, 21, 2, 0, 0, TimeSpan.Zero), CancellationToken.None))
            .Should().BeFalse();
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
        var act = async () => await store.RecordRunStartAsync(
            "j", JobRunTrigger.Scheduled, "", scheduledFireUtc: null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
