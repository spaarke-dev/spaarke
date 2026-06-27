using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="PinnedContextRecallService"/> (R6 Pillar 7 / task 066, D-C-19).
/// </summary>
/// <remarks>
/// Covers: kill-switch short-circuit, empty user-message short-circuit, no-pins short-circuit,
/// happy-path top-K ranking by cosine similarity, similarity threshold filtering, topK
/// clamping, tenant + matter isolation propagation, per-pin embedding failure tolerance,
/// LLM circuit-broken graceful failure on the user-message embedding.
/// </remarks>
public sealed class PinnedContextRecallServiceTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string MatterX = "matter-x";
    private const string UserAlice = "user-alice";

    private readonly Mock<IPinnedContextRepository> _repo = new();
    private readonly Mock<IEmbeddingCache> _cache = new();
    private readonly Mock<IOpenAiClient> _openAi = new();
    private readonly Mock<ILogger<PinnedContextRecallService>> _logger = new();

    private static PinnedContextRecallService CreateSut(
        Mock<IPinnedContextRepository> repo,
        Mock<IEmbeddingCache> cache,
        Mock<IOpenAiClient> openAi,
        Mock<ILogger<PinnedContextRecallService>> logger,
        PinnedContextRecallOptions? options = null)
    {
        options ??= new PinnedContextRecallOptions { Enabled = true };
        return new PinnedContextRecallService(
            repo.Object,
            cache.Object,
            openAi.Object,
            Options.Create(options),
            logger.Object);
    }

    private static PinnedContextItem BuildPin(
        string pinId,
        string tenantId,
        string matterId,
        string content,
        string? userId = null)
    {
        userId ??= UserAlice;
        return new PinnedContextItem
        {
            Id = $"pinned-context_{tenantId}_{pinId}",
            DocumentType = "pinned-context",
            TenantId = tenantId,
            UserId = userId,
            PinType = PinType.MatterFact,
            Title = $"Pin {pinId}",
            Content = content,
            MatterId = matterId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };
    }

    /// <summary>
    /// Stubs the cache so the first call for a piece of content returns the supplied vector
    /// (simulating cache HIT). Avoids any IOpenAiClient.GenerateEmbeddingAsync interaction.
    /// </summary>
    private void StubCacheHit(string content, float[] vector)
    {
        _cache
            .Setup(c => c.GetEmbeddingForContentAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)new ReadOnlyMemory<float>(vector));
    }

    // =========================================================================================
    // Kill switch
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ReturnsEmpty_WhenKillSwitchOff()
    {
        var sut = CreateSut(_repo, _cache, _openAi, _logger,
            new PinnedContextRecallOptions { Enabled = false });

        var result = await sut.RecallAsync(TenantA, MatterX, "hello", topK: 5);

        result.Should().BeEmpty();
        // Rationale: kill switch off must short-circuit before any repository call.
        _repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cache.Verify(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================================
    // Empty / whitespace user message
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ReturnsEmpty_WhenUserMessageEmpty()
    {
        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "", topK: 5);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetByMatterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecallAsync_ReturnsEmpty_WhenUserMessageWhitespace()
    {
        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "   \n  ", topK: 5);

        result.Should().BeEmpty();
    }

    // =========================================================================================
    // No pins
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ReturnsEmpty_WhenNoPinsForMatter()
    {
        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "question", topK: 5);

        result.Should().BeEmpty();
        // Rationale: no pins means there's nothing to rank — skip embedding the user message.
        _cache.Verify(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================================
    // Happy path — top-K ranking
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ReturnsTopKPinsSortedBySimilarityDescending()
    {
        // Construct embeddings where similarity to userMessage is deterministic and ordered.
        // Use 3-D unit vectors so cosine similarity = dot product.
        var userMessageVec = new[] { 1f, 0f, 0f };

        var highSimVec = new[] { 0.95f, 0.0f, 0.0f };      // sim ≈ 0.95 (exact: 0.95 / sqrt(0.9025) = 1.0; rescale)
        var medSimVec = new[] { 0.7f, 0.7f, 0.0f };        // sim ≈ 0.707
        var lowSimVec = new[] { 0.5f, 0.0f, 0.5f };        // sim ≈ 0.707 too — pick differently
        var noiseVec = new[] { 0.1f, 0.9f, 0.4f };         // sim ≈ 0.102

        var pinHigh = BuildPin("p-high", TenantA, MatterX, "high-relevance content");
        var pinMed = BuildPin("p-med", TenantA, MatterX, "medium-relevance content");
        var pinLow = BuildPin("p-low", TenantA, MatterX, "low-relevance content");
        var pinNoise = BuildPin("p-noise", TenantA, MatterX, "noise content");

        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinNoise, pinMed, pinHigh, pinLow });

        StubCacheHit("user query", userMessageVec);
        StubCacheHit("high-relevance content", highSimVec);
        StubCacheHit("medium-relevance content", medSimVec);
        StubCacheHit("low-relevance content", lowSimVec);
        StubCacheHit("noise content", noiseVec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "user query", topK: 3);

        result.Should().HaveCount(3, because: "topK=3 caps the output cardinality");
        result[0].Id.Should().Be(pinHigh.Id, because: "highest cosine similarity comes first");
        // Medium tier comes after high — both mid/low could land in slot [1]; what matters is the
        // noise pin (lowest similarity) was cut by the topK=3 limit.
        result[1].Id.Should().BeOneOf(pinMed.Id, pinLow.Id);
        result.Select(p => p.Id).Should().NotContain(pinNoise.Id,
            because: "the noise pin has the lowest similarity and should be cut by topK=3");
    }

    // =========================================================================================
    // Similarity threshold filtering
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_FiltersOutPinsBelowSimilarityThreshold()
    {
        var userMessageVec = new[] { 1f, 0f, 0f };
        var aboveVec = new[] { 1f, 0f, 0f };       // sim = 1.0
        var belowVec = new[] { 0.1f, 1f, 0f };     // sim ≈ 0.0995

        var pinAbove = BuildPin("p-above", TenantA, MatterX, "above-threshold content");
        var pinBelow = BuildPin("p-below", TenantA, MatterX, "below-threshold content");

        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinAbove, pinBelow });

        StubCacheHit("user msg", userMessageVec);
        StubCacheHit("above-threshold content", aboveVec);
        StubCacheHit("below-threshold content", belowVec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger,
            new PinnedContextRecallOptions { Enabled = true, SimilarityThreshold = 0.5 });

        var result = await sut.RecallAsync(TenantA, MatterX, "user msg", topK: 5);

        result.Should().ContainSingle().Which.Id.Should().Be(pinAbove.Id,
            because: "the below-threshold pin must be filtered out");
    }

    // =========================================================================================
    // topK clamping
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ClampsTopKToCeilingWhenCallerExceedsBounds()
    {
        var pins = Enumerable.Range(0, 25)
            .Select(i => BuildPin($"p-{i}", TenantA, MatterX, $"content-{i}"))
            .ToArray();

        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pins);

        // All embeddings identical → all share similarity 1.0; ranking is stable but topK
        // is the only differentiator on count.
        var sharedVec = new[] { 1f, 0f, 0f };
        StubCacheHit("msg", sharedVec);
        foreach (var p in pins) StubCacheHit(p.Content, sharedVec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "msg", topK: 999);

        result.Should().HaveCount(PinnedContextRecallService.MaxTopK,
            because: "topK=999 must clamp to the MaxTopK ceiling (20)");
    }

    [Fact]
    public async Task RecallAsync_ClampsTopKToFloorWhenCallerSuppliesZero()
    {
        var pin = BuildPin("p1", TenantA, MatterX, "content");
        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pin });

        var vec = new[] { 1f, 0f, 0f };
        StubCacheHit("msg", vec);
        StubCacheHit(pin.Content, vec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "msg", topK: 0);

        result.Should().HaveCount(1,
            because: "topK=0 must clamp to MinTopK=1, returning the single available pin");
    }

    // =========================================================================================
    // Tenant + matter isolation propagation
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_PassesTenantAndMatterToRepositoryUnchanged()
    {
        var pin = BuildPin("p1", TenantB, MatterX, "content");
        _repo
            .Setup(r => r.GetByMatterAsync(TenantB, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pin });

        var vec = new[] { 1f, 0f, 0f };
        StubCacheHit("msg", vec);
        StubCacheHit(pin.Content, vec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantB, MatterX, "msg", topK: 5);

        result.Should().ContainSingle();
        // Rationale: tenant + matter ids must flow through unchanged — ADR-014 partition isolation.
        _repo.Verify(r => r.GetByMatterAsync(TenantB, MatterX, It.IsAny<CancellationToken>()),
            Times.Once);
        // Verify tenant A was NEVER queried.
        _repo.Verify(r => r.GetByMatterAsync(TenantA, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecallAsync_RejectsEmptyTenantId()
    {
        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.RecallAsync("", MatterX, "msg", topK: 5));
    }

    [Fact]
    public async Task RecallAsync_RejectsEmptyMatterId()
    {
        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.RecallAsync(TenantA, "", "msg", topK: 5));
    }

    // =========================================================================================
    // Per-pin embedding failure tolerance
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_SkipsPinsWithFailedEmbeddingButReturnsOthers()
    {
        var pinOk = BuildPin("p-ok", TenantA, MatterX, "good content");
        var pinFail = BuildPin("p-fail", TenantA, MatterX, "bad content");

        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinOk, pinFail });

        var vec = new[] { 1f, 0f, 0f };
        StubCacheHit("msg", vec);
        StubCacheHit("good content", vec);

        // bad content: cache miss → generate throws generic exception → pin dropped.
        _cache
            .Setup(c => c.GetEmbeddingForContentAsync("bad content", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);
        _openAi
            .Setup(c => c.GenerateEmbeddingAsync("bad content", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated embedding failure"));

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "msg", topK: 5);

        result.Should().ContainSingle().Which.Id.Should().Be(pinOk.Id,
            because: "the failed pin must be dropped silently; the successful pin must still be ranked");
    }

    [Fact]
    public async Task RecallAsync_SkipsPinsWithEmptyContent()
    {
        // A pin with whitespace-only content would have been blocked by PinnedContextRepository
        // at write time, but defensive: if a malformed item slips through, skip it.
        var pinEmpty = new PinnedContextItem
        {
            Id = "pinned-context_tenant-a_p-empty",
            DocumentType = "pinned-context",
            TenantId = TenantA,
            UserId = UserAlice,
            PinType = PinType.MatterFact,
            Title = "Empty",
            Content = "   ",
            MatterId = MatterX,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = UserAlice
        };
        var pinOk = BuildPin("p-ok", TenantA, MatterX, "real content");

        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinEmpty, pinOk });

        var vec = new[] { 1f, 0f, 0f };
        StubCacheHit("msg", vec);
        StubCacheHit("real content", vec);

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "msg", topK: 5);

        result.Should().ContainSingle().Which.Id.Should().Be(pinOk.Id);
    }

    // =========================================================================================
    // LLM circuit-broken on user-message embedding
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_ReturnsEmpty_WhenUserMessageEmbeddingCircuitBroken()
    {
        var pinOk = BuildPin("p-ok", TenantA, MatterX, "content");
        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pinOk });

        _cache
            .Setup(c => c.GetEmbeddingForContentAsync("msg", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);
        _openAi
            .Setup(c => c.GenerateEmbeddingAsync("msg", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OpenAiCircuitBrokenException(TimeSpan.FromSeconds(30)));

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        var result = await sut.RecallAsync(TenantA, MatterX, "msg", topK: 5);

        result.Should().BeEmpty(
            because: "circuit broken on the user-message embedding must return empty (soft-fail) — caller short-circuits selective recall");
    }

    // =========================================================================================
    // Cancellation propagates
    // =========================================================================================

    [Fact]
    public async Task RecallAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pin = BuildPin("p1", TenantA, MatterX, "content");
        _repo
            .Setup(r => r.GetByMatterAsync(TenantA, MatterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pin });

        _cache
            .Setup(c => c.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut(_repo, _cache, _openAi, _logger);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.RecallAsync(TenantA, MatterX, "msg", topK: 5, cancellationToken: cts.Token));
    }

    // =========================================================================================
    // CosineSimilarity (pure-function unit tests)
    // =========================================================================================

    [Fact]
    public void CosineSimilarity_ReturnsOne_ForIdenticalVectors()
    {
        var v = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var sim = PinnedContextRecallService.CosineSimilarity(v, v);
        sim.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void CosineSimilarity_ReturnsZero_ForOrthogonalVectors()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { 0f, 1f, 0f };
        var sim = PinnedContextRecallService.CosineSimilarity(a, b);
        sim.Should().BeApproximately(0.0, 1e-6);
    }

    [Fact]
    public void CosineSimilarity_ReturnsZero_ForEmptyVectors()
    {
        var a = Array.Empty<float>();
        var b = Array.Empty<float>();
        var sim = PinnedContextRecallService.CosineSimilarity(a, b);
        sim.Should().Be(0.0);
    }

    [Fact]
    public void CosineSimilarity_ReturnsZero_ForMismatchedLengths()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 1f, 0f, 0f };
        var sim = PinnedContextRecallService.CosineSimilarity(a, b);
        sim.Should().Be(0.0,
            because: "mismatched dimensions are a degenerate case — degrade gracefully to 0 rather than throw");
    }

    [Fact]
    public void CosineSimilarity_ReturnsZero_WhenEitherVectorIsZeroMagnitude()
    {
        var a = new float[] { 0f, 0f, 0f };
        var b = new float[] { 1f, 1f, 1f };
        var sim = PinnedContextRecallService.CosineSimilarity(a, b);
        sim.Should().Be(0.0);
    }

    // =========================================================================================
    // Constructor null-arg defense
    // =========================================================================================

}
