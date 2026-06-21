using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.Scheduling.Tests;

public class ScheduledJobHostTests
{
    // Test convention: jobs scheduled with the 6-field cron "* * * * * *" (every second) fire
    // rapidly so tests stay sub-second. The host transparently supports both 5- and 6-field
    // cron via ScheduledJobHost.ParseCron — see also CronosParsingTests + the ParseCron test.

    private static ScheduledJobHostOptions FastOptions(TimeSpan? drainTimeout = null) => new()
    {
        RefreshInterval = TimeSpan.FromMilliseconds(200),
        ShutdownDrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(2),
        MaxLoopSleep = TimeSpan.FromMilliseconds(200)
    };

    private static BackgroundJobDefinition EverySecond(string jobId, bool enabled = true) =>
        new(JobId: jobId,
            DisplayName: $"Test {jobId}",
            Description: $"Test job {jobId}",
            Enabled: enabled,
            CronSchedule: "* * * * * *", // 6-field (Cronos seconds-mode) — fires every second
            ConfigJson: null);

    [Fact]
    public void ParseCron_Supports6FieldSecondsMode()
    {
        var cron = ScheduledJobHost.ParseCron("*/2 * * * * *");
        var next = cron.GetNextOccurrence(new DateTime(2026, 6, 21, 1, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
        next.Should().Be(new DateTime(2026, 6, 21, 1, 0, 2, DateTimeKind.Utc));
    }

    [Fact]
    public void ParseCron_Defaults5FieldMinutesMode()
    {
        var cron = ScheduledJobHost.ParseCron("0 2 * * *");
        var next = cron.GetNextOccurrence(new DateTime(2026, 6, 21, 1, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
        next.Should().Be(new DateTime(2026, 6, 21, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task EmptyRegistry_HostStartsAndStopsCleanly_NoDispatches()
    {
        var registry = new ScheduledJobRegistry();
        var store = new InMemoryBackgroundJobStore();
        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await host.StopAsync(CancellationToken.None);

        store.RunRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredJob_WithoutDefinition_NotDispatchedAndCleanShutdown()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("orphan-handler");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(400);
        await host.StopAsync(CancellationToken.None);

        fake.InvocationCount.Should().Be(0);
        store.RunRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task DefinitionWithoutHandler_NotDispatched()
    {
        var registry = new ScheduledJobRegistry();
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("missing-handler"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(2000); // 2 seconds — well past first fire interval if any handler existed
        await host.StopAsync(CancellationToken.None);

        store.RunRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task DisabledDefinition_NotDispatched()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("disabled-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("disabled-job", enabled: false));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(2000);
        await host.StopAsync(CancellationToken.None);

        fake.InvocationCount.Should().Be(0);
        store.RunRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_DueJob_RunsHandlerAndRecordsRun()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("due-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("due-job"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5));
        await Task.Delay(200); // let run-complete record write
        await host.StopAsync(CancellationToken.None);

        fake.InvocationCount.Should().BeGreaterThan(0);
        var dueRuns = store.RunRecords.Where(r => r.JobId == "due-job").ToList();
        dueRuns.Should().NotBeEmpty();
        var run = dueRuns.First();
        run.CorrelationId.Should().NotBeNullOrEmpty();
        run.Trigger.Should().Be(JobRunTrigger.Scheduled);
        run.Result.Should().NotBeNull();
        run.Result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunContext_CarriesFreshCorrelationIdPerRun_NFR08()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("corr-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("corr-job"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => fake.InvocationCount >= 2, TimeSpan.FromSeconds(6));
        await host.StopAsync(CancellationToken.None);

        var corrJobs = store.RunRecords.Where(r => r.JobId == "corr-job").ToList();
        corrJobs.Should().HaveCountGreaterThanOrEqualTo(2, "two ticks should produce two distinct run records");

        // Distinct correlation ids per run (NFR-08).
        corrJobs.Select(r => r.CorrelationId).Distinct().Should().HaveCountGreaterThanOrEqualTo(2);
        fake.LastContext.Should().NotBeNull();
        fake.LastContext!.CorrelationId.Should().NotBeNullOrEmpty();
        fake.LastContext.RunId.Should().NotBe(Guid.Empty);
        fake.LastContext.Trigger.Should().Be(JobRunTrigger.Scheduled);
    }

    [Fact]
    public async Task RefreshTick_PicksUpDefinitionAddedAtRuntime()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("late-add");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        // No definitions seeded yet.

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(300); // first refresh ticks elapse with empty state
        fake.InvocationCount.Should().Be(0, "no definitions yet => no dispatch");

        // Add the definition at runtime — next refresh tick must pick it up + start scheduling it.
        store.AddOrReplaceJob(EverySecond("late-add"));

        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5),
            because: "the hourly refresh tick (set to 200ms in test) MUST pick up the new definition");
        await host.StopAsync(CancellationToken.None);

        store.RunRecords.Should().Contain(r => r.JobId == "late-add");
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07()
    {
        var observed = false;
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeScheduledJob("slow", async (ctx, ct) =>
        {
            startedTcs.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new JobRunResult(true, null, 0, TimeSpan.Zero);
            }
            catch (OperationCanceledException)
            {
                observed = true;
                throw;
            }
        });

        var registry = new ScheduledJobRegistry();
        registry.Register(slow);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("slow"));

        var host = new ScheduledJobHost(registry, store, FastOptions(TimeSpan.FromSeconds(3)),
            NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopSw = Stopwatch.StartNew();
        await host.StopAsync(CancellationToken.None);
        stopSw.Stop();

        observed.Should().BeTrue("the in-flight job MUST observe cancellation (NFR-07)");
        stopSw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "StopAsync MUST drain within ShutdownDrainTimeout + a small overhead (NFR-07: 30s ceiling)");

        var slowRun = store.RunRecords.FirstOrDefault(r => r.JobId == "slow");
        slowRun.Should().NotBeNull();
        slowRun!.Result.Should().NotBeNull();
        slowRun.Result!.Success.Should().BeFalse();
        slowRun.Result.ErrorMessage.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task InvalidCronExpression_LoggedAndJobSkipped_HostKeepsRunning()
    {
        var registry = new ScheduledJobRegistry();
        var good = new FakeScheduledJob("good");
        var bad = new FakeScheduledJob("bad");
        registry.Register(good);
        registry.Register(bad);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("good"));
        store.AddOrReplaceJob(new BackgroundJobDefinition(
            "bad", "Bad", "", true, "not-a-cron-expression", null));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => good.InvocationCount > 0, TimeSpan.FromSeconds(5));
        await host.StopAsync(CancellationToken.None);

        good.InvocationCount.Should().BeGreaterThan(0);
        bad.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfigJson_FlowedToJobRunContextParameters()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("cfg-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition(
            "cfg-job", "Cfg", "", true, "* * * * * *", ConfigJson: "{\"foo\":42}"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5));
        await host.StopAsync(CancellationToken.None);

        fake.LastContext.Should().NotBeNull();
        fake.LastContext!.Parameters.Should().ContainKey("configJson");
        fake.LastContext.Parameters["configJson"].Should().Be("{\"foo\":42}");
        fake.LastContext.Parameters.Should().ContainKey("jobId");
        fake.LastContext.Parameters["jobId"].Should().Be("cfg-job");
    }

    /// <summary>Polls a predicate until satisfied or the deadline elapses (xUnit-friendly wait helper).</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string? because = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return;
            await Task.Delay(50);
        }
        if (!predicate())
        {
            throw new TimeoutException($"Predicate did not become true within {timeout}{(because is null ? "" : " — " + because)}");
        }
    }
}
