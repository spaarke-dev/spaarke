using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Api.Office;
using StackExchange.Redis;
using Xunit;
using JobOutcomeStatus = Sprk.Bff.Api.Services.Jobs.JobStatus;
using OfficeJobStatus = Sprk.Bff.Api.Models.Office.JobStatus;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// GitHub #1086 (ISS-018, task 068): Office background work was lost on an app restart, and SSE event numbers were
/// per instance.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Generate Profile was fire-and-forget.</b> The route answered 202, then ran the profile in <c>Task.Run</c>
/// under <c>ApplicationStopping</c>. Nothing was persisted, so a restart lost it.</item>
/// <item><b>The profile job lost its own redelivery.</b> <c>AppOnlyDocumentAnalysisJobHandler</c> holds a 10-minute Redis
/// processing lock and released it with the job's token. A graceful stop cancels that token, and
/// <c>RedisCache.RemoveAsync</c> → <c>ConnectAsync</c> calls <c>ThrowIfCancellationRequested()</c> first (verified
/// against the .NET 10 source), so the lock stayed. A crash leaves it too. The redelivery, five minutes later on
/// <c>sdap-jobs</c>, found the lock and was completed as "already being processed" without running.</item>
/// <item><b>SSE numbers were per instance</b>, and the stream mixed them with its own counter.</item>
/// </list>
/// <para><b>Why <see cref="RedisLikeCache"/> stands in for Redis.</b> It is shared by the "processes" in a test, as
/// Redis is shared by instances and outlives them, and its async members refuse a cancelled token before doing any
/// work, as <c>RedisCache</c>'s do. <c>MemoryDistributedCache</c> does neither.</para>
/// </remarks>
[Trait("status", "new")]
public class Issue1086_OfficeBackgroundWorkRestartTests
{
    private static readonly Guid DocumentId = Guid.Parse("6b1f3c8e-2d4a-4f9b-8e7c-1a2b3c4d5e6f");
    private static readonly string Route = $"/api/office/documents/{DocumentId}/generate-profile";

