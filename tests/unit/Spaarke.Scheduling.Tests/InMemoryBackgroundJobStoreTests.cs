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

    // ================================================================================
    // ===== Task 022: GetRecentRunsAsync + SetEnabledAsync ==========================
    // ================================================================================

    [Fact]
    public async Task GetRecentRunsAsync_ReturnsNewestFirst_AndRespectsLimit()
    {
        // Seed 5 runs over a 5-minute window; ask for 3 — the newest 3 should come back.
        var store = new InMemoryBackgroundJobStore();
        var baseTime = new DateTimeOffset(2026, 6, 21, 1, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            store.SeedRunRecord(new InMemoryBackgroundJobStore.RunRecord(
                RunId: Guid.NewGuid(),
                JobId: "history-job",
                Trigger: JobRunTrigger.Scheduled,
                CorrelationId: $"corr-{i}",
                ScheduledFireUtc: baseTime.AddMinutes(i),
                StartedAtUtc: baseTime.AddMinutes(i),
                CompletedAtUtc: baseTime.AddMinutes(i).AddSeconds(1),
                Result: new JobRunResult(Success: true, ErrorMessage: null, ProcessedItems: i, Duration: TimeSpan.FromSeconds(1))));
        }

        var runs = await store.GetRecentRunsAsync("history-job", limit: 3, CancellationToken.None);

        runs.Should().HaveCount(3);
        // Newest first: indexes 4, 3, 2 by StartedAt — verify ProcessedItems matches.
        runs[0].ProcessedItems.Should().Be(4);
        runs[1].ProcessedItems.Should().Be(3);
        runs[2].ProcessedItems.Should().Be(2);
    }

    [Fact]
    public async Task GetRecentRunsAsync_EmptyJob_ReturnsEmptyList()
    {
        var store = new InMemoryBackgroundJobStore();
        var runs = await store.GetRecentRunsAsync("never-run", limit: 10, CancellationToken.None);
        runs.Should().NotBeNull();
        runs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentRunsAsync_LimitClampedToOne_WhenZeroOrNegative()
    {
        var store = new InMemoryBackgroundJobStore();
        store.SeedRunRecord(new InMemoryBackgroundJobStore.RunRecord(
            RunId: Guid.NewGuid(),
            JobId: "clamp-job",
            Trigger: JobRunTrigger.Scheduled,
            CorrelationId: "c",
            ScheduledFireUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Result: new JobRunResult(true, null, 0, TimeSpan.Zero)));

        // limit=0 → clamped to 1; limit=-5 → clamped to 1.
        (await store.GetRecentRunsAsync("clamp-job", 0, CancellationToken.None)).Should().HaveCount(1);
        (await store.GetRecentRunsAsync("clamp-job", -5, CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task SetEnabledAsync_FlipsEnabledFlag_AndReturnsTrue()
    {
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition("enable-flip", "Flip", "", true, "0 * * * *", null));

        var updated = await store.SetEnabledAsync("enable-flip", enabled: false, CancellationToken.None);

        updated.Should().BeTrue("the definition exists, so the update should succeed");
        var def = (await store.LoadJobsAsync(CancellationToken.None)).Single();
        def.Enabled.Should().BeFalse();
        def.JobId.Should().Be("enable-flip");
        // Other fields must be preserved by the with-expression copy.
        def.DisplayName.Should().Be("Flip");
        def.CronSchedule.Should().Be("0 * * * *");
    }

    [Fact]
    public async Task SetEnabledAsync_NoOp_WhenAlreadyInDesiredState_StillReturnsTrue()
    {
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition("noop", "N", "", true, "0 * * * *", null));

        // Re-enable an already-enabled definition.
        var updated = await store.SetEnabledAsync("noop", enabled: true, CancellationToken.None);

        updated.Should().BeTrue("a no-op enable on an already-enabled job is treated as a successful update so the endpoint still returns 204");
    }

    [Fact]
    public async Task SetEnabledAsync_ReturnsFalse_WhenJobIdMissing()
    {
        var store = new InMemoryBackgroundJobStore();

        var updated = await store.SetEnabledAsync("never-seeded", enabled: true, CancellationToken.None);

        updated.Should().BeFalse("the endpoint maps the false return to ProblemDetails 404");
    }

    [Fact]
    public async Task SetEnabledAsync_ThrowsArgumentException_WhenJobIdNullOrEmpty()
    {
        var store = new InMemoryBackgroundJobStore();

        var act = async () => await store.SetEnabledAsync("", enabled: true, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
