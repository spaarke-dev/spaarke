using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Spaarke.Scheduling.Tests;

/// <summary>
/// ADR-036 A1 rules 1 and 2 (unified-access-control-r2 task 103): one dispatch per schedule across instances; a
/// manual trigger and a scheduled tick never overlap; the lease spans every retry and expires when its holder stops
/// renewing; a lease store that is not configured, or configured but down; and the deployment-slot guard.
/// </summary>
/// <remarks>
/// Every test drives a <see cref="FakeTimeProvider"/>: the host routes each sleep, retry delay and lease renewal
/// through it, so no assertion depends on how fast the runner is. Two hosts sharing one
/// <see cref="FakeDistributedLease"/> stand in for two App Service instances sharing Redis.
/// </remarks>
public class ScheduledJobLeaseTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-14T12:00:00Z");
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private const string EveryTenSeconds = "*/10 * * * * *";
    private const string EveryMinute = "0 * * * * *";

    [Fact]
    public async Task TwoHostsSharingALease_SameOccurrence_DispatchOnceAndTheOtherRecordsSkipped()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var jobA = new FakeScheduledJob("shared-job");
        var jobB = new FakeScheduledJob("shared-job");
        var logA = new ListLogger<ScheduledJobHost>();
        var logB = new ListLogger<ScheduledJobHost>();
        var (hostA, storeA) = NewHost(jobA, time, lease, logA);
        var (hostB, storeB) = NewHost(jobB, time, lease, logB);
        var occurrence = Start.AddSeconds(10);

        await hostA.StartAsync(CancellationToken.None);
        await hostB.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(
            time,
            () => Settled(storeA, storeB).Count(r => r.ScheduledFireUtc == occurrence) >= 2,
            "both hosts MUST settle the 12:00:10 occurrence — one run, one skip");
        await hostA.StopAsync(CancellationToken.None);
        await hostB.StopAsync(CancellationToken.None);

        var settled = Settled(storeA, storeB).Where(r => r.ScheduledFireUtc == occurrence).ToList();
        settled.Should().HaveCount(2);
        settled.Count(r => r.Result is { Skipped: false, Success: true }).Should().Be(1, "exactly one host runs the occurrence");
        settled.Count(r => r.Result is { Skipped: true }).Should().Be(1, "the other records it as skipped, not failed");
        (jobA.InvocationCount + jobB.InvocationCount).Should().Be(1);
        logA.Count(LogLevel.Information, "skipped").Should().Be(logB.Count(LogLevel.Information, "skipped") == 1 ? 0 : 1,
            "the host that did not run the occurrence MUST say so");
        (logA.Count(LogLevel.Warning, "no distributed lease store") + logB.Count(LogLevel.Warning, "no distributed lease store"))
            .Should().Be(0, "a distributed lease never triggers the single-instance warning");
    }

    [Fact]
    public async Task OccurrenceAnotherInstanceAlreadyDispatched_IsSkippedEvenThoughTheLeaseIsFree()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        // Another instance ran 12:00:10 quickly and has already released the lease.
        lease.MarkDispatched("late-job", Start.AddSeconds(10));
        var job = new FakeScheduledJob("late-job");
        var (host, store) = NewHost(job, time, lease);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.CompletedAtUtc is not null), "the 12:00:10 tick settles");
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(0, "a free lease is not enough — this occurrence was already dispatched");
        var tick = store.RunRecords.Single();
        tick.ScheduledFireUtc.Should().Be(Start.AddSeconds(10));
        tick.Result!.Skipped.Should().BeTrue();
        tick.Result.ResultJson.Should().Contain("occurrence-already-dispatched");
    }

    [Fact]
    public async Task ProcessLocalLease_OneHolderAtATime_OncePerOccurrence_ReleasedOnlyByItsHolder()
    {
        var lease = new ProcessLocalScheduledJobLease();
        var occurrence = Start.AddSeconds(10);

        var first = await lease.TryAcquireAsync("job", occurrence, LeaseDuration, CancellationToken.None);
        var whileHeld = await lease.TryAcquireAsync("job", occurrenceUtc: null, LeaseDuration, CancellationToken.None);
        await lease.ReleaseAsync("job", first.Token!, CancellationToken.None);
        var sameOccurrenceAgain = await lease.TryAcquireAsync("job", occurrence, LeaseDuration, CancellationToken.None);
        var nextOccurrence = await lease.TryAcquireAsync("job", occurrence.AddSeconds(10), LeaseDuration, CancellationToken.None);
        await lease.ReleaseAsync("job", "not-the-holder", CancellationToken.None);
        var afterStrangerRelease = await lease.TryAcquireAsync("job", occurrenceUtc: null, LeaseDuration, CancellationToken.None);

        first.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        whileHeld.Status.Should().Be(ScheduledJobLeaseStatus.HeldElsewhere);
        sameOccurrenceAgain.Status.Should().Be(ScheduledJobLeaseStatus.OccurrenceAlreadyDispatched);
        nextOccurrence.Status.Should().Be(ScheduledJobLeaseStatus.Granted);
        afterStrangerRelease.Status.Should().Be(ScheduledJobLeaseStatus.HeldElsewhere,
            "a release by anyone but the holder frees nothing");
        lease.IsDistributed.Should().BeFalse();
    }

    [Fact]
    public async Task LeaseSpansEveryRetryAndBackoff_OtherHostsSkipUntilTheRunEnds()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var jobA = new FakeScheduledJob("retry-job", async (_, ct) =>
        {
            if (Interlocked.Increment(ref attempts) < 3)
            {
                throw new InvalidOperationException("transient");
            }

            await release.Task.WaitAsync(ct);
            return new JobRunResult(true, null, 1, TimeSpan.Zero);
        });
        var jobB = new FakeScheduledJob("retry-job");
        var (hostA, storeA) = NewHost(jobA, time, lease);
        var (hostB, storeB) = NewHost(jobB, time, lease);

        await hostA.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => Volatile.Read(ref attempts) >= 3,
            "attempts 1 and 2 fail, and attempt 3 starts after the 5s + 10s backoff");
        await hostB.StartAsync(CancellationToken.None);
        await AdvanceForAsync(time, LeaseDuration * 3);

        lease.HolderOf("retry-job").Should().NotBeNull("the run still holds the lease three lease-durations after it began");
        lease.Renewals.Should().BeGreaterThan(0, "only renewal keeps a lease alive past its duration");
        lease.RequestedDurations.Should().OnlyContain(d => d == LeaseDuration, "the lease is always taken for a finite duration");
        jobB.InvocationCount.Should().Be(0, "no other host runs the job while the first run is in flight");
        storeB.RunRecords.Should().Contain(r => r.Result != null && r.Result.Skipped);

        release.SetResult();
        await AdvanceUntilAsync(time, () => lease.HolderOf("retry-job") is null
                && storeA.RunRecords.Any(r => r.Result is { Success: true, Skipped: false }),
            "the run completes and releases its lease");
        await AdvanceUntilAsync(time, () => jobB.InvocationCount + jobA.InvocationCount > 3,
            "once released, the next occurrence runs");
        await hostA.StopAsync(CancellationToken.None);
        await hostB.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HolderThatCannotRenew_IsCancelledWhenItsLeaseExpires_AndALaterTickRunsElsewhere()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        // The first run blocks until cancelled; any later run — on either host — completes at once. (Either host may
        // win a later occurrence, so the assertions are on the lease's grants, not on which host ran.)
        var blocked = 0;
        Func<JobRunContext, CancellationToken, Task<JobRunResult>> body = async (_, ct) =>
        {
            if (Interlocked.Exchange(ref blocked, 1) == 0)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return new JobRunResult(true, null, 0, TimeSpan.Zero);
        };
        var jobA = new FakeScheduledJob("stuck-job", body);
        var jobB = new FakeScheduledJob("stuck-job", body);
        var (hostA, storeA) = NewHost(jobA, time, lease);
        var (hostB, _) = NewHost(jobB, time, lease);

        await hostA.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => jobA.InvocationCount == 1, "host A takes the lease and runs");
        // Host A loses Redis — its renewals never land, while acquires still reach the store.
        lease.RefuseRenewals = true;
        await hostB.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => jobA.InvocationCount + jobB.InvocationCount >= 2
                && storeA.RunRecords.Any(r => r.Result is { Skipped: false, Success: false }),
            "host A's run is cancelled once its lease has expired, and the job runs again");
        await hostA.StopAsync(CancellationToken.None);
        await hostB.StopAsync(CancellationToken.None);

        var grants = lease.Grants.Where(g => g.JobId == "stuck-job").Select(g => g.At).ToList();
        grants[1].Should().BeOnOrAfter(grants[0] + LeaseDuration,
            "the job ran again only once the first lease had expired — never while it was live");
        storeA.RunRecords.First(r => r.Result is { Skipped: false, Success: false }).Result!.ErrorMessage
            .Should().Contain("could not be renewed", "a holder that cannot renew stops rather than overlap");
    }

    [Fact]
    public async Task LeaseLapsedMidRun_WithNoOtherHolder_IsRetakenAndTheRunCompletes()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var log = new ListLogger<ScheduledJobHost>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new FakeScheduledJob("lapse-job", async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return new JobRunResult(true, null, 1, TimeSpan.Zero);
        });
        var (host, store) = NewHost(job, time, lease, log);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => job.InvocationCount == 1, "the run starts under the lease");
        lease.Expire("lapse-job");
        await AdvanceUntilAsync(time, () => lease.Grants.Count(g => g.JobId == "lapse-job") == 2,
            "the next renewal is refused and the free lease is re-taken");
        release.SetResult();
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.Result is { Skipped: false }), "the run completes");
        await host.StopAsync(CancellationToken.None);

        store.RunRecords.Single(r => r.Result is { Skipped: false }).Result!.Success.Should().BeTrue();
        log.Count(LogLevel.Warning, "was re-taken").Should().Be(1);
    }

    [Fact]
    public async Task HungRunPastMaxRunDuration_StopsRenewing_SoTheJobRunsAgainWhileItIsStillHung()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var logA = new ListLogger<ScheduledJobHost>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The first run hangs — it ignores its cancellation token. Any later run, on either host, completes at once.
        var hung = 0;
        Func<JobRunContext, CancellationToken, Task<JobRunResult>> body = async (_, _) =>
        {
            if (Interlocked.Exchange(ref hung, 1) == 0)
            {
                await release.Task;
            }

            return new JobRunResult(true, null, 0, TimeSpan.Zero);
        };
        var jobA = new FakeScheduledJob("hung-job", body);
        var jobB = new FakeScheduledJob("hung-job", body);
        var options = Options();
        options.MaxRunDuration = TimeSpan.FromMinutes(1);
        var (hostA, _) = NewHost(jobA, time, lease, logA, options);
        var (hostB, _) = NewHost(jobB, time, lease, options: options);

        await hostA.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => jobA.InvocationCount == 1, "host A's run starts, and hangs");
        await hostB.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => jobA.InvocationCount + jobB.InvocationCount >= 2,
            "past MaxRunDuration host A stops renewing, the lease expires, and the job runs again", maxSteps: 1000);
        var stillHung = !release.Task.IsCompleted;
        release.SetResult();
        await hostA.StopAsync(CancellationToken.None);
        await hostB.StopAsync(CancellationToken.None);

        stillHung.Should().BeTrue("the job ran again while the first run was still hung");
        var grants = lease.Grants.Where(g => g.JobId == "hung-job").Select(g => g.At).ToList();
        grants[1].Should().BeOnOrAfter(grants[0] + options.MaxRunDuration,
            "the hung run's lease was renewed until MaxRunDuration and never taken while live");
        logA.Count(LogLevel.Error, "longer than MaxRunDuration").Should().Be(1);
    }

    [Fact]
    public async Task TriggerNowAsync_RetriedRun_EachAttemptSeesItsAttemptNumber()
    {
        // ADR-036 A1 rule 5: a job's heartbeat carries its attempt number, so the host must pass it on each retry.
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var attempts = new List<int>();
        var job = new FakeScheduledJob("retry-job", (ctx, _) =>
        {
            lock (attempts)
            {
                attempts.Add(ctx.Attempt);
            }

            return ctx.Attempt == 1
                ? throw new InvalidOperationException("transient")
                : Task.FromResult(new JobRunResult(true, null, 1, TimeSpan.Zero));
        });
        var (host, store) = NewHost(job, time, lease);

        var run = await host.TriggerNowAsync("retry-job", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.RunId == run.RunId && r.Result is not null),
            "the second attempt succeeds after the retry backoff");

        attempts.Should().Equal(1, 2);
        store.RunRecords.Single(r => r.RunId == run.RunId).Result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ManualRun_HostShutdown_CancelsItAndReleasesTheLease()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var job = new FakeScheduledJob("manual-long", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new JobRunResult(true, null, 0, TimeSpan.Zero);
        });
        var (host, store) = NewHost(job, time, lease);

        await host.StartAsync(CancellationToken.None);
        var manual = await host.TriggerNowAsync("manual-long", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => job.InvocationCount == 1, "the manual run starts");
        await host.StopAsync(CancellationToken.None);

        var run = store.RunRecords.Single(r => r.RunId == manual.RunId);
        run.Result.Should().NotBeNull("the drain waits for the cancelled run to record its completion");
        run.Result!.ErrorMessage.Should().Contain("host shutdown");
        lease.HolderOf("manual-long").Should().BeNull("a run that stops on shutdown gives its lease back");
    }

    [Fact]
    public async Task LeaseTakenByAnotherHolder_DuringTheRun_CancelsTheRun()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var job = new FakeScheduledJob("stolen-job", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new JobRunResult(true, null, 0, TimeSpan.Zero);
        });
        var (host, store) = NewHost(job, time, lease);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => job.InvocationCount == 1, "the run starts under the lease");
        lease.Steal("stolen-job");
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.Result is { Skipped: false }),
            "the next renewal finds the lease taken and cancels the run");
        await host.StopAsync(CancellationToken.None);

        var run = store.RunRecords.Single(r => r.Result is { Skipped: false });
        run.Result!.Success.Should().BeFalse();
        run.Result.ErrorMessage.Should().Contain("another holder took the scheduler lease");
        lease.HolderOf("stolen-job").Should().Be("thief", "a run that lost its lease never releases the new holder's");
    }

    [Fact]
    public async Task ManualTrigger_WhileAScheduledRunIsInFlight_IsRefusedAsBusy()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new FakeScheduledJob("busy-job", async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return new JobRunResult(true, null, 1, TimeSpan.Zero);
        });
        var (host, store) = NewHost(job, time, lease);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => job.InvocationCount == 1, "the scheduled run starts");

        var overlapping = async () => await host.TriggerNowAsync("busy-job", parameters: null, CancellationToken.None);
        (await overlapping.Should().ThrowAsync<ScheduledJobBusyException>()).Which.JobId.Should().Be("busy-job");
        job.InvocationCount.Should().Be(1);

        release.SetResult();
        await AdvanceUntilAsync(time, () => lease.HolderOf("busy-job") is null, "the scheduled run ends");
        var manual = await host.TriggerNowAsync("busy-job", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.RunId == manual.RunId && r.CompletedAtUtc is not null),
            "once the job is free, a manual trigger runs");
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task ScheduledTick_WhileAManualRunIsInFlight_IsSkipped()
    {
        var time = new FakeTimeProvider(Start);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new FakeScheduledJob("manual-first", async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return new JobRunResult(true, null, 1, TimeSpan.Zero);
        });
        // No lease passed: the host's process-local default keeps the same promise within one process.
        var (host, store) = NewHost(job, time, lease: null);

        await host.StartAsync(CancellationToken.None);
        await host.TriggerNowAsync("manual-first", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.Result is { Skipped: true }),
            "the 12:00:10 tick finds the manual run holding the lease");
        release.SetResult();
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(1, "the scheduled tick never ran alongside the manual run");
        store.RunRecords.Single(r => r.Result is { Skipped: true }).ScheduledFireUtc.Should().Be(Start.AddSeconds(10));
    }

    [Fact]
    public async Task NoDistributedLeaseStore_DispatchesAndWarnsOnce()
    {
        var time = new FakeTimeProvider(Start);
        var log = new ListLogger<ScheduledJobHost>();
        var job = new FakeScheduledJob("dev-job");
        var (host, store) = NewHost(job, time, lease: null, log);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => job.InvocationCount >= 2, "a single-instance host still dispatches every tick");
        await AdvanceUntilAsync(time, () => store.RunRecords.Count(r => r.CompletedAtUtc is not null) >= 2,
            "both runs complete");
        var manual = await host.TriggerNowAsync("dev-job", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.RunId == manual.RunId && r.CompletedAtUtc is not null),
            "the manual trigger completes");
        await host.StopAsync(CancellationToken.None);

        log.Count(LogLevel.Warning, "no distributed lease store is configured").Should().Be(1,
            "the host warns once — not on every dispatch, scheduled or manual");
    }

    [Fact]
    public async Task SlotGuard_RunsNoScheduledTick_SaysSoAtStartup_AndStillRunsAManualTrigger()
    {
        var time = new FakeTimeProvider(Start);
        var log = new ListLogger<ScheduledJobHost>();
        var job = new FakeScheduledJob("guarded-job");
        var (host, store) = NewHost(job, time, new FakeDistributedLease(time), log, Options(runScheduledJobs: false));

        await host.StartAsync(CancellationToken.None);
        await AdvanceForAsync(time, TimeSpan.FromSeconds(35));

        job.InvocationCount.Should().Be(0, "three occurrences passed and none ran on the guarded host");
        store.RunRecords.Should().BeEmpty();
        log.Count(LogLevel.Warning, "scheduled dispatch is OFF on this host").Should().Be(1);

        var manual = await host.TriggerNowAsync("guarded-job", parameters: null, CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.RunId == manual.RunId && r.CompletedAtUtc is not null),
            "a manual admin trigger still runs on a guarded host");
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task LeaseStoreUnavailable_TickIsNotDispatched_AndIsRecordedFailedAfterTheRetries()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time) { Unavailable = true };
        var log = new ListLogger<ScheduledJobHost>();
        var job = new FakeScheduledJob("down-job");
        var (host, store) = NewHost(job, time, lease, log, cron: EveryMinute);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.CompletedAtUtc is not null),
            "the 12:01:00 tick settles once the acquire retries are spent", maxSteps: 1000);
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(0, "owner decision 2026-09-14: never dispatch without the lease");
        var tick = store.RunRecords.Single();
        tick.ScheduledFireUtc.Should().Be(Start.AddMinutes(1));
        tick.Result!.Skipped.Should().BeFalse("nobody ran it — a fleet-wide miss is a failure, not a skip");
        tick.Result.Success.Should().BeFalse();
        tick.Result.ErrorMessage.Should().Contain("lease store unavailable");
        lease.AcquireAttempts.Should().Be(new JobRetryPolicy().MaxAttempts, "the acquire is retried with the job backoff first");
        log.Count(LogLevel.Error, "NOT dispatched").Should().Be(1);
    }

    [Fact]
    public async Task LeaseStoreBackWithinTheRetries_TickIsDispatchedOnce()
    {
        var time = new FakeTimeProvider(Start);
        var lease = new FakeDistributedLease(time);
        lease.FailNextAcquires(1);
        var job = new FakeScheduledJob("blip-job");
        var (host, store) = NewHost(job, time, lease, cron: EveryMinute);

        await host.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(time, () => store.RunRecords.Any(r => r.CompletedAtUtc is not null),
            "the second acquire attempt, 5s later, reaches the store", maxSteps: 1000);
        await host.StopAsync(CancellationToken.None);

        job.InvocationCount.Should().Be(1);
        store.RunRecords.Single().Result!.Success.Should().BeTrue();
        lease.AcquireAttempts.Should().Be(2);
    }

    private static ScheduledJobHostOptions Options(bool runScheduledJobs = true) => new()
    {
        // Refresh only at start — a refresh landing on a due instant pushes the job to its next occurrence
        // (see ScheduledJobHostTests.VirtualClockOptions).
        RefreshInterval = TimeSpan.FromHours(1),
        ShutdownDrainTimeout = TimeSpan.FromSeconds(2),
        MaxLoopSleep = Step,
        LeaseDuration = LeaseDuration,
        RunScheduledJobs = runScheduledJobs,
    };

    private static (ScheduledJobHost Host, InMemoryBackgroundJobStore Store) NewHost(
        IScheduledJob job,
        FakeTimeProvider time,
        IScheduledJobLease? lease,
        ILogger<ScheduledJobHost>? logger = null,
        ScheduledJobHostOptions? options = null,
        string cron = EveryTenSeconds)
    {
        var registrations = new[] { new ScheduledJobRegistration(job, cron, Enabled: true) };
        var store = new InMemoryBackgroundJobStore(registrations);
        var host = new ScheduledJobHost(
            new ScheduledJobRegistry(registrations),
            store,
            options ?? Options(),
            logger ?? NullLogger<ScheduledJobHost>.Instance,
            time,
            lease);
        return (host, store);
    }

    private static IEnumerable<InMemoryBackgroundJobStore.RunRecord> Settled(params InMemoryBackgroundJobStore[] stores) =>
        stores.SelectMany(s => s.RunRecords).Where(r => r.CompletedAtUtc is not null);

    /// <summary>Advances virtual time in fixed steps until the predicate holds (see ScheduledJobHostTests).</summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> predicate, string because, int maxSteps = 600)
    {
        for (var step = 0; step < maxSteps; step++)
        {
            if (predicate()) return;
            time.Advance(Step);
            await Task.Delay(1).ConfigureAwait(false);
        }

        if (!predicate())
        {
            throw new TimeoutException($"Predicate did not hold within {maxSteps} virtual steps of {Step} — {because}");
        }
    }

    /// <summary>Advances virtual time step by step, so the host observes every step.</summary>
    private static async Task AdvanceForAsync(FakeTimeProvider time, TimeSpan total)
    {
        for (var i = 0; i < total.Ticks / Step.Ticks; i++)
        {
            time.Advance(Step);
            await Task.Delay(1).ConfigureAwait(false);
        }
    }
}
