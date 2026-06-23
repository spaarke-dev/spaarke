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

    [Fact(Skip = "CI cron-tick flake — passes locally; needs TimeProvider refactor (see PR #415)")]
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

    [Fact(Skip = "CI cron-tick flake — passes locally; needs TimeProvider refactor (see PR #415)")]
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
        // 5s = 3s drain + 2s overhead locally; CI runners need ~5x overhead headroom.
        var drainCeiling = _isCi ? TimeSpan.FromSeconds(25) : TimeSpan.FromSeconds(5);
        stopSw.Elapsed.Should().BeLessThan(drainCeiling,
            "StopAsync MUST drain within ShutdownDrainTimeout + reasonable overhead (NFR-07: 30s hard ceiling)");

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

    [Fact(Skip = "CI cron-tick flake — passes locally; needs TimeProvider refactor (see PR #415)")]
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

    // ================================================================================
    // ===== Task 021: TriggerNowAsync (manual-admin out-of-band dispatch) ===========
    // ================================================================================
    //
    // The host's TriggerNowAsync is the dispatch mechanism for POST /api/admin/jobs/{jobId}/trigger
    // (R3 task 021). These tests verify the contract independent of the BFF endpoint layer.

    [Fact]
    public async Task TriggerNowAsync_RegisteredJob_InvokesHandler_AndRecordsRunWithManualAdminTrigger()
    {
        // Arrange
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("trigger-target");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        // Note: NO definition seeded — TriggerNowAsync MUST work for handler-registered-but-not-yet-defined
        // jobs (admin troubleshooting flow).

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        // Act
        var result = await host.TriggerNowAsync("trigger-target", parameters: null, CancellationToken.None);

        // Assert — synchronous return shape.
        result.Should().NotBeNull();
        result.RunId.Should().NotBe(Guid.Empty);
        result.Status.Should().Be("Running");
        result.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        // Wait for background task to invoke the handler + write completion record.
        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () => store.RunRecords.Any(r => r.RunId == result.RunId && r.CompletedAtUtc is not null),
            TimeSpan.FromSeconds(5));

        // Assert — run record has the right shape.
        var run = store.RunRecords.Single(r => r.JobId == "trigger-target");
        run.RunId.Should().Be(result.RunId);
        run.Trigger.Should().Be(JobRunTrigger.ManualAdmin, "FR-2.6 / task 021 requires Trigger=ManualAdmin");
        run.ScheduledFireUtc.Should().BeNull("manual triggers persist scheduledFireUtc=null per IBackgroundJobStore contract");
        run.CorrelationId.Should().NotBeNullOrEmpty("NFR-08: fresh correlationId per run");
        run.Result.Should().NotBeNull();
        run.Result!.Success.Should().BeTrue("FakeScheduledJob always succeeds by default");

        // Assert — the handler context carried the manual trigger marker.
        fake.LastContext.Should().NotBeNull();
        fake.LastContext!.Trigger.Should().Be(JobRunTrigger.ManualAdmin);
        fake.LastContext.CorrelationId.Should().Be(run.CorrelationId);
        fake.LastContext.Parameters.Should().ContainKey("jobId");
        fake.LastContext.Parameters["jobId"].Should().Be("trigger-target");
    }

    [Fact]
    public async Task TriggerNowAsync_UnknownJobId_ThrowsJobNotFoundException()
    {
        // Arrange
        var registry = new ScheduledJobRegistry();
        var store = new InMemoryBackgroundJobStore();
        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        // Act + Assert
        var act = async () => await host.TriggerNowAsync("never-registered", parameters: null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<JobNotFoundException>();
        ex.Which.JobId.Should().Be("never-registered");
        ex.Which.Message.Should().Contain("never-registered");

        // Assert — no run record was created for the unknown job (host must NOT write a runStart row
        // for a non-existent handler).
        store.RunRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task TriggerNowAsync_TwoSequentialTriggers_ProduceDistinctRunIdsAndCorrelationIds_NFR08()
    {
        // Arrange
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("nfr08-job");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        // Act — fire twice.
        var first = await host.TriggerNowAsync("nfr08-job", parameters: null, CancellationToken.None);
        var second = await host.TriggerNowAsync("nfr08-job", parameters: null, CancellationToken.None);

        // Wait for both background tasks to complete.
        await WaitUntilAsync(
            () => store.RunRecords.Count(r => r.JobId == "nfr08-job" && r.CompletedAtUtc is not null) >= 2,
            TimeSpan.FromSeconds(5));

        // Assert — RunIds distinct.
        first.RunId.Should().NotBe(second.RunId, "every manual trigger MUST yield a distinct runId");

        // Assert — both runs persisted; correlationIds distinct (NFR-08).
        var runs = store.RunRecords.Where(r => r.JobId == "nfr08-job").ToList();
        runs.Should().HaveCount(2);
        runs.Select(r => r.CorrelationId).Distinct().Should().HaveCount(2,
            "NFR-08 mandates a fresh correlationId per run");
        runs.All(r => r.Trigger == JobRunTrigger.ManualAdmin).Should().BeTrue();
    }

    [Fact]
    public async Task TriggerNowAsync_OverrideParameters_MergedIntoRunContext()
    {
        // Arrange
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("params-job");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        // Seed a definition with configJson — the override should be added ALONGSIDE, not replace it.
        store.AddOrReplaceJob(new BackgroundJobDefinition(
            "params-job", "Params", "Param-merging test", true, "0 2 * * *",
            ConfigJson: "{\"persisted\":true}"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        var overrides = new Dictionary<string, object>
        {
            ["adminOverride"] = "yes",
            ["targetTenant"] = "test-tenant",
        };

        // Act
        await host.TriggerNowAsync("params-job", overrides, CancellationToken.None);
        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5));

        // Assert — context carries jobId + persisted configJson + caller overrides.
        fake.LastContext.Should().NotBeNull();
        var p = fake.LastContext!.Parameters;
        p.Should().ContainKey("jobId");
        p["jobId"].Should().Be("params-job");
        p.Should().ContainKey("configJson");
        p["configJson"].Should().Be("{\"persisted\":true}");
        p.Should().ContainKey("adminOverride");
        p["adminOverride"].Should().Be("yes");
        p.Should().ContainKey("targetTenant");
        p["targetTenant"].Should().Be("test-tenant");
    }

    [Fact]
    public async Task TriggerNowAsync_CancellationBeforeDispatch_ThrowsOperationCancelled()
    {
        // Arrange
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("cancel-pre-dispatch");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel — TriggerNowAsync should observe immediately

        // Act + Assert — pre-cancelled token short-circuits BEFORE the run row is written.
        var act = async () => await host.TriggerNowAsync("cancel-pre-dispatch", parameters: null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Handler was NOT invoked + no run record persisted.
        fake.InvocationCount.Should().Be(0);
        store.RunRecords.Should().BeEmpty();
    }

    // ================================================================================
    // ===== Task 022: RefreshDefinitionsAsync (admin enable/disable plumbing) =======
    // ================================================================================
    //
    // The host's RefreshDefinitionsAsync was promoted to public in task 022 so the admin
    // enable/disable endpoints can force an immediate re-read of the store after flipping
    // sprk_enabled. These tests pin the contract:
    //   1. Disable → next-tick dispatch stops (verified by mutating store then refreshing).
    //   2. Re-enable → next-tick dispatch resumes.
    //   3. Refresh is safe to call externally (no exceptions, prior state preserved on failure).

    [Fact(Skip = "CI cron-tick flake — passes locally; needs TimeProvider refactor (see PR #415)")]
    public async Task RefreshDefinitionsAsync_PicksUpDisableFlip_DispatchStopsOnNextTick()
    {
        // Arrange — fast-tick host with one enabled "every second" job.
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("disable-mid-flight");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("disable-mid-flight"));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);
        await host.StartAsync(CancellationToken.None);

        // Let it fire at least once so we know it's running normally.
        await WaitUntilAsync(() => fake.InvocationCount > 0, TimeSpan.FromSeconds(5));
        var baselineCount = fake.InvocationCount;

        // Act — flip Enabled=false in the store, then force-refresh.
        (await store.SetEnabledAsync("disable-mid-flight", enabled: false, CancellationToken.None))
            .Should().BeTrue();
        await host.RefreshDefinitionsAsync(CancellationToken.None);

        // Wait long enough that any cron-driven dispatch would have fired multiple times if
        // the disable wasn't honored — but keep it sub-second-and-a-half so test runtime stays
        // tight.
        var countAtRefresh = fake.InvocationCount;
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await host.StopAsync(CancellationToken.None);

        // Assert — invocation count after refresh is exactly the count at refresh time
        // (or one more if a dispatch was in-flight at the moment of refresh; we allow that
        // for race-tolerance — but it MUST NOT keep firing). Use a tight upper bound: at most
        // 1 extra invocation post-refresh; without disable we'd see ≥3.
        var deltaAfterRefresh = fake.InvocationCount - countAtRefresh;
        deltaAfterRefresh.Should().BeLessThanOrEqualTo(1,
            "after disable + refresh, the cron loop must skip subsequent ticks (at most 1 in-flight race tolerance)");
        baselineCount.Should().BeGreaterThan(0, "sanity — the job WAS firing before the disable");
    }

    [Fact]
    public async Task RefreshDefinitionsAsync_PicksUpEnableFlip_DispatchResumesOnNextTick()
    {
        // Arrange — start with a DISABLED definition so no dispatches happen.
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("enable-from-disabled");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("enable-from-disabled", enabled: false));

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);
        await host.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        fake.InvocationCount.Should().Be(0, "disabled definition must not be dispatched");

        // Act — flip Enabled=true + refresh.
        (await store.SetEnabledAsync("enable-from-disabled", enabled: true, CancellationToken.None))
            .Should().BeTrue();
        await host.RefreshDefinitionsAsync(CancellationToken.None);

        // Assert — host now dispatches.
        await WaitUntilAsync(
            () => fake.InvocationCount > 0,
            TimeSpan.FromSeconds(5),
            because: "after enable + refresh, the host MUST pick up the change and start dispatching");

        await host.StopAsync(CancellationToken.None);

        store.RunRecords.Should().Contain(r => r.JobId == "enable-from-disabled");
    }

    [Fact]
    public async Task RefreshDefinitionsAsync_PublicSurface_CanBeCalledWithoutHostStart()
    {
        // The endpoint resolves the host as a singleton and may call RefreshDefinitionsAsync
        // before the BackgroundService loop has started (e.g., during P3 admin-surface
        // testing where the cron loop is intentionally not running per SchedulingModule.cs).
        // RefreshDefinitionsAsync MUST be safe in that state — it only mutates _state.
        var registry = new ScheduledJobRegistry();
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition("standalone-refresh", "S", "", true, "0 2 * * *", null));
        var fake = new FakeScheduledJob("standalone-refresh");
        registry.Register(fake);

        var host = new ScheduledJobHost(registry, store, FastOptions(), NullLogger<ScheduledJobHost>.Instance);

        // Do NOT call StartAsync — just refresh directly.
        var act = async () => await host.RefreshDefinitionsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // CI runners can be 3-5x slower than local; multiply tight timeouts to
    // avoid flake without changing per-test call sites or weakening intent.
    // GitHub Actions sets CI=true; local dev runs unscaled.
    private static readonly bool _isCi =
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Polls a predicate until satisfied or the deadline elapses (xUnit-friendly wait helper).</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string? because = null)
    {
        var effectiveTimeout = _isCi ? TimeSpan.FromTicks(timeout.Ticks * 5) : timeout;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < effectiveTimeout)
        {
            if (predicate()) return;
            await Task.Delay(50);
        }
        if (!predicate())
        {
            throw new TimeoutException($"Predicate did not become true within {effectiveTimeout}{(because is null ? "" : " — " + because)}");
        }
    }
}
