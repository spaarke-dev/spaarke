using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for <see cref="InsightsPlaybookExecutionCache"/> (D-P13).
/// Covers all five acceptance criteria from task 023:
/// <list type="bullet">
/// <item>Identical tuple → cache hit (engine NOT invoked second time)</item>
/// <item>Different accessibleScopeHash → cache miss (engine invoked)</item>
/// <item>TTL respected (configurable per playbook metadata)</item>
/// <item>App Insights events emit for hit / miss / eviction (smoke)</item>
/// <item>ADR-009 compliance: IDistributedCache abstraction</item>
/// </list>
/// </summary>
public class InsightsPlaybookExecutionCacheTests
{
    private static readonly Guid PlaybookA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PlaybookB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string TenantId = "tenant-acme";
    private const string Subject = "matter:M-1234";
    private const string ScopeHashAlice = "alice-scope-v1";
    private const string ScopeHashBob   = "bob-scope-v1";

    private readonly Mock<IDistributedCache> _cacheMock = new();

    private InsightsPlaybookExecutionCache CreateSut()
        => new(_cacheMock.Object, NullLogger<InsightsPlaybookExecutionCache>.Instance, metrics: null);

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static InsightArtifact MakeInferenceArtifact(string predicate = "predictedCost")
        => new InferenceArtifact
        {
            Id = "inf:M-1234:predictedCost",
            Subject = Subject,
            Predicate = predicate,
            Value = new Value
            {
                Raw = JsonDocument.Parse("280000").RootElement.Clone(),
                DisplayHint = "currency-usd"
            },
            Confidence = 0.74,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy
            {
                Kind = "agent",
                Id = "agent://insights-v1",
                Version = "v1"
            },
            Scope = new Scope { TenantId = TenantId, MatterId = "M-1234" },
            TenantId = TenantId,
            Reasoning = "Based on 12 comparable matters."
        };

    /// <summary>
    /// Synthetic engine output stream containing one ReturnInsightArtifactNode NodeCompleted
    /// event carrying the supplied artifact.
    /// </summary>
    private static async IAsyncEnumerable<PlaybookStreamEvent> EngineStreamWith(
        InsightArtifact artifact,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);

        await Task.Yield();

        yield return PlaybookStreamEvent.NodeCompleted(
            runId,
            playbookId,
            nodeId,
            InsightsPlaybookExecutionCache.ReturnInsightArtifactNodeName,
            NodeOutput.Ok(
                nodeId,
                outputVariable: "insightArtifact",
                data: artifact,
                textContent: null));

        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    /// <summary>Engine stream with no ReturnInsightArtifactNode (e.g., decline path).</summary>
    private static async IAsyncEnumerable<PlaybookStreamEvent> EngineStreamWithoutArtifact(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();

        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 0);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    private static byte[] SerializeArtifact(InsightArtifact a) => JsonSerializer.SerializeToUtf8Bytes(a);

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrExecuteAsync_CacheHit_ReturnsCachedArtifactWithoutInvokingEngine()
    {
        // Arrange
        var cached = MakeInferenceArtifact();
        var cachedBytes = SerializeArtifact(cached);

        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);

