using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.Scheduling.Tests;

public class ScheduledJobHostTests
{
    // Test convention: jobs scheduled with the 6-field cron "* * * * * *" (every second) fire
    // rapidly so tests stay sub-second. The host transparently supports both 5- and 6-field
    // cron via ScheduledJobHost.ParseCron — see also CronosParsingTests + the ParseCron test.

    private static ScheduledJobHostOptions FastOptions(
        TimeSpan? drainTimeout = null,
        TimeSpan? refreshInterval = null) => new()
    {
        RefreshInterval = refreshInterval ?? TimeSpan.FromMilliseconds(200),
        ShutdownDrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(2),
        MaxLoopSleep = TimeSpan.FromMilliseconds(200)
    };

    /// <summary>
    /// Options for virtual-clock tests: tick often, refresh rarely.
    /// </summary>
    /// <remarks>
    /// Historical: before #1575 a refresh recomputed NextFireUtc from <c>now</c> EXCLUSIVE, so a
    /// 200ms refresh stepped by an exactly periodic 200ms virtual clock landed on every due instant
    /// and starved the job. A refresh now carries an unchanged job's next fire over (pinned by
    /// <c>RefreshLoop_IntervalDividingTheCronPeriod_DoesNotStarveDispatch_1575</c>); the long
    /// interval is kept so these tests exercise dispatch without refresh noise.
    /// </remarks>
    private static ScheduledJobHostOptions VirtualClockOptions(TimeSpan? drainTimeout = null) =>
        FastOptions(drainTimeout, refreshInterval: TimeSpan.FromSeconds(30));

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

