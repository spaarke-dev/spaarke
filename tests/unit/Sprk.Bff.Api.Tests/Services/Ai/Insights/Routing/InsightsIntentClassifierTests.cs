using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Routing;

/// <summary>
/// Unit tests for <see cref="InsightsIntentClassifier"/> (Wave E2 task 041 / FR-05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mock strategy</b>: <see cref="IOpenAiClient"/> is mocked so the suite exercises the
/// full prompt-build → schema-validate → parse → cache flow without making a real LLM call.
/// This is the canonical pattern per the project's <c>tests/CLAUDE.md</c> ("mock at
/// boundaries") and matches the latency-budget verification approach the POML calls for
/// (mock-based in-process; live latency assertion deferred to post-deploy smoke).
/// </para>
/// <para>
/// <b>Coverage by POML acceptance criterion</b>:
/// <list type="number">
///   <item>Classifier returns valid {path, playbookId?, confidence} —
///   <see cref="Classify_PlaybookIntent_ReturnsPlaybookPathWithId"/>,
///   <see cref="Classify_RagIntent_ReturnsRagPathWithoutPlaybookId"/></item>
///   <item>Below-threshold queries fall back to RAG —
///   <see cref="Classify_LowConfidence_SetsBelowThresholdTrue"/></item>
///   <item>forceMode override — verified at endpoint-test layer (wire DTO)</item>
///   <item>Latency &lt; 500ms p95 on sample queries (mock-based) —
///   <see cref="Classify_HundredQueries_StayUnderMockLatencyBudget"/></item>
///   <item>spec.md FR-05 partially met — multiple tests collectively verify</item>
/// </list>
/// </para>
/// </remarks>
public class InsightsIntentClassifierTests
{
    private const string PredictMatterCostJson = """
        {"path":"playbook","playbookId":"predict-matter-cost@v1","confidence":0.92,"reason":"Cost-prediction question on matter — clean playbook match."}
        """;

    private const string OpenEndedRagJson = """
        {"path":"rag","playbookId":null,"confidence":0.86,"reason":"Open-ended summary request; no registered playbook."}
        """;

    private const string LowConfidenceRagJson = """
        {"path":"rag","playbookId":null,"confidence":0.45,"reason":"Ambiguous intent."}
        """;

    private const string MalformedJson = "{ this is not valid json }";