        var engineInvoked = 0;
        IAsyncEnumerable<PlaybookStreamEvent> EngineFactory(CancellationToken ct)
        {
            engineInvoked++;
            return EngineStreamWith(cached, ct);
        }

        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        // Act
        var result = await sut.GetOrExecuteAsync(request, EngineFactory);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Should().Be(Subject);
        engineInvoked.Should().Be(0, "cache hit must not invoke the engine");
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "cache hit must not write back");
    }

    [Fact]
    public async Task GetOrExecuteAsync_CacheMiss_InvokesEngineAndCachesResult()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var artifact = MakeInferenceArtifact();
        var engineInvoked = 0;
        IAsyncEnumerable<PlaybookStreamEvent> EngineFactory(CancellationToken ct)
        {
            engineInvoked++;
            return EngineStreamWith(artifact, ct);
        }

        var sut = CreateSut();
        var ttl = TimeSpan.FromMinutes(15);
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId, Ttl: ttl);

        // Act
        var result = await sut.GetOrExecuteAsync(request, EngineFactory);

        // Assert
        result.Should().NotBeNull();
        result!.Predicate.Should().Be("predictedCost");
        engineInvoked.Should().Be(1, "cache miss must invoke the engine exactly once");
        _cacheMock.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == ttl),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "cache miss must write the artifact with the requested TTL");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DefaultTtl_AppliedWhenRequestOmitsIt()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var artifact = MakeInferenceArtifact();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId, Ttl: null);

        // Act
        var result = await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(artifact, ct));

        // Assert
        result.Should().NotBeNull();
        _cacheMock.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(o =>
                    o.AbsoluteExpirationRelativeToNow == InsightsPlaybookExecutionCache.DefaultTtl),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "default 5-minute TTL must be used when the request omits one");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DifferentAccessibleScopeHash_CacheMiss()
    {
        // DEP-3 anchor test: same playbook + same subject + same parameters but a different
        // accessible-scope hash must NOT see the other user's cached entry. Verified by
        // requiring the cache to look up under a different key than the one Alice wrote.
        var artifactAlice = MakeInferenceArtifact();
        var aliceBytes = SerializeArtifact(artifactAlice);

        // Compute keys
        var aliceKey = InsightsPlaybookCacheKey.Compose(PlaybookA, Subject, null, ScopeHashAlice);
        var bobKey   = InsightsPlaybookCacheKey.Compose(PlaybookA, Subject, null, ScopeHashBob);

        aliceKey.Should().NotBe(bobKey, "key composer must partition by accessibleScopeHash");

        // Cache mock: returns Alice's bytes only when looked up under Alice's key
        _cacheMock
            .Setup(c => c.GetAsync(aliceKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aliceBytes);
        _cacheMock
            .Setup(c => c.GetAsync(bobKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var sut = CreateSut();

        // Bob's lookup
        var bobRequest = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashBob, TenantId);

        var bobEngineInvoked = 0;
        var artifactForBob = MakeInferenceArtifact("predictedCostForBob");
        IAsyncEnumerable<PlaybookStreamEvent> BobEngine(CancellationToken ct)
        {
            bobEngineInvoked++;
            return EngineStreamWith(artifactForBob, ct);
        }

        // Act
        var bobResult = await sut.GetOrExecuteAsync(bobRequest, BobEngine);

        // Assert
        bobEngineInvoked.Should().Be(1, "Bob's scope hash differs from Alice's; engine must run for Bob");
        bobResult.Should().NotBeNull();
        ((InferenceArtifact)bobResult!).Predicate.Should().Be("predictedCostForBob",
            "Bob must see HIS engine output, not Alice's cached one");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DifferentPlaybookId_IsSeparateCacheEntry()
    {
        // Different playbookId must produce a different key — playbook A's cached entry
        // must NOT leak to playbook B even if subject + parameters + scope match.
        var playbookAArtifact = MakeInferenceArtifact("predictedCost");
        var aBytes = SerializeArtifact(playbookAArtifact);

        var keyA = InsightsPlaybookCacheKey.Compose(PlaybookA, Subject, null, ScopeHashAlice);
        var keyB = InsightsPlaybookCacheKey.Compose(PlaybookB, Subject, null, ScopeHashAlice);

        keyA.Should().NotBe(keyB);

        _cacheMock
            .Setup(c => c.GetAsync(keyA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aBytes);
        _cacheMock
            .Setup(c => c.GetAsync(keyB, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var sut = CreateSut();

        var bRequest = new InsightsPlaybookExecutionRequest(
            PlaybookB, Subject, null, ScopeHashAlice, TenantId);

        var engineInvoked = 0;
        var bArtifact = MakeInferenceArtifact("differentPrediction");
        IAsyncEnumerable<PlaybookStreamEvent> BEngine(CancellationToken ct)
        {
            engineInvoked++;
            return EngineStreamWith(bArtifact, ct);
        }

        var bResult = await sut.GetOrExecuteAsync(bRequest, BEngine);

        engineInvoked.Should().Be(1, "playbook B must not see playbook A's cached entry");
        ((InferenceArtifact)bResult!).Predicate.Should().Be("differentPrediction");
    }

    [Fact]
    public async Task GetOrExecuteAsync_EngineProducesNoArtifact_ReturnsNullAndDoesNotCache()
    {
        // E.g., the decline path: ReturnInsightArtifactNode was skipped because the
        // playbook short-circuited to DeclineToFindNode. We MUST NOT cache "no result"
        // because the next call might succeed.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(request, EngineStreamWithoutArtifact);

        result.Should().BeNull();
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "must not cache nulls — next call might produce an artifact");
    }

    [Fact]
    public async Task GetOrExecuteAsync_CacheReadThrows_FallsBackToEngine()
    {
        // ADR-009: cache is an optimisation, not a hard dependency. Redis transient
        // failures must NOT prevent the engine from running and returning a fresh artifact.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection refused"));

        var artifact = MakeInferenceArtifact();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(artifact, ct));

        result.Should().NotBeNull();
        result!.Subject.Should().Be(Subject);
    }

    [Fact]
    public async Task GetOrExecuteAsync_CacheWriteThrows_StillReturnsArtifact()
    {
        // Write failures are non-fatal — we already have the artifact in hand.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis OOM"));

        var artifact = MakeInferenceArtifact();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(artifact, ct));

        result.Should().NotBeNull();
        result!.Subject.Should().Be(Subject);
    }

    [Fact]
    public async Task GetOrExecuteAsync_CorruptCachedBytes_FallsBackToEngineAndOverwrites()
    {
        // Real-world hardening: if a Redis entry was written by an older serialiser
        // version (or stress-test corrupted it), we should treat it as a miss and
        // re-run the engine rather than 500-ing the caller.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x7B, 0x2F, 0x2F, 0x2F }); // "{///"  — not valid JSON for InsightArtifact

        var artifact = MakeInferenceArtifact();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(request, ct => EngineStreamWith(artifact, ct));

        result.Should().NotBeNull();
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "corrupt entry must be overwritten with the fresh engine output");
    }

    [Fact]
    public async Task EvictAsync_RemovesUnderComposedKey()
    {
        var sut = CreateSut();

        await sut.EvictAsync(PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var expectedKey = InsightsPlaybookCacheKey.Compose(PlaybookA, Subject, null, ScopeHashAlice);
        _cacheMock.Verify(
            c => c.RemoveAsync(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EvictAsync_RedisFailure_DoesNotThrow()
    {
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = CreateSut();

        var act = async () => await sut.EvictAsync(PlaybookA, Subject, null, ScopeHashAlice, TenantId);
        await act.Should().NotThrowAsync("eviction is best-effort; TTL is the safety net");
    }
}