    // ══ (a) Generate Profile ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateProfile_ARestartAfterThe202_LeavesTheRequestOnTheJobQueue()
    {
        using var host = new ProfileRequestTestWebAppFactory();
        host.GrantWrite(DocumentId);

        var response = await host.AuthorizedClient().PostAsync(Route, content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        // The restart: the process is told to stop, as an App Service restart or a deploy does.
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await host.WaitForBackgroundWorkToSettleAsync();

        var job = host.SubmittedJobs.Should().ContainSingle(
            "the 202 promised a profile, and only a request on the job queue outlives the process "
            + $"(the in-process profile was {(host.InProcessProfileWasCancelled ? "cancelled by the stop" : "not cancelled")})")
            .Subject;
        job.JobType.Should().Be(AppOnlyDocumentAnalysisJobHandler.JobTypeName);
        job.SubjectId.Should().Be(DocumentId.ToString());
        job.IdempotencyKey.Should().Be($"analysis-{DocumentId}-documentprofile-version-{job.JobId:N}",
            "the key carries the request's own id, task 029's discriminator, so a redelivery repeats it and a new request does not");
    }

    [Fact]
    public async Task GenerateProfile_TwiceForADocumentASaveAlreadyProfiled_BothRequestsRun()
    {
        using var host = new ProfileRequestTestWebAppFactory();
        host.GrantWrite(DocumentId);
        var client = host.AuthorizedClient();

        (await client.PostAsync(Route, content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsync(Route, content: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await host.WaitForBackgroundWorkToSettleAsync();

        var jobs = host.SubmittedJobs.ToList();
        jobs.Should().HaveCount(2, "each click is its own request on the queue");
        jobs.Select(j => j.IdempotencyKey).Should().OnlyHaveUniqueItems();

        // The trap the dispatcher was built to avoid: the save already processed the document's bare key.
        var idempotency = new IdempotencyService(new RedisLikeCache(), NullLogger<IdempotencyService>.Instance);
        await idempotency.MarkEventAsProcessedAsync($"analysis-{DocumentId}-documentprofile");
        var analysis = SucceedingAnalysis();

        foreach (var queued in jobs)
        {
            var outcome = await NewHandler(analysis.Object, idempotency).ProcessAsync(queued, CancellationToken.None);
            outcome.Status.Should().Be(JobOutcomeStatus.Completed);
        }

        analysis.Verify(
            a => a.AnalyzeDocumentAsync(DocumentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "neither an earlier profile nor the first click may turn a second click into a skip");
    }

    // ══ (a) The profile job and its lock ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProfileJob_RedeliveredAfterItsProcessCrashed_RunsInsteadOfBeingCompletedAsInProgress()
    {
        var redis = new RedisLikeCache();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-01T12:00:00Z"));
        var job = ProfileRequestJob();

        // The first process takes the message, starts the profile, and dies.
        var crashed = new BlockingAnalysis();
        try
        {
            _ = NewHandler(crashed.Service, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance, clock))
                .ProcessAsync(job, CancellationToken.None);
            await crashed.Entered;

            // Service Bus redelivers the same message to the next process once the dead delivery's message lock expires
            // (sdap-jobs lockDuration PT5M).
            clock.Advance(TimeSpan.FromMinutes(5));
            var redelivered = SucceedingAnalysis();
            var outcome = await NewHandler(redelivered.Object, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance, clock))
                .ProcessAsync(job, CancellationToken.None);

            redelivered.Verify(
                a => a.AnalyzeDocumentAsync(DocumentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "a redelivery is the same message, which Service Bus hands out only after the earlier delivery lost it; "
                + "its own stale lock must not turn it into a completed no-op");
            outcome.Status.Should().Be(JobOutcomeStatus.Completed);
        }
        finally
        {
            crashed.Release();
        }
    }

    /// <summary>Guard (one line, per the POML's scope rule): taking back its own lock must not let a duplicate copy of a
    /// job (a resent message, duplicate detection being off) run beside the copy that is still running.</summary>
    [Fact]
    public async Task ProfileJob_DeliveredTwiceWhileTheFirstCopyRuns_DoesNotRunTwice()
    {
        var redis = new RedisLikeCache();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-01T12:00:00Z"));
        var job = ProfileRequestJob();

        var first = new BlockingAnalysis();
        try
        {
            _ = NewHandler(first.Service, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance, clock))
                .ProcessAsync(job, CancellationToken.None);
            await first.Entered;

            clock.Advance(TimeSpan.FromSeconds(5));
            var duplicate = SucceedingAnalysis();
            await NewHandler(duplicate.Object, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance, clock))
                .ProcessAsync(job, CancellationToken.None);

            duplicate.Verify(
                a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "a lock its own job took seconds ago belongs to a copy that is still running");
        }
        finally
        {
            first.Release();
        }
    }

    [Fact]
    public async Task ProfileJob_StoppedGracefully_ReleasesItsLock_SoTheNextJobOnThatKeyRuns()
    {
        var redis = new RedisLikeCache();
        var key = $"analysis-{DocumentId}-documentprofile";
        using var stopping = new CancellationTokenSource();

        var interrupted = new CancellableAnalysis();
        var running = NewHandler(interrupted.Service, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance))
            .ProcessAsync(ProfileRequestJob(key), stopping.Token);
        await interrupted.Entered;
        stopping.Cancel(); // the graceful stop
        var first = await running;

        first.Status.Should().NotBe(JobOutcomeStatus.Completed, "an interrupted profile is retried, never reported done");

        // Another producer's job on the same bare key (a save and an attachment both queue it).
        var next = SucceedingAnalysis();
        await NewHandler(next.Object, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance))
            .ProcessAsync(ProfileRequestJob(key), CancellationToken.None);

        next.Verify(
            a => a.AnalyzeDocumentAsync(DocumentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a graceful stop must release the processing lock; with a cancelled token the release never reaches Redis "
            + "and the key stays locked for ten minutes");
    }

    /// <summary>Guard (one line, per the POML's scope rule): the restart fix must not let two different jobs run one key at once.</summary>
    [Fact]
    public async Task ProfileJob_WhileADifferentJobOnTheSameKeyIsRunning_DoesNotRunAlongsideIt()
    {
        var redis = new RedisLikeCache();
        var key = $"analysis-{DocumentId}-documentprofile";

        var holder = new BlockingAnalysis();
        try
        {
            _ = NewHandler(holder.Service, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance))
                .ProcessAsync(ProfileRequestJob(key), CancellationToken.None);
            await holder.Entered;

            var other = SucceedingAnalysis();
            await NewHandler(other.Object, new IdempotencyService(redis, NullLogger<IdempotencyService>.Instance))
                .ProcessAsync(ProfileRequestJob(key), CancellationToken.None);

            other.Verify(
                a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            holder.Release();
        }
    }

    // ══ (b) SSE sequence numbers ═══════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SseSequence_TwoInstancesAndARestart_NumberOneJobsEventsWithoutReuse()
    {
        var redis = new SharedRedis();
        var jobId = Guid.NewGuid();
        var a = redis.NewInstance();
        var b = redis.NewInstance();

        await a.PublishStatusUpdateAsync(Update(jobId, 10));
        await b.PublishStatusUpdateAsync(Update(jobId, 20));
        await a.PublishStatusUpdateAsync(Update(jobId, 30));
        await b.PublishStatusUpdateAsync(Update(jobId, 40));
        var restarted = redis.NewInstance(); // instance A after a restart
        await restarted.PublishStatusUpdateAsync(Update(jobId, 50));

        redis.PublishedSequences(jobId).Should().Equal(new long[] { 1, 2, 3, 4, 5 },
            "Last-Event-ID resumes by number, so one job's events need one number line across instances and restarts");
    }

    [Fact]
    public async Task SseStream_ReconnectWithLastEventId_SendsEveryLaterEventOnce_AndNoEarlierOne()
    {
        var jobId = Guid.NewGuid();
        var bus = new FakeJobBus();
        bus.OnSubscribe = () =>
        {
            bus.Publish(Update(jobId, 40, sequence: 4)); // already seen by the client
            bus.Publish(Update(jobId, 60, sequence: 6));
            bus.Publish(Completed(jobId, sequence: 7));
        };
        var stream = NewStream(bus, RunningRow(jobId));

        var frames = (await CollectAsync(stream, jobId, lastEventId: $"{jobId}:5")).Frames;

        frames.Where(f => f.Id is not null).Select(f => f.Sequence!.Value).Should().Equal(new long[] { 6, 7 },
            "after Last-Event-ID 5 the client must get publisher events 6 and 7, each once and under its own number; "
            + "the connection, snapshot and heartbeats are not numbered events and must not take a number. "
            + $"Frames: {Describe(frames)}");
        frames.Should().Contain(f => f.Event == SseHelper.EventTypes.JobComplete);
    }

    [Fact]
    public async Task SseStream_AnEventPublishedWhileTheSnapshotIsRead_IsStillDelivered()
    {
        var jobId = Guid.NewGuid();
        var bus = new FakeJobBus();
        var row = RunningRow(jobId);
        var published = false;
        var stream = NewStream(bus, () =>
        {
            // The job finishes while the stream reads its snapshot. Pub/sub keeps no history.
            if (!published)
            {
                published = true;
                bus.Publish(Completed(jobId, sequence: 1));
            }

            return row;
        });

        var frames = (await CollectAsync(stream, jobId, lastEventId: null)).Frames;

        frames.Should().Contain(f => f.Event == SseHelper.EventTypes.JobComplete,
            "the stream must be subscribed before it reads the snapshot; otherwise an event in between is never sent "
            + $"and the stream waits on heartbeats. Frames: {Describe(frames)}");
    }

    /// <summary>Beyond the POML's list, justified: the reconnect rule must not hold a stream open whose outcome the client already has.</summary>
    [Fact]
    public async Task SseStream_ReconnectAfterTheOutcomeWasSent_EndsInsteadOfWaiting()
    {
        var jobId = Guid.NewGuid();
        var bus = new FakeJobBus();
        bus.OnSubscribe = () => bus.Publish(Completed(jobId, sequence: 7));
        var stream = NewStream(bus, RunningRow(jobId));

        var run = await CollectAsync(stream, jobId, lastEventId: $"{jobId}:7");

        run.EndedOnItsOwn.Should().BeTrue($"the client already has event 7, the outcome. Frames: {Describe(run.Frames)}");
        run.Frames.Should().NotContain(f => f.Id != null, "nothing after event 7 was published");
    }

    /// <summary>Beyond the POML's list, justified: a stream must reach an outcome even when Redis drops its subscription.</summary>
    [Fact]
    public async Task SseStream_SubscriptionEndsWithoutAnOutcome_FallsBackToPollingAndDeliversIt()
    {
        var jobId = Guid.NewGuid();
        var bus = new FakeJobBus();
        bus.OnSubscribe = () =>
        {
            bus.Publish(Update(jobId, 60, sequence: 1));
            bus.EndSubscriptions(); // Redis drops the subscription
        };
        var reads = 0;
        var stream = NewStream(bus, () => ++reads == 1 ? RunningRow(jobId) : CompletedRow(jobId));

        var run = await CollectAsync(stream, jobId, lastEventId: null, TimeSpan.FromSeconds(5));

        run.Frames.Should().Contain(f => f.Event == SseHelper.EventTypes.JobComplete,
            $"the outcome is read from the job's row once the subscription is gone. Frames: {Describe(run.Frames)}");
        run.EndedOnItsOwn.Should().BeTrue();
    }

    [Fact]
    public async Task SseStream_WithoutRedis_PollsAndNumbersNothing()
    {
        var jobId = Guid.NewGuid();
        var bus = new FakeJobBus { Healthy = false };
        var reads = 0;
        var stream = NewStream(bus, () => ++reads < 3 ? RunningRow(jobId) : CompletedRow(jobId));

        var run = await CollectAsync(stream, jobId, lastEventId: null, TimeSpan.FromSeconds(5));

        run.Frames.Should().Contain(f => f.Event == SseHelper.EventTypes.JobComplete);
        run.Frames.Should().NotContain(f => f.Id != null,
            $"without Redis there is no publisher number line, so no frame may claim a number. Frames: {Describe(run.Frames)}");
    }

    // ══ Fixtures ═══════════════════════════════════════════════════════════════════════════════════════════════════

    private static JobContract ProfileRequestJob(string? key = null)
    {
        var jobId = Guid.NewGuid();
        return new JobContract
        {
            JobId = jobId,
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
            SubjectId = DocumentId.ToString(),
            CorrelationId = "issue-1086",
            IdempotencyKey = key ?? $"analysis-{DocumentId}-documentprofile-version-{jobId:N}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(new { DocumentId, Source = "Issue1086" })),
        };
    }

    private static AppOnlyDocumentAnalysisJobHandler NewHandler(IAppOnlyAnalysisService analysis, IIdempotencyService idempotency) =>
        new(analysis, idempotency, new Mock<DocumentTelemetry>().Object, NullLogger<AppOnlyDocumentAnalysisJobHandler>.Instance);

    private static Mock<IAppOnlyAnalysisService> SucceedingAnalysis()
    {
        var analysis = new Mock<IAppOnlyAnalysisService>();
        analysis
            .Setup(a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, string? _, CancellationToken _) => AppOnlyDocumentAnalysisResult.Success(id, profileUpdate: null));
        return analysis;
    }