    // ────────────────────────────────────────────────────────────────────────────────
    // (1) Classifier returns valid {path, playbookId?, confidence}
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Classify_PlaybookIntent_ReturnsPlaybookPathWithId()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PredictMatterCostJson);

        // Act
        var result = await sut.ClassifyAsync(
            "What will this matter cost to complete?",
            new IntentClassificationContext("matter", "tid-1"),
            CancellationToken.None);

        // Assert
        result.Path.Should().Be(IntentPath.Playbook);
        result.PlaybookId.Should().Be("predict-matter-cost@v1");
        result.Confidence.Should().BeApproximately(0.92, 0.001);
        result.BelowThreshold.Should().BeFalse();
        result.Reason.Should().NotBeEmpty();
        result.CacheHit.Should().BeFalse("first call is always a cache miss");
    }

    [Fact]
    public async Task Classify_RagIntent_ReturnsRagPathWithoutPlaybookId()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenEndedRagJson);

        // Act
        var result = await sut.ClassifyAsync(
            "Summarize the key risks in the latest deal documents",
            new IntentClassificationContext("matter", "tid-1"),
            CancellationToken.None);

        // Assert
        result.Path.Should().Be(IntentPath.Rag);
        result.PlaybookId.Should().BeNull("RAG path must not carry a playbookId");
        result.Confidence.Should().BeApproximately(0.86, 0.001);
        result.BelowThreshold.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (2) Below-threshold queries fall back to RAG
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Classify_LowConfidence_SetsBelowThresholdTrue()
    {
        // Arrange — threshold 0.7 (default); LLM returns 0.45
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LowConfidenceRagJson);

        // Act
        var result = await sut.ClassifyAsync(
            "ambiguous query",
            context: null,
            CancellationToken.None);

        // Assert
        result.BelowThreshold.Should().BeTrue(
            "0.45 is below the 0.7 default threshold — caller MUST fall back to RAG");
        result.Path.Should().Be(IntentPath.Rag,
            "FR-05 safety: ambiguous classifications go to RAG");
    }

    [Fact]
    public async Task Classify_PlaybookIntent_BelowThreshold_StillReportsPlaybookHint()
    {
        // Arrange — confidence just below threshold but classifier picked playbook
        var sut = CreateSut(out var openAiMock, threshold: 0.95);
        const string lowConfPlaybookJson = """
            {"path":"playbook","playbookId":"predict-matter-cost@v1","confidence":0.88,"reason":"Possible match."}
            """;
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowConfPlaybookJson);

        // Act
        var result = await sut.ClassifyAsync(
            "predict cost",
            new IntentClassificationContext("matter", null),
            CancellationToken.None);

        // Assert — observability: caller can see what the classifier WANTED to do
        result.Path.Should().Be(IntentPath.Playbook,
            "classifier's hint is preserved for observability/tuning");
        result.PlaybookId.Should().Be("predict-matter-cost@v1");
        result.BelowThreshold.Should().BeTrue("0.88 < 0.95 threshold");
        result.Confidence.Should().BeApproximately(0.88, 0.001);
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (3) Caching — second call within TTL returns CacheHit=true
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Classify_RepeatedQuery_SecondCallHitsCache()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PredictMatterCostJson);

        var query = "What will this matter cost?";
        var ctx = new IntentClassificationContext("matter", "tid-1");

        // Act — two identical calls
        var first = await sut.ClassifyAsync(query, ctx, CancellationToken.None);
        var second = await sut.ClassifyAsync(query, ctx, CancellationToken.None);

        // Assert
        first.CacheHit.Should().BeFalse();
        second.CacheHit.Should().BeTrue("identical query within TTL must come from cache");
        second.Path.Should().Be(first.Path);
        second.PlaybookId.Should().Be(first.PlaybookId);
        second.Confidence.Should().Be(first.Confidence);

        openAiMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "second call MUST be served from cache without a second LLM call");
    }

    [Fact]
    public async Task Classify_DifferentSubjectScheme_DoesNotShareCacheEntry()
    {
        // Arrange — same query, different scheme — must NOT share cache
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .SetupSequence(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PredictMatterCostJson)
            .ReturnsAsync(OpenEndedRagJson);

        // Act
        var matter = await sut.ClassifyAsync(
            "predict cost",
            new IntentClassificationContext("matter", null),
            CancellationToken.None);

        var project = await sut.ClassifyAsync(
            "predict cost",
            new IntentClassificationContext("project", null),
            CancellationToken.None);

        // Assert
        matter.Path.Should().Be(IntentPath.Playbook);
        project.Path.Should().Be(IntentPath.Rag,
            "different subject scheme must produce a different cache key");

        openAiMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "different subject schemes must trigger two LLM calls");
    }

    [Fact]
    public async Task Classify_NormalizedQuery_HitsCacheAcrossCasingDifferences()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenEndedRagJson);

        // Act
        var lower = await sut.ClassifyAsync("summarize the deal", context: null, CancellationToken.None);
        var upper = await sut.ClassifyAsync("  SUMMARIZE THE DEAL  ", context: null, CancellationToken.None);

        // Assert
        upper.CacheHit.Should().BeTrue("normalization (trim+lower) should hit the same cache entry");

        openAiMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (4) Defensive fallback — LLM throws / returns malformed JSON
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Classify_LlmThrows_ReturnsRagFallbackWithZeroConfidence()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        // Act
        var result = await sut.ClassifyAsync(
            "test query",
            context: null,
            CancellationToken.None);

        // Assert
        result.Path.Should().Be(IntentPath.Rag, "FR-05 safety: LLM failure → RAG fallback");
        result.Confidence.Should().Be(0.0);
        result.BelowThreshold.Should().BeTrue();
        result.PlaybookId.Should().BeNull();
        result.CacheHit.Should().BeFalse();
        result.Reason.Should().Contain("Classifier unavailable");
    }

    [Fact]
    public async Task Classify_MalformedLlmResponse_ReturnsRagFallback()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalformedJson);

        // Act
        var result = await sut.ClassifyAsync("query", context: null, CancellationToken.None);

        // Assert
        result.Path.Should().Be(IntentPath.Rag);
        result.Confidence.Should().Be(0.0);
        result.BelowThreshold.Should().BeTrue();
    }

    [Fact]
    public async Task Classify_LlmCancelled_PropagatesCancellation()
    {
        // Arrange
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act + Assert — cancellation MUST propagate, not become a fallback
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.ClassifyAsync("query", context: null, CancellationToken.None));
    }

    [Fact]
    public async Task Classify_FailedCall_IsNotCached()
    {
        // Arrange — first call fails, second call would succeed if not for cache
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .SetupSequence(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient failure"))
            .ReturnsAsync(PredictMatterCostJson);

        // Act
        var first = await sut.ClassifyAsync("query", context: null, CancellationToken.None);
        var second = await sut.ClassifyAsync("query", context: null, CancellationToken.None);

        // Assert
        first.Confidence.Should().Be(0.0, "first call fallback");
        second.Confidence.Should().BeApproximately(0.92, 0.001,
            "fallback MUST NOT be cached — second call retries the classifier");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (5) Validation
    // ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Classify_EmptyQuery_Throws(string query)
    {
        var sut = CreateSut(out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ClassifyAsync(query, context: null, CancellationToken.None));
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (6) Constructor null-checks
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullOpenAi_Throws()
    {
        var act = () => new InsightsIntentClassifier(
            openAi: null!,
            cache: new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 }),
            options: BuildOptionsMonitor(new InsightsIntentClassifierOptions()),
            logger: NullLogger<InsightsIntentClassifier>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new InsightsIntentClassifier(
            openAi: new Mock<IOpenAiClient>().Object,
            cache: null!,
            options: BuildOptionsMonitor(new InsightsIntentClassifierOptions()),
            logger: NullLogger<InsightsIntentClassifier>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new InsightsIntentClassifier(
            openAi: new Mock<IOpenAiClient>().Object,
            cache: new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 }),
            options: null!,
            logger: NullLogger<InsightsIntentClassifier>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (7) Latency budget (mock-based)
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Classify_HundredQueries_StayUnderMockLatencyBudget()
    {
        // Arrange — POML Step 7: sample 100 queries, verify p95 < 500ms.
        // In-process with a mocked LLM, "latency" here is dominated by the prompt build,
        // schema serialization, deserialization, and cache lookup — NOT a real LLM call.
        // Real LLM latency is verified post-deploy via smoke tests. The point of this test
        // is to detect a regression in the classifier's in-process overhead — if any of
        // those steps grows pathologically slow (e.g., a regex backtracking issue),
        // this test catches it.
        var sut = CreateSut(out var openAiMock);
        openAiMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PredictMatterCostJson);

        const int sampleCount = 100;
        var latencies = new List<long>(sampleCount);

        // Act — 100 unique queries (so each is a cache MISS — the worst case)
        for (int i = 0; i < sampleCount; i++)
        {
            var sw = Stopwatch.StartNew();
            await sut.ClassifyAsync(
                $"sample query number {i} about matter cost",
                new IntentClassificationContext("matter", $"tid-{i % 10}"),
                CancellationToken.None);
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        // Assert — p95 well under 500ms in-process budget.
        latencies.Sort();
        var p95 = latencies[(int)(sampleCount * 0.95)];
        p95.Should().BeLessThan(500,
            $"In-process classifier overhead must stay well under the FR-05 500ms p95 budget so the real LLM call has headroom. Observed p95={p95}ms (samples: min={latencies[0]}ms, max={latencies[^1]}ms).");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (8) Prompt structure smoke — ensures few-shot block present
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPrompt_IncludesFewShotExamples()
    {
        var prompt = InsightsIntentClassifier.BuildPrompt(
            "What will this matter cost?",
            new IntentClassificationContext("matter", "tid-1"));

        prompt.Should().Contain("predict-matter-cost@v1", "registered playbook must be named in the prompt");
        prompt.Should().Contain("playbook", "system instruction must explain the playbook route");
        prompt.Should().Contain("rag", "system instruction must explain the RAG route");
        prompt.Should().Contain("Examples:", "few-shot examples must be present");
        prompt.Should().Contain("Subject scheme: matter", "caller-supplied context must be present");
    }

    [Fact]
    public void BuildPrompt_NoSubject_OmitsSubjectLine()
    {
        var prompt = InsightsIntentClassifier.BuildPrompt(
            "What will this matter cost?",
            context: null);

        // The final prompt block (live query) must NOT contain a "Subject scheme: <something>"
        // entry — but examples can. Verify by checking the tail of the prompt.
        var lastQuestionStart = prompt.LastIndexOf("Q: ", StringComparison.Ordinal);
        var tail = prompt[lastQuestionStart..];
        tail.Should().NotContain("Subject scheme:");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // (9) Cache-key derivation
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeCacheKey_NormalizesQueryAndIncludesScheme()
    {
        var keyA = InsightsIntentClassifier.ComputeCacheKey("Foo Bar", "matter");
        var keyB = InsightsIntentClassifier.ComputeCacheKey("  foo bar  ", "MATTER");
        var keyC = InsightsIntentClassifier.ComputeCacheKey("Foo Bar", "project");

        keyA.Should().Be(keyB, "trim+lower normalization must produce the same key");
        keyA.Should().NotBe(keyC, "different scheme must produce a different key");
        keyA.Should().StartWith("insights.intent:", "key prefix must be stable for log scanning");
    }

    [Fact]
    public void ComputeCacheKey_LongQueryIsTruncated()
    {
        // Arrange — two queries identical up to the cap (1024 chars) but differing past it.
        var prefix = new string('x', InsightsIntentClassifier.CacheKeyMaxQueryLength);
        var queryA = prefix + "aaa";
        var queryB = prefix + "bbb";

        // Act
        var keyA = InsightsIntentClassifier.ComputeCacheKey(queryA, "matter");
        var keyB = InsightsIntentClassifier.ComputeCacheKey(queryB, "matter");

        // Assert
        keyA.Should().Be(keyB, "queries identical within the cap collide on cache key");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ────────────────────────────────────────────────────────────────────────────────

    private static InsightsIntentClassifier CreateSut(
        out Mock<IOpenAiClient> openAiMock,
        double threshold = 0.7,
        int cacheTtlMinutes = 15)
    {
        openAiMock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        return new InsightsIntentClassifier(
            openAi: openAiMock.Object,
            cache: new MemoryCache(new MemoryCacheOptions { SizeLimit = 256 }),
            options: BuildOptionsMonitor(new InsightsIntentClassifierOptions
            {
                ConfidenceThreshold = threshold,
                CacheTtlMinutes = cacheTtlMinutes
            }),
            logger: NullLogger<InsightsIntentClassifier>.Instance);
    }

    private static IOptionsMonitor<InsightsIntentClassifierOptions> BuildOptionsMonitor(
        InsightsIntentClassifierOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<InsightsIntentClassifierOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }
}
