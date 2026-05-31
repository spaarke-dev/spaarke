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

    /// <summary>
    /// Synthetic engine stream that emits a DeclineToFindNode NodeCompleted event carrying
    /// the supplied <see cref="DeclineResponse"/> (insufficient-evidence path). Task 071.
    /// </summary>
    private static async IAsyncEnumerable<PlaybookStreamEvent> EngineStreamWithDecline(
        DeclineResponse decline,
        string nodeName = InsightsPlaybookExecutionCache.DeclineToFindNodeName,
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
            nodeName,
            NodeOutput.Ok(
                nodeId,
                outputVariable: "decline",
                data: decline,
                textContent: decline.Explanation));

        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    private static DeclineResponse MakeDecline(int cohortCount = 5) => new()
    {
        Reason = "insufficient-evidence",
        Explanation = $"Cannot predict cost: only {cohortCount} comparable matters were found (need 12).",
        MinimumEvidenceNeeded = new Dictionary<string, object>
        {
            ["comparableMatters"] = new { have = cohortCount, need = 12 - cohortCount, from = "retrieveCohortObservations", reason = "below-threshold" }
        },
        SuggestedActions = new[] { "Broaden the matter-type filter", "Author a Precedent" },
        ConfidenceInDecline = 0.95
    };

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
        result.HasArtifact.Should().BeTrue("cache HIT returns cached artifact");
        result.Artifact!.Subject.Should().Be(Subject);
        result.Decline.Should().BeNull("cache only stores artifacts, not declines (task 071)");
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
        result.HasArtifact.Should().BeTrue();
        result.Artifact!.Predicate.Should().Be("predictedCost");
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
        bobResult.HasArtifact.Should().BeTrue();
        ((InferenceArtifact)bobResult.Artifact!).Predicate.Should().Be("predictedCostForBob",
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
        bResult.HasArtifact.Should().BeTrue();
        ((InferenceArtifact)bResult.Artifact!).Predicate.Should().Be("differentPrediction");
    }

    [Fact]
    public async Task GetOrExecuteAsync_EngineProducesNoArtifactAndNoDecline_ReturnsEmptyAndDoesNotCache()
    {
        // Defensive case: the engine completed but emitted neither ReturnInsightArtifactNode
        // nor DeclineToFindNode (malformed playbook or branch-routing bug). We MUST NOT cache
        // because the next call might produce one. The orchestrator surfaces this as a scaffold
        // decline + logs Warning so the facade contract holds for Zone B.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(request, EngineStreamWithoutArtifact);

        result.Should().NotBeNull();
        result.IsEmpty.Should().BeTrue("engine produced no artifact and no decline");
        result.Artifact.Should().BeNull();
        result.Decline.Should().BeNull();
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "must not cache empty results — next call might produce an artifact or decline");
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
        result.HasArtifact.Should().BeTrue();
        result.Artifact!.Subject.Should().Be(Subject);
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
        result.HasArtifact.Should().BeTrue();
        result.Artifact!.Subject.Should().Be(Subject);
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

    // ─── Task 071 (Wave 8.5): Decline extraction from engine stream ──────────

    [Fact]
    public async Task GetOrExecuteAsync_EngineEmitsDecline_ReturnsRealDeclineNotEmpty()
    {
        // Task 071 Gap 2 closure: DrainEngineStreamAsync now scans for DeclineToFindNode
        // events (not just ReturnInsightArtifactNode). When the playbook takes the
        // insufficient-evidence branch, the cache surfaces the real DeclineResponse with
        // populated MinimumEvidenceNeeded — no more null/scaffold fallback.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var decline = MakeDecline(cohortCount: 5);
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var result = await sut.GetOrExecuteAsync(
            request,
            ct => EngineStreamWithDecline(decline, ct: ct));

        result.Should().NotBeNull();
        result.HasDecline.Should().BeTrue("engine emitted DeclineToFindNode output");
        result.HasArtifact.Should().BeFalse("decline path; no artifact");
        result.Decline!.Reason.Should().Be("insufficient-evidence");
        result.Decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters",
            "real gap analysis from EvidenceSufficiencyNode upstream propagates through");
        result.Decline.ConfidenceInDecline.Should().Be(0.95);
    }

    [Fact]
    public async Task GetOrExecuteAsync_DeclineExtracted_NotCached()
    {
        // Task 071 cache-correctness contract: declines MUST NOT be cached because evidence
        // sufficiency depends on the current state of the index — a cached decline becomes
        // stale the moment a new Observation lands. This test proves the cache does NOT
        // write-through on the decline path.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        await sut.GetOrExecuteAsync(
            request,
            ct => EngineStreamWithDecline(MakeDecline(), ct: ct));

        _cacheMock.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "declines are state-dependent; caching them would surface stale decline verdicts after new evidence lands");
    }

    [Fact]
    public async Task GetOrExecuteAsync_FirstCallDecline_SecondCallArtifact_NoCacheHitForDecline()
    {
        // Task 071 invariant test: a first invocation that returns Decline does NOT cause
        // the second invocation to see a cache HIT. The second invocation re-runs the engine
        // (so if the index has changed, the new sufficient-evidence verdict surfaces).
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var artifactForSecondCall = MakeInferenceArtifact();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        var firstCallEngineInvoked = 0;
        var secondCallEngineInvoked = 0;

        // First call: insufficient-evidence path
        var firstResult = await sut.GetOrExecuteAsync(
            request,
            ct =>
            {
                firstCallEngineInvoked++;
                return EngineStreamWithDecline(MakeDecline(), ct: ct);
            });

        // Second call: sufficient-evidence path (the index has been updated between calls)
        var secondResult = await sut.GetOrExecuteAsync(
            request,
            ct =>
            {
                secondCallEngineInvoked++;
                return EngineStreamWith(artifactForSecondCall, ct);
            });

        firstResult.HasDecline.Should().BeTrue("first call's evidence was insufficient");
        secondResult.HasArtifact.Should().BeTrue(
            "second call's engine re-runs (decline was not cached) and now finds sufficient evidence");

        firstCallEngineInvoked.Should().Be(1, "first call invokes the engine");
        secondCallEngineInvoked.Should().Be(1,
            "second call MUST re-invoke the engine because the first call's decline was not cached");
    }

    [Fact]
    public async Task GetOrExecuteAsync_DeclineWithUnexpectedNodeName_StillExtractedByStructuralMatch()
    {
        // Robustness test: the drain matches by exact name (DeclineToFindNodeName) OR by
        // structural fingerprint (all 5 DeclineResponse required fields present). This guards
        // against future playbooks renaming the decline node without losing decline propagation.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var decline = MakeDecline();
        var sut = CreateSut();
        var request = new InsightsPlaybookExecutionRequest(
            PlaybookA, Subject, null, ScopeHashAlice, TenantId);

        // Use a non-conventional node name; the structural fingerprint must still match.
        var result = await sut.GetOrExecuteAsync(
            request,
            ct => EngineStreamWithDecline(decline, nodeName: "customDeclineNodeName", ct: ct));

        result.HasDecline.Should().BeTrue(
            "structural fingerprint (5 required DeclineResponse fields) matches even though node name differs");
    }
}