    /// <summary>A profile that starts and never finishes until released: the process dies or is still running.</summary>
    private sealed class BlockingAnalysis
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingAnalysis()
        {
            var mock = new Mock<IAppOnlyAnalysisService>();
            mock.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid id, string? _, CancellationToken _) =>
                {
                    _entered.TrySetResult();
                    await _release.Task;
                    return AppOnlyDocumentAnalysisResult.Success(id, profileUpdate: null);
                });
            Service = mock.Object;
        }

        public IAppOnlyAnalysisService Service { get; }
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
    }

    /// <summary>A profile that honours cancellation the way <c>AppOnlyAnalysisService</c> does: it catches and returns Failed.</summary>
    private sealed class CancellableAnalysis
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellableAnalysis()
        {
            var mock = new Mock<IAppOnlyAnalysisService>();
            mock.Setup(a => a.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid id, string? _, CancellationToken ct) =>
                {
                    _entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct);
                        return AppOnlyDocumentAnalysisResult.Success(id, profileUpdate: null);
                    }
                    catch (OperationCanceledException ex)
                    {
                        return AppOnlyDocumentAnalysisResult.Failed(id, ex.Message);
                    }
                });
            Service = mock.Object;
        }

        public IAppOnlyAnalysisService Service { get; }
        public Task Entered => _entered.Task;
    }

    /// <summary>
    /// The shared cache instances share, behaving as <c>RedisCache</c> does where it matters here: async members throw on a
    /// cancelled token before doing any work. Expiry is not modelled; the tests run well inside the 10-minute lock.
    /// </summary>
    private sealed class RedisLikeCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();

        public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// One Redis shared by several <see cref="JobStatusService"/> instances: <c>INCR</c> on one keyspace, and every
    /// published message recorded.
    /// </summary>
    private sealed class SharedRedis
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly ConcurrentQueue<string> _published = new();
        private readonly Mock<IDatabase> _database = new();

        public SharedRedis()
        {
            _database
                .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, long by, CommandFlags _) => _counters.AddOrUpdate(key.ToString(), by, (_, n) => n + by));
        }

        public JobStatusService NewInstance()
        {
            var subscriber = new Mock<ISubscriber>();
            subscriber
                .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Callback((RedisChannel _, RedisValue message, CommandFlags _) => _published.Enqueue(message.ToString()))
                .ReturnsAsync(1L);

            var multiplexer = new Mock<IConnectionMultiplexer>();
            multiplexer.Setup(m => m.IsConnected).Returns(true);
            multiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);
            multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
            return new JobStatusService(multiplexer.Object, NullLogger<JobStatusService>.Instance);
        }

        public IReadOnlyList<long> PublishedSequences(Guid jobId) => _published
            .Select(json => JsonDocument.Parse(json).RootElement)
            .Where(root => root.GetProperty("jobId").GetGuid() == jobId)
            .Select(root => root.GetProperty("sequence").GetInt64())
            .ToList();
    }

    /// <summary>Redis pub/sub as the stream sees it: a subscriber receives only what is published after it subscribed.</summary>
    private sealed class FakeJobBus : IJobStatusService
    {
        private readonly List<Channel<JobStatusUpdate>> _subscribers = new();

        public Action? OnSubscribe { get; set; }

        public bool Healthy { get; set; } = true;

        /// <summary>Ends every live subscription without an outcome, as Redis does when it drops one.</summary>
        public void EndSubscriptions()
        {
            lock (_subscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    subscriber.Writer.TryComplete();
                }
            }
        }

        public void Publish(JobStatusUpdate update)
        {
            lock (_subscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    subscriber.Writer.TryWrite(update);
                }
            }
        }

        public async IAsyncEnumerable<JobStatusUpdate> SubscribeToJobAsync(
            Guid jobId,
            Action? onSubscribed = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<JobStatusUpdate>();
            lock (_subscribers)
            {
                _subscribers.Add(channel);
            }

            onSubscribed?.Invoke();
            OnSubscribe?.Invoke();
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
                if (update.UpdateType is JobStatusUpdateType.JobCompleted or JobStatusUpdateType.JobFailed or JobStatusUpdateType.JobCancelled)
                {
                    yield break;
                }
            }
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(Healthy);

        public Task<bool> PublishStatusUpdateAsync(JobStatusUpdate update, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateJobStatusAsync(Guid jobId, OfficeJobStatus status, int progress, string? currentPhase = null,
            CompletedPhase? completedPhase = null, JobResult? result = null, JobError? error = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> CompleteJobAsync(Guid jobId, JobResult result, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FailJobAsync(Guid jobId, JobError error, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static JobStatusUpdate Update(Guid jobId, int progress, long sequence = 0) => new()
    {
        JobId = jobId,
        UpdateType = JobStatusUpdateType.Progress,
        Status = OfficeJobStatus.Running,
        Progress = progress,
        CurrentPhase = "ProfileSummary",
        Sequence = sequence,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static JobStatusUpdate Completed(Guid jobId, long sequence) => new()
    {
        JobId = jobId,
        UpdateType = JobStatusUpdateType.JobCompleted,
        Status = OfficeJobStatus.Completed,
        Progress = 100,
        CurrentPhase = "Complete",
        Sequence = sequence,
        Result = new JobResult { Artifact = new CreatedArtifact { Type = ArtifactType.Document, Id = DocumentId } },
        Timestamp = DateTimeOffset.UtcNow,
    };

    /// <summary>A save's job row, still running and created just now (so it is not abandoned).</summary>
    private static ProcessingJobRecord RunningRow(Guid jobId) => new()
    {
        Id = jobId,
        Status = 1,
        Progress = 40,
        CurrentStage = "ProfileSummary",
        CreatedOn = DateTime.UtcNow,
    };

    /// <summary>The same row, finished: a save's job the workers marked Completed (status 2).</summary>
    private static ProcessingJobRecord CompletedRow(Guid jobId) => RunningRow(jobId) with { Status = 2, Progress = 100 };

    private static OfficeJobStatusService NewStream(IJobStatusService bus, ProcessingJobRecord row) => NewStream(bus, () => row);

    private static OfficeJobStatusService NewStream(IJobStatusService bus, Func<ProcessingJobRecord> readRow)
    {
        var rows = new Mock<IProcessingJobService>();
        rows.Setup(r => r.GetProcessingJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => readRow());
        return new OfficeJobStatusService(rows.Object, bus, TimeProvider.System, NullLogger<OfficeJobStatusService>.Instance);
    }

    private sealed record SseFrame(string? Event, string? Id, string? Data)
    {
        public long? Sequence => Id is null ? null : long.Parse(Id[(Id.LastIndexOf(':') + 1)..]);
    }

    private sealed record StreamRun(List<SseFrame> Frames, bool EndedOnItsOwn);

    private static async Task<StreamRun> CollectAsync(
        OfficeJobStatusService stream, Guid jobId, string? lastEventId, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        var text = new StringBuilder();
        var endedOnItsOwn = true;
        try
        {
            await foreach (var chunk in stream.StreamAsync(jobId, lastEventId, deadline.Token))
            {
                text.Append(Encoding.UTF8.GetString(chunk));
            }
        }
        catch (OperationCanceledException)
        {
            // The stream never ended on its own; the frames so far are the evidence.
            endedOnItsOwn = false;
        }

        var frames = text.ToString()
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                string? Field(string name) => block.Split('\n')
                    .Where(line => line.StartsWith(name + ": ", StringComparison.Ordinal))
                    .Select(line => line[(name.Length + 2)..])
                    .FirstOrDefault();
                return new SseFrame(Field("event"), Field("id"), Field("data"));
            })
            .ToList();
        return new StreamRun(frames, endedOnItsOwn);
    }

    private static string Describe(IEnumerable<SseFrame> frames) =>
        string.Join(", ", frames.Select(f => $"{f.Event}#{f.Sequence?.ToString() ?? "-"}"));

    /// <summary>
    /// The Office host with the Generate Profile route's two boundaries doubled: the job queue (<see cref="JobSubmissionService"/>,
    /// recording what is queued) and the profile facade (a profile that runs until the process stops).
    /// </summary>
    private sealed class ProfileRequestTestWebAppFactory : OfficeTestWebAppFactory
    {
        private readonly Dictionary<string, AccessRights> _grants = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<JobContract> _submitted = new();
        private readonly TaskCompletionSource _inProcessProfileCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _jobSubmitted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<JobContract> SubmittedJobs => _submitted;
        public bool InProcessProfileWasCancelled => _inProcessProfileCancelled.Task.IsCompleted;

        public void GrantWrite(Guid documentId) => _grants[documentId.ToString("D")] = AccessRights.Read | AccessRights.Write;

        public HttpClient AuthorizedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
            return client;
        }

        /// <summary>Waits until the background work either reached the queue or was cancelled in the process.</summary>
        public Task WaitForBackgroundWorkToSettleAsync() =>
            Task.WhenAny(_jobSubmitted.Task, _inProcessProfileCancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                var access = new Mock<IAccessDataSource>();
                access
                    .Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) => new AccessSnapshot
                    {
                        UserId = userId,
                        ResourceId = resourceId,
                        AccessRights = _grants.TryGetValue(resourceId, out var rights) ? rights : AccessRights.None,
                    });
                services.RemoveAll<IAccessDataSource>();
                services.AddSingleton(access.Object);

                var profile = new Mock<IDocumentProfileAi>();
                profile
                    .Setup(p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                    .Returns(async (Guid _, HttpContext _, CancellationToken ct) =>
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            _inProcessProfileCancelled.TrySetResult();
                            throw;
                        }

                        return DocumentProfileOutcome.Succeeded();
                    });
                services.RemoveAll<IDocumentProfileAi>();
                services.AddScoped(_ => profile.Object);

                var queue = new Mock<JobSubmissionService>(
                    MockBehavior.Loose,
                    Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
                    Mock.Of<ILogger<JobSubmissionService>>(),
                    new Mock<ServiceBusClient>().Object);
                queue
                    .Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                    .Callback((JobContract job, CancellationToken _) =>
                    {
                        _submitted.Enqueue(job);
                        _jobSubmitted.TrySetResult();
                    })
                    .Returns(Task.CompletedTask);
                services.RemoveAll<JobSubmissionService>();
                services.AddSingleton(queue.Object);
            });
        }
    }
}