    // Un-skipped 2026-08-28: the TimeProvider refactor the old skip reason waited for shipped in
    // PR #884. Driving the clock removes the cron-tick race entirely.
    [Fact]
    public async Task Dispatch_DueJob_RunsHandlerAndRecordsRun()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("due-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("due-job"));

        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => fake.InvocationCount > 0,
            "a due job MUST dispatch once virtual time reaches its cron occurrence");
        // Wait for the completion RECORD, not for a fixed duration — the previous `Task.Delay(200)`
        // was a guess at how long the write takes, which is the same class of bet as the cron wait.
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.JobId == "due-job" && r.Result is not null),
            "the run-complete record MUST be written after the handler returns");
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

    // Un-skipped 2026-08-28 (PR #884 TimeProvider refactor). The old skip reason cited the
    // 6164472a3 / 8128d32cc precedent of bulk-removing timing assertions to stop CI whack-a-mole;
    // driving the clock is the fix that precedent was substituting for.
    [Fact]
    public async Task RunContext_CarriesFreshCorrelationIdPerRun_NFR08()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("corr-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("corr-job"));

        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => fake.InvocationCount >= 2,
            "two cron occurrences MUST produce two dispatches (NFR-08 needs two runs to compare)");
        await AdvanceUntilAsync(time, () => store.RunRecords.Count(r => r.JobId == "corr-job") >= 2,
            "both dispatches MUST persist run records");
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

    // Un-skipped 2026-08-28 (PR #884 TimeProvider refactor).
    [Fact]
    public async Task RefreshTick_PicksUpDefinitionAddedAtRuntime()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("late-add");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        // No definitions seeded yet.

        // This test cannot use VirtualClockOptions' long refresh interval — the behaviour under
        // test IS the refresh tick noticing a runtime addition, so refresh has to fire during the
        // test. 700ms (not a divisor of the 1s cron period) dates from before #1575, when a refresh
        // landing on a due instant pushed the job to its next occurrence; since #1575 an unchanged
        // job keeps its next fire across a refresh, so the choice is no longer load-bearing.
        var (host, time) = HostWithVirtualClock(
            registry, store, FastOptions(refreshInterval: TimeSpan.FromMilliseconds(700)));

        await host.StartAsync(CancellationToken.None);
        // Let several refresh ticks elapse against empty state, in virtual time.
        for (var i = 0; i < 5; i++) { time.Advance(VirtualStep); await Task.Delay(1); }
        fake.InvocationCount.Should().Be(0, "no definitions yet => no dispatch");

        // Add the definition at runtime — a later refresh tick must pick it up + schedule it.
        store.AddOrReplaceJob(EverySecond("late-add"));

        await AdvanceUntilAsync(time, () => fake.InvocationCount > 0,
            "the refresh tick MUST pick up a definition added after the host started");
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

        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions(TimeSpan.FromSeconds(3)));

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => startedTcs.Task.IsCompleted,
            "the every-second cron MUST dispatch 'slow' once virtual time reaches its next occurrence");

        await host.StopAsync(CancellationToken.None);

        observed.Should().BeTrue("the in-flight job MUST observe cancellation (NFR-07)");

        // Task 091 (#848): the old `stopSw.Elapsed < (CI ? 25s : 5s)` ceiling is deliberately gone.
        // It measured the runner, not the host — and it could only ever fail for the wrong reason,
        // because a drain that did NOT cancel promptly is already caught by the run-record
        // assertions below: base.StopAsync cancels the stopping token, so a job that observed it
        // records Success=false + "Cancelled", while one that sat out the 3s ShutdownDrainTimeout
        // would not. The behaviour is the assertion; elapsed wall-clock never was.
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

        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => good.InvocationCount > 0,
            "a valid cron MUST still dispatch even though a sibling definition has an unparseable expression");
        await host.StopAsync(CancellationToken.None);

        good.InvocationCount.Should().BeGreaterThan(0);
        bad.InvocationCount.Should().Be(0, "an unparseable cron MUST be skipped, not dispatched");
    }

    // Un-skipped 2026-08-28 (PR #884 TimeProvider refactor).
    [Fact]
    public async Task ConfigJson_FlowedToJobRunContextParameters()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("cfg-job");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(new BackgroundJobDefinition(
            "cfg-job", "Cfg", "", true, "* * * * * *", ConfigJson: "{\"foo\":42}"));

        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => fake.InvocationCount > 0,
            "the job MUST dispatch so its context can be inspected");
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

        // Act — fire twice, the second after the first completes: two runs of one job never overlap
        // (ADR-036 A1 rule 1 — an overlapping trigger is refused; see ScheduledJobLeaseTests).
        var first = await host.TriggerNowAsync("nfr08-job", parameters: null, CancellationToken.None);
        await WaitUntilAsync(
            () => store.RunRecords.Any(r => r.RunId == first.RunId && r.CompletedAtUtc is not null),
            TimeSpan.FromSeconds(5));
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

    [Fact]
    public async Task RefreshDefinitionsAsync_PicksUpDisableFlip_DispatchStopsOnNextTick()
    {
        // Arrange — fast-tick host with one enabled "every second" job.
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("disable-mid-flight");
        registry.Register(fake);

        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("disable-mid-flight"));

        // Un-skipped 2026-08-30. The skip said "needs TimeProvider refactor (PR #415)" — but the
        // refactor already existed; this test had simply never adopted it. On the virtual clock the
        // 1500 ms real sleep below becomes advanced time, so the wall-clock race cannot occur.
        //
        // VirtualClockOptions (30 s refresh): this test drives refresh EXPLICITLY, so a long refresh
        // interval keeps the loop's own refresh out of the way (see VirtualClockOptions, #1575).
        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());
        await host.StartAsync(CancellationToken.None);

        // Let it fire at least once so we know it's running normally.
        await AdvanceUntilAsync(time, () => fake.InvocationCount > 0,
            "the job must dispatch normally before the disable is meaningful");
        var baselineCount = fake.InvocationCount;

        // Act — flip Enabled=false in the store, then force-refresh.
        (await store.SetEnabledAsync("disable-mid-flight", enabled: false, CancellationToken.None))
            .Should().BeTrue();
        await host.RefreshDefinitionsAsync(CancellationToken.None);

        // Wait long enough that any cron-driven dispatch would have fired multiple times if
        // the disable wasn't honored — but keep it sub-second-and-a-half so test runtime stays
        // tight.
        // Advance well past several cron periods. Without the disable this window would produce
        // multiple dispatches, so a flat count is a real assertion rather than an absence of time.
        var countAtRefresh = fake.InvocationCount;
        await AdvanceForAsync(time, TimeSpan.FromSeconds(5));
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

        // Un-skipped 2026-08-30 — same conversion as the disable-flip test above.
        var (host, time) = HostWithVirtualClock(registry, store, VirtualClockOptions());
        await host.StartAsync(CancellationToken.None);

        // Advance past several cron periods: a disabled definition must produce nothing even when
        // plenty of time passes. The old real-time version slept 500 ms — less than ONE cron
        // period — so it proved almost nothing.
        await AdvanceForAsync(time, TimeSpan.FromSeconds(3));
        fake.InvocationCount.Should().Be(0, "disabled definition must not be dispatched");

        // Act — flip Enabled=true + refresh.
        (await store.SetEnabledAsync("enable-from-disabled", enabled: true, CancellationToken.None))
            .Should().BeTrue();
        await host.RefreshDefinitionsAsync(CancellationToken.None);

        // Assert — host now dispatches.
        await AdvanceUntilAsync(time, () => fake.InvocationCount > 0,
            "after enable + refresh, the host MUST pick up the change and start dispatching");

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

    // ================================================================================
    // ===== #1575: a definitions refresh must not drop a due tick ====================
    // ================================================================================
    //
    // RefreshDefinitionsAsync used to recompute every job's NextFireUtc from `now` EXCLUSIVE, so a refresh
    // landing on a due instant (the hourly refresh, which the loop runs before its due-check, or an admin
    // enable/disable) — or between a due instant and its dispatch — dropped that occurrence. Live on dev this
    // turned a 2-minute reconcile bound into ~4 minutes about once an hour. These tests drive the loop one
    // iteration at a time on a virtual clock (StepDriver) with the 5-field cron the live jobs use.

    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-10-09T12:00:00Z");

    private const string EveryTwoMinutes = "*/2 * * * *";

    private static BackgroundJobDefinition EveryTwoMinutesJob(string jobId, bool enabled = true, string? configJson = null) =>
        new(jobId, $"Test {jobId}", $"Test job {jobId}", enabled, EveryTwoMinutes, configJson);

    private static ScheduledJobHostOptions StepOptions(TimeSpan refreshInterval) => new()
    {
        RefreshInterval = refreshInterval,
        ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        MaxLoopSleep = TimeSpan.FromMinutes(1),
    };

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 10, 9, hour, minute, second, TimeSpan.Zero);

    private static List<DateTimeOffset?> ScheduledFires(InMemoryBackgroundJobStore store, string jobId) =>
        store.RunRecords.Where(r => r.JobId == jobId).Select(r => r.ScheduledFireUtc).OrderBy(f => f).ToList();

    [Fact]
    public async Task HourlyRefresh_LandingOnDueInstant_DoesNotDropThatTick_1575()
    {
        // The refresh interval equals the cron period, so the loop's own refresh (which runs BEFORE the
        // due-check) lands exactly on the 12:02 occurrence. Before #1575 that refresh recomputed the next
        // fire from 12:02 exclusive (12:04) and the 12:02 tick never ran.
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("refresh-on-due");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("refresh-on-due"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromMinutes(2)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None); // 12:00 — next fire 12:02

        driver.Time.SetUtcNow(At(12, 2));
        driver.Tick(); // refresh is due (2 min since 12:00) and runs first, then the due-check
        await driver.DrainAsync();

        ScheduledFires(store, "refresh-on-due").Should().Equal(
            new DateTimeOffset?[] { At(12, 2) },
            "a refresh at the due instant MUST NOT push the job past the occurrence it was about to fire");
        fake.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshLoop_IntervalDividingTheCronPeriod_DoesNotStarveDispatch_1575()
    {
        // The resonance the older tests in this file had to configure around: a 200 ms refresh stepped by an
        // exactly periodic 200 ms virtual clock lands on every whole second, i.e. on every due instant of the
        // every-second cron. Before #1575 the job never dispatched; now every occurrence still fires.
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("resonant-refresh");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EverySecond("resonant-refresh"));

        var (host, time) = HostWithVirtualClock(registry, store, FastOptions()); // 200 ms refresh

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => fake.InvocationCount >= 3,
            "a refresh interval that divides the cron period MUST NOT starve dispatch");
        await host.StopAsync(CancellationToken.None);

        fake.InvocationCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task AdminRefresh_WhileATickIsDueButUnfired_TickSurvivesAndFires_1575()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("due-unfired");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("due-unfired"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromHours(1)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None); // next fire 12:02

        // 12:02 is due but the loop has not woken yet; an admin enable/disable on ANOTHER job refreshes now.
        driver.Time.SetUtcNow(At(12, 2, 30));
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);

        driver.Tick(); // the surviving 12:02 occurrence fires
        await driver.WaitForCompletedRunsAsync("due-unfired", 1);

        driver.Time.SetUtcNow(At(12, 3));
        driver.Tick(); // nothing due: firing at 12:02:30 advanced the job to 12:04

        driver.Time.SetUtcNow(At(12, 4));
        driver.Tick();
        await driver.DrainAsync();

        ScheduledFires(store, "due-unfired").Should().Equal(
            new DateTimeOffset?[] { At(12, 2), At(12, 4) },
            "the overdue occurrence MUST fire once after the refresh, and the schedule then continues normally");
    }

    [Fact]
    public async Task Refresh_WithChangedCron_RecomputesNextFire_1575()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("cron-changed");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("cron-changed"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromHours(1)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None); // next fire 12:02 on the old cron

        // Re-schedule AT the old cron's due instant: the old occurrence must NOT carry over.
        driver.Time.SetUtcNow(At(12, 2));
        store.AddOrReplaceJob(EveryTwoMinutesJob("cron-changed") with { CronSchedule = "*/3 * * * *" });
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);

        driver.Tick(); // 12:02 is not an occurrence of the new cron

        driver.Time.SetUtcNow(At(12, 3));
        driver.Tick();
        await driver.DrainAsync();

        ScheduledFires(store, "cron-changed").Should().Equal(
            new DateTimeOffset?[] { At(12, 3) },
            "a changed cron MUST be recomputed from the refresh instant, not inherit the old schedule's next fire");
    }

    [Fact]
    public async Task Refresh_DisabledThenEnabled_GetsAFreshNextFire_1575()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("re-enabled");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("re-enabled"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromHours(1)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None); // next fire 12:02

        driver.Time.SetUtcNow(At(12, 1));
        (await store.SetEnabledAsync("re-enabled", enabled: false, CancellationToken.None)).Should().BeTrue();
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);

        foreach (var minute in new[] { 2, 4 })
        {
            driver.Time.SetUtcNow(At(12, minute));
            driver.Tick(); // disabled: no fire
        }

        driver.Time.SetUtcNow(At(12, 5));
        (await store.SetEnabledAsync("re-enabled", enabled: true, CancellationToken.None)).Should().BeTrue();
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);
        driver.Tick(); // the pre-disable 12:02 MUST NOT come back as a catch-up fire

        driver.Time.SetUtcNow(At(12, 6));
        driver.Tick();
        await driver.DrainAsync();

        ScheduledFires(store, "re-enabled").Should().Equal(
            new DateTimeOffset?[] { At(12, 6) },
            "a re-enabled job MUST be scheduled fresh from the enable instant");
    }

    [Fact]
    public async Task Refresh_UnchangedJob_KeepsItsNextFire_AndFiresItOnce_1575()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("unchanged");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("unchanged"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromHours(1)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None); // next fire 12:02

        // Refreshes before, and exactly at, the due instant leave the 12:02 occurrence in place.
        foreach (var instant in new[] { At(12, 1), At(12, 1, 59), At(12, 2) })
        {
            driver.Time.SetUtcNow(instant);
            await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);
        }

        driver.Tick();
        await driver.WaitForCompletedRunsAsync("unchanged", 1);

        // A refresh after the dispatch carries the ADVANCED next fire (12:04), never the one just fired.
        driver.Time.SetUtcNow(At(12, 2, 30));
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);
        driver.Tick();
        await driver.DrainAsync();

        ScheduledFires(store, "unchanged").Should().Equal(
            new DateTimeOffset?[] { At(12, 2) },
            "an unchanged job keeps its next fire across refreshes, and a fired occurrence never fires twice");
    }

    [Fact]
    public async Task Refresh_AtDueInstant_NonScheduleChange_KeepsTheTick_AndAppliesTheNewConfig_1575()
    {
        var registry = new ScheduledJobRegistry();
        var fake = new FakeScheduledJob("config-changed");
        registry.Register(fake);
        var store = new InMemoryBackgroundJobStore();
        store.AddOrReplaceJob(EveryTwoMinutesJob("config-changed", configJson: "{\"v\":1}"));

        await using var driver = new StepDriver(registry, store, StepOptions(TimeSpan.FromHours(1)), Noon);
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);

        driver.Time.SetUtcNow(At(12, 2));
        store.AddOrReplaceJob(EveryTwoMinutesJob("config-changed", configJson: "{\"v\":2}"));
        await driver.Host.RefreshDefinitionsAsync(CancellationToken.None);

        driver.Tick();
        await driver.DrainAsync();

        ScheduledFires(store, "config-changed").Should().Equal(
            new DateTimeOffset?[] { At(12, 2) },
            "a change outside the schedule MUST NOT reschedule the job");
        fake.LastContext!.Parameters["configJson"].Should().Be("{\"v\":2}",
            "the refreshed definition still takes effect for the run");
    }

    /// <summary>
    /// Drives <see cref="ScheduledJobHost.TickAsync"/> one iteration at a time on a virtual clock, without the
    /// background loop.
    /// </summary>
    /// <remarks>
    /// The due-check and dispatch run synchronously inside <c>TickAsync</c> (the in-memory store completes
    /// synchronously); only the iteration's trailing sleep waits on the virtual clock, so each tick's task is
    /// left pending and cancelled by <see cref="DrainAsync"/>. The tick token is cancelled only AFTER the host
    /// has drained its in-flight runs, because a scheduled run is linked to it.
    /// </remarks>
    private sealed class StepDriver : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _ticks = new();
        private readonly InMemoryBackgroundJobStore _store;
        private bool _drained;

        public StepDriver(
            ScheduledJobRegistry registry,
            InMemoryBackgroundJobStore store,
            ScheduledJobHostOptions options,
            DateTimeOffset start)
        {
            _store = store;
            Time = new FakeTimeProvider(start);
            Host = new ScheduledJobHost(registry, store, options, NullLogger<ScheduledJobHost>.Instance, Time);
        }

        public ScheduledJobHost Host { get; }

        public FakeTimeProvider Time { get; }

        public void Tick() => _ticks.Add(Host.TickAsync(_cts.Token));

        public Task WaitForCompletedRunsAsync(string jobId, int count) =>
            WaitUntilAsync(
                () => _store.RunRecords.Count(r => r.JobId == jobId && r.CompletedAtUtc is not null) >= count,
                TimeSpan.FromSeconds(10),
                $"{count} completed run(s) of '{jobId}'");

        /// <summary>Waits for every dispatched run to finish, then ends the pending tick sleeps.</summary>
        public async Task DrainAsync()
        {
            if (_drained) return;
            _drained = true;

            await Host.StopAsync(CancellationToken.None);
            _cts.Cancel();
            foreach (var tick in _ticks)
            {
                try { await tick; }
                catch (OperationCanceledException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DrainAsync();
            _cts.Dispose();
            Host.Dispose();
        }
    }

    /// <summary>
    /// Advances the host's VIRTUAL clock in fixed steps until <paramref name="predicate"/> holds.
    /// </summary>
    /// <remarks>
    /// <para>Replaces the old <c>WaitUntilAsync</c> (task 091 / #848), which polled a real
    /// <see cref="Stopwatch"/> against a wall-clock deadline scaled 5x on CI. That helper made
    /// every host test a bet on how fast the runner could schedule a cron tick — the bet lost
    /// often enough that six tests in this file were left permanently <c>[Fact(Skip)]</c> and the
    /// whole assembly had parallelisation disabled.</para>
    /// <para>The bound here is a step COUNT over virtual time, not elapsed real time. A loaded
    /// runner makes each yield slower; it cannot make the loop give up early, because virtual
    /// time only moves when this method moves it. <see cref="ScheduledJobHost"/> routes every
    /// sleep through its injected <see cref="TimeProvider"/>, which is what makes this work.</para>
    /// <para>The <c>Task.Delay(1)</c> is a thread-scheduling handshake letting the host loop's
    /// timer continuation observe the advance — it is not a timing assertion, and no test
    /// asserts on how long it took.</para>
    /// </remarks>
    private static async Task AdvanceUntilAsync(
        FakeTimeProvider time,
        Func<bool> predicate,
        string because,
        int maxSteps = 400)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            if (predicate()) return;
            time.Advance(VirtualStep);
            await Task.Delay(1).ConfigureAwait(false);
        }

        if (!predicate())
        {
            throw new TimeoutException(
                $"Predicate did not become true within {maxSteps} virtual steps of {VirtualStep} — {because}");
        }
    }

    /// <summary>Virtual-clock increment per <see cref="AdvanceUntilAsync"/> step (matches MaxLoopSleep).</summary>
    /// <summary>
    /// Advances virtual time by <paramref name="total"/> in <see cref="VirtualStep"/> increments,
    /// yielding between each so the host loop observes every step.
    ///
    /// <para>Used for "and then nothing happened" assertions. Advancing in one jump would let the
    /// host coalesce the whole span into a single tick, so a flat invocation count would prove
    /// nothing — flat because time never appeared to pass, not because dispatch was correctly
    /// suppressed.</para>
    /// </summary>
    private static async Task AdvanceForAsync(FakeTimeProvider time, TimeSpan total)
    {
        var steps = (int)(total.Ticks / VirtualStep.Ticks);
        for (var i = 0; i < steps; i++)
        {
            time.Advance(VirtualStep);
            await Task.Delay(1).ConfigureAwait(false);
        }
    }

    private static readonly TimeSpan VirtualStep = TimeSpan.FromMilliseconds(200);

    // CI runners can be 3-5x slower than local; scale the bound rather than weakening intent.
    private static readonly bool _isCi =
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Polls a predicate against the REAL clock. Retained (task 091 / #848) only for waits that do
    /// NOT depend on a cron tick — i.e. the <c>TriggerNowAsync</c> tests, which dispatch manually and
    /// simply need the fire-and-track background task to finish. Those never flaked, because nothing
    /// about them is scheduled.
    /// </summary>
    /// <remarks>
    /// Cron-driven tests MUST use <see cref="AdvanceUntilAsync"/> instead — waiting on the wall clock
    /// for a scheduled tick is what made this file flaky and left six tests <c>[Fact(Skip)]</c>.
    /// Note this is a completion wait, not a timing assertion: no caller asserts on elapsed time.
    /// </remarks>
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

    /// <summary>Host wired to a virtual clock — the only shape used by tests that need a tick to fire.</summary>
    private static (ScheduledJobHost Host, FakeTimeProvider Time) HostWithVirtualClock(
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ScheduledJobHostOptions options)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-28T12:00:00Z"));
        var host = new ScheduledJobHost(registry, store, options, NullLogger<ScheduledJobHost>.Instance, time);
        return (host, time);
    }
}
