// FR-28 (task 055) — unit tests for ComposePushSaveStatusStore, the ADR-009 Redis persistence
// wrapper for the push/save pipeline's cross-request JobAwareCompletionState.
//
// ADR-038 KEEP category: the Redis persistence surface is covered with a REAL
// MemoryDistributedCache (same pattern as AnnotationReanchorServiceTests / SpeSyncOrchestratorTests)
// — a genuine round-trip through IDistributedCache, not a mock of the cache. No
// Mock<HttpMessageHandler>, no DI, no transport mocks.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public class ComposePushSaveStatusStoreTests
{
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static JobAwareCompletionState SampleState(string documentSpeId) =>
        JobAwareCompletionStateProjector.Project(
            new JobContract { JobType = "compose-push-save", SubjectId = documentSpeId },
            new[]
            {
                new StoredStepSignal { StepName = "push", StoredStatus = JobStatus.Completed, Started = true },
                new StoredStepSignal { StepName = "save", StoredStatus = JobStatus.Completed, Started = true },
                new StoredStepSignal { StepName = "version", StoredStatus = JobStatus.Completed, Started = true },
            },
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsTheCompletionState()
    {
        var sut = new ComposePushSaveStatusStore(NewCache());
        var state = SampleState("spe-item-1");

        await sut.SaveAsync("spe-item-1", state, CancellationToken.None);
        var read = await sut.GetAsync("spe-item-1", CancellationToken.None);

        read.Should().NotBeNull();
        read!.JobType.Should().Be("compose-push-save");
        read.Aggregate.Should().Be(JobAwareState.Completed);
        read.Steps.Select(s => s.StepName).Should().BeEquivalentTo(new[] { "push", "save", "version" });
    }

    [Fact]
    public async Task GetAsync_WhenNoEntrySaved_ReturnsNull()
    {
        var sut = new ComposePushSaveStatusStore(NewCache());

        var read = await sut.GetAsync("never-saved-item", CancellationToken.None);

        read.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSameDocument_OverwritesThePriorEntry()
    {
        var cache = NewCache();
        var sut = new ComposePushSaveStatusStore(cache);
        var documentSpeId = "spe-item-2";

        var failedFirstAttempt = JobAwareCompletionStateProjector.Project(
            new JobContract { JobType = "compose-push-save", SubjectId = documentSpeId },
            new[] { new StoredStepSignal { StepName = "push", StoredStatus = JobStatus.Failed, Started = true, Attempt = 1, MaxAttempts = 1 } },
            DateTimeOffset.UtcNow);
        await sut.SaveAsync(documentSpeId, failedFirstAttempt, CancellationToken.None);

        var succeededRetry = SampleState(documentSpeId);
        await sut.SaveAsync(documentSpeId, succeededRetry, CancellationToken.None);

        var read = await sut.GetAsync(documentSpeId, CancellationToken.None);
        read!.Aggregate.Should().Be(JobAwareState.Completed, "a fresh push overwrites the prior failed status");
    }
}
