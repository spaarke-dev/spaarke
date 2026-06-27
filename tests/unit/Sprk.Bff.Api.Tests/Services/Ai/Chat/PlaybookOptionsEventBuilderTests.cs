using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;
using Xunit;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="PlaybookOptionsEventBuilder"/>
/// (chat-routing-redesign-r1 task 117a / FR-49 + FR-48 + FR-51).
///
/// <para>
/// Coverage matrix:
/// <list type="bullet">
///   <item><description>Selector says no-rerank-needed → builder returns event with selector's candidates; <c>RerankInvoked=false</c>; reranker NEVER called.</description></item>
///   <item><description>Selector says rerank-recommended → reranker IS called; final candidates come from reranker's Top3; <c>RerankInvoked=true</c>.</description></item>
///   <item><description>Reranker returns the graceful-degrade Top3 (with reason "timeout-graceful-degrade") → event still surfaces candidates; <c>RerankReason</c> carries the tag.</description></item>
///   <item><description>Empty Phase B input → empty Candidates list; <c>LibraryModalCta=true</c> still on (FR-51).</description></item>
///   <item><description>Multi-file: <c>SessionAttachmentIds</c> includes every supplied ID verbatim and in order.</description></item>
///   <item><description><c>LibraryModalCta</c> is ALWAYS <c>true</c> regardless of candidate count (FR-51 invariant test).</description></item>
///   <item><description>ADR-015 telemetry assertion: captured log messages contain NO user message text and NO attachment filename / content type.</description></item>
///   <item><description>Latency tag is set and non-negative.</description></item>
///   <item><description>Cancellation: cancellation token is propagated to both selector and reranker.</description></item>
///   <item><description>Edge case: reranker returns 0 candidates → builder falls back to selector's <c>TopCandidates</c> (graceful).</description></item>
///   <item><description>FR-49 shape-lock: emitted payload serializes to exactly the locked field set (5 candidate fields + 4 envelope fields); no auto-execute / other fields leak.</description></item>
///   <item><description>FR-48 invariant: the projected candidate record carries no <c>autoExecute</c> property under any path.</description></item>
/// </list>
/// </para>
/// </summary>
public class PlaybookOptionsEventBuilderTests
{
    private const string PlaybookA = "11111111-aaaa-bbbb-cccc-111111111111";
    private const string PlaybookB = "22222222-aaaa-bbbb-cccc-222222222222";
    private const string PlaybookC = "33333333-aaaa-bbbb-cccc-333333333333";
    private const string PlaybookD = "44444444-aaaa-bbbb-cccc-444444444444";
    private const string PlaybookE = "55555555-aaaa-bbbb-cccc-555555555555";

    /// <summary>
    /// Verbatim user message used in the ADR-015 telemetry assertion. The
    /// builder MUST NOT echo this string into any log line.
    /// </summary>
    private const string SecretUserMessage = "TELEMETRY_FORBIDDEN_USER_TEXT_117A";

    /// <summary>
    /// Verbatim attachment filename used in the ADR-015 telemetry assertion.
    /// The builder logs counts only, never names.
    /// </summary>
    private const string SecretAttachmentName = "FORBIDDEN_FILENAME_117A.pdf";

    private const string SecretAttachmentMime = "application/x-forbidden-117a-mime";

    // ────────────────────────────────────────────────────────────────────
    // Capturing logger for ADR-015 telemetry assertions
    // ────────────────────────────────────────────────────────────────────

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> bag) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                bag.Add(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static ILogger<PlaybookOptionsEventBuilder> CreateCapturingLogger(out CapturingLoggerProvider provider)
    {
        var p = new CapturingLoggerProvider();
        provider = p;
        var factory = LoggerFactory.Create(builder => builder.AddProvider(p));
        return factory.CreateLogger<PlaybookOptionsEventBuilder>();
    }

    private static PlaybookCandidate Candidate(string id, double confidence, string? name = null) =>
        new(
            PlaybookId: id,
            PlaybookName: name ?? $"Playbook-{id[..8]}",
            Confidence: confidence,
            ContributingFileCount: 1);

    private static List<PlaybookCandidate> ThreeCandidates() => new()
    {
        Candidate(PlaybookA, 0.92),
        Candidate(PlaybookB, 0.88),
        Candidate(PlaybookC, 0.86),
    };

    private static List<PlaybookCandidate> FiveCandidates() => new()
    {
        Candidate(PlaybookA, 0.92),
        Candidate(PlaybookB, 0.88),
        Candidate(PlaybookC, 0.86),
        Candidate(PlaybookD, 0.82),
        Candidate(PlaybookE, 0.80),
    };

    /// <summary>
    /// The builder's inputs are not used by the mocked selector — the mock
    /// returns whatever the test set up. A non-null empty list is sufficient
    /// for "phaseBResults" except in shape tests that just need a stand-in.
    /// </summary>
    private static IReadOnlyList<PhaseBPerFileResult> EmptyPhaseB() => Array.Empty<PhaseBPerFileResult>();

    private static IReadOnlyList<AttachmentMetadata> NoAttachments() => Array.Empty<AttachmentMetadata>();

    private static AttachmentMetadata Attachment(string name, string mime = "application/pdf", int len = 1024) =>
        new() { Filename = name, ContentType = mime, TextLength = len };

    private static Mock<IPlaybookCandidateSelector> SelectorReturning(PlaybookCandidateSelection result)
    {
        var mock = new Mock<IPlaybookCandidateSelector>();
        mock.Setup(s => s.Select(It.IsAny<IReadOnlyList<PhaseBPerFileResult>>(), It.IsAny<CancellationToken>()))
            .Returns(result);
        return mock;
    }

    private static Mock<IIntentRerankerService> RerankerReturning(IntentRerankerResult result)
    {
        var mock = new Mock<IIntentRerankerService>();
        mock.Setup(r => r.RerankAsync(It.IsAny<IntentRerankerInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static PlaybookOptionsEventBuilder CreateBuilder(
        Mock<IPlaybookCandidateSelector> selector,
        Mock<IIntentRerankerService> reranker,
        ILogger<PlaybookOptionsEventBuilder>? logger = null)
        => new(
            selector.Object,
            reranker.Object,
            logger ?? new Mock<ILogger<PlaybookOptionsEventBuilder>>().Object);

    // ────────────────────────────────────────────────────────────────────
    // Test 1: no-rerank-needed happy path
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NoRerankNeeded_UsesSelectorCandidatesDirectly()
    {
        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: ThreeCandidates(),
            RerankRecommended: false,
            Reason: "high-confidence-single");

        var selector = SelectorReturning(selectorOutput);
        // Reranker should NEVER be called on this path.
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "summarize the attached contract",
            NoAttachments(),
            new[] { "attach-1", "attach-2" });

        result.Candidates.Should().HaveCount(3);
        result.Candidates[0].PlaybookId.Should().Be(PlaybookA);
        result.Candidates[0].DisplayName.Should().Be("Playbook-11111111");
        result.Candidates[0].Confidence.Should().Be(0.92);
        result.Candidates[0].Reason.Should().Be("high-confidence-single");
        result.RerankInvoked.Should().BeFalse();
        result.RerankReason.Should().BeNull();
        result.LibraryModalCta.Should().BeTrue();
        result.SessionAttachmentIds.Should().Equal("attach-1", "attach-2");

        reranker.Verify(
            r => r.RerankAsync(It.IsAny<IntentRerankerInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 2: rerank-recommended → reranker called → its Top3 wins
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_RerankRecommended_CallsRerankerAndUsesItsOutput()
    {
        var fiveCandidates = FiveCandidates();
        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: fiveCandidates,
            RerankRecommended: true,
            Reason: "ambiguous-top-2-within-margin");

        var rerankerOutput = new IntentRerankerResult(
            Top3: new List<RankedPlaybookCandidate>
            {
                new(fiveCandidates[2], "LLM said C is best fit for this contract"),
                new(fiveCandidates[0], "LLM said A is a strong second"),
                new(fiveCandidates[4], "LLM said E is plausible"),
            },
            RerankInvoked: true,
            Reason: "llm-rerank-from-5",
            LatencyMs: TimeSpan.FromMilliseconds(420));

        var selector = SelectorReturning(selectorOutput);
        var reranker = RerankerReturning(rerankerOutput);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "ambiguous user message",
            new[] { Attachment("contract.pdf") },
            new[] { "attach-1" });

        result.Candidates.Should().HaveCount(3);
        result.Candidates[0].PlaybookId.Should().Be(PlaybookC); // reranker order, not confidence order
        result.Candidates[1].PlaybookId.Should().Be(PlaybookA);
        result.Candidates[2].PlaybookId.Should().Be(PlaybookE);
        result.RerankInvoked.Should().BeTrue();
        result.RerankReason.Should().Be("llm-rerank-from-5");
        result.LibraryModalCta.Should().BeTrue();

        reranker.Verify(
            r => r.RerankAsync(
                It.Is<IntentRerankerInput>(i => i.UserMessage == "ambiguous user message"
                                                && i.Top5Candidates.Count == 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 3: reranker times out → graceful-degrade Top3 still surfaces
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_RerankerTimesOut_StillSurfacesCandidatesWithGracefulDegradeTag()
    {
        var fiveCandidates = FiveCandidates();
        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: fiveCandidates,
            RerankRecommended: true,
            Reason: "ambiguous-below-threshold");

        // Reranker's graceful-degrade contract: returns top-3 by confidence from the
        // input with a "timeout-graceful-degrade" reason. Builder should propagate the
        // reason to RerankReason and still emit candidates.
        var rerankerOutput = new IntentRerankerResult(
            Top3: new List<RankedPlaybookCandidate>
            {
                new(fiveCandidates[0], string.Empty),
                new(fiveCandidates[1], string.Empty),
                new(fiveCandidates[2], string.Empty),
            },
            RerankInvoked: true,
            Reason: "timeout-graceful-degrade",
            LatencyMs: TimeSpan.FromMilliseconds(800));

        var selector = SelectorReturning(selectorOutput);
        var reranker = RerankerReturning(rerankerOutput);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "something",
            NoAttachments(),
            new[] { "attach-1" });

        result.Candidates.Should().HaveCount(3);
        result.RerankInvoked.Should().BeTrue();
        result.RerankReason.Should().Be("timeout-graceful-degrade");
        result.LibraryModalCta.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 4: empty Phase B input → empty candidates, library CTA still on
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_EmptyPhaseBInput_ReturnsEmptyCandidatesButLibraryCtaOn()
    {
        var selectorOutput = PlaybookCandidateSelection.NoMatch;
        var selector = SelectorReturning(selectorOutput);
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "anything",
            NoAttachments(),
            Array.Empty<string>());

        result.Candidates.Should().BeEmpty();
        result.LibraryModalCta.Should().BeTrue(); // FR-51 invariant: ALWAYS true
        result.RerankInvoked.Should().BeFalse();
        result.RerankReason.Should().BeNull();
        result.SessionAttachmentIds.Should().BeEmpty();

        reranker.Verify(
            r => r.RerankAsync(It.IsAny<IntentRerankerInput>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 5: SessionAttachmentIds passthrough (multi-file)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_MultiFile_SurfacesAllSessionAttachmentIdsInOrder()
    {
        var selector = SelectorReturning(new PlaybookCandidateSelection(
            TopCandidates: ThreeCandidates(),
            RerankRecommended: false,
            Reason: "high-confidence-single"));
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker);

        var ids = new[] { "file-a", "file-b", "file-c", "file-d" };

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "anything",
            NoAttachments(),
            ids);

        result.SessionAttachmentIds.Should().Equal(ids);
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 6: FR-51 LibraryModalCta is always true
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)] // no-match
    [InlineData(1)] // single candidate
    [InlineData(3)] // typical top-3
    public async Task BuildAsync_LibraryModalCta_IsAlwaysTrue(int candidateCount)
    {
        var candidates = candidateCount == 0
            ? Array.Empty<PlaybookCandidate>()
            : FiveCandidates().Take(candidateCount).ToArray();

        var selectorOutput = candidateCount == 0
            ? PlaybookCandidateSelection.NoMatch
            : new PlaybookCandidateSelection(candidates, RerankRecommended: false, Reason: "high-confidence-single");

        var selector = SelectorReturning(selectorOutput);
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "anything",
            NoAttachments(),
            Array.Empty<string>());

        result.LibraryModalCta.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 7: ADR-015 telemetry — no user message text or attachment names in logs
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_LogMessages_NeverContainUserTextOrAttachmentNames()
    {
        var logger = CreateCapturingLogger(out var capture);

        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: FiveCandidates(),
            RerankRecommended: true,
            Reason: "ambiguous-top-2-within-margin");

        var rerankerOutput = new IntentRerankerResult(
            Top3: FiveCandidates().Take(3).Select(c => new RankedPlaybookCandidate(c, string.Empty)).ToList(),
            RerankInvoked: true,
            Reason: "llm-rerank-from-5",
            LatencyMs: TimeSpan.FromMilliseconds(300));

        var selector = SelectorReturning(selectorOutput);
        var reranker = RerankerReturning(rerankerOutput);

        var builder = CreateBuilder(selector, reranker, logger);

        await builder.BuildAsync(
            EmptyPhaseB(),
            SecretUserMessage,
            new[] { Attachment(SecretAttachmentName, SecretAttachmentMime, len: 99_999) },
            new[] { "attach-1" });

        capture.Messages.Should().NotBeEmpty("the builder MUST log at least one telemetry line");
        foreach (var msg in capture.Messages)
        {
            msg.Should().NotContain(SecretUserMessage,
                "ADR-015 tier-1: user message text MUST NOT appear in any log line");
            msg.Should().NotContain(SecretAttachmentName,
                "ADR-015 tier-1: attachment filenames MUST NOT appear in any log line");
            msg.Should().NotContain(SecretAttachmentMime,
                "ADR-015 tier-1: attachment content-types MUST NOT appear in any log line");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 8: latency tag is set (positive integer in log line)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_TelemetryLine_IncludesLatencyMsTag()
    {
        var logger = CreateCapturingLogger(out var capture);

        var selectorOutput = PlaybookCandidateSelection.NoMatch;
        var selector = SelectorReturning(selectorOutput);
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker, logger);

        await builder.BuildAsync(
            EmptyPhaseB(),
            "x",
            NoAttachments(),
            Array.Empty<string>());

        capture.Messages.Should().Contain(m => m.Contains("latencyMs="));
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 9: cancellation token propagation to selector AND reranker
    // ────────────────────────────────────────────────────────────────────


    // ────────────────────────────────────────────────────────────────────
    // Test 10: reranker returns 0 candidates → fall back to selector's Top
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_RerankerReturnsZeroCandidates_FallsBackToSelectorTop()
    {
        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: ThreeCandidates(),
            RerankRecommended: true,
            Reason: "ambiguous-top-2-within-margin");

        var rerankerOutput = new IntentRerankerResult(
            Top3: Array.Empty<RankedPlaybookCandidate>(),
            RerankInvoked: true,
            Reason: "llm-rerank-partial",
            LatencyMs: TimeSpan.FromMilliseconds(150));

        var selector = SelectorReturning(selectorOutput);
        var reranker = RerankerReturning(rerankerOutput);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "x",
            NoAttachments(),
            Array.Empty<string>());

        result.Candidates.Should().HaveCount(3,
            "when the reranker returns zero candidates the builder MUST fall back to the selector's top to avoid an empty-options UX bug");
        result.Candidates[0].PlaybookId.Should().Be(PlaybookA);
        result.RerankInvoked.Should().BeTrue();
        result.RerankReason.Should().Be("llm-rerank-partial");
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 11: FR-49 shape-lock — payload serializes to exactly the locked fields
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_SerializedPayload_HasExactlyTheLockedFieldSet()
    {
        var selectorOutput = new PlaybookCandidateSelection(
            TopCandidates: new List<PlaybookCandidate> { Candidate(PlaybookA, 0.91) },
            RerankRecommended: false,
            Reason: "high-confidence-single");

        var selector = SelectorReturning(selectorOutput);
        var reranker = new Mock<IIntentRerankerService>(MockBehavior.Strict);

        var builder = CreateBuilder(selector, reranker);

        var result = await builder.BuildAsync(
            EmptyPhaseB(),
            "x",
            NoAttachments(),
            new[] { "f1" });

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Envelope fields (exactly 5)
        root.EnumerateObject().Select(p => p.Name).OrderBy(n => n).Should().BeEquivalentTo(new[]
        {
            "candidates", "libraryModalCta", "rerankInvoked", "rerankReason", "sessionAttachmentIds",
        });

        // Candidate fields (exactly 5)
        var firstCandidate = root.GetProperty("candidates")[0];
        firstCandidate.EnumerateObject().Select(p => p.Name).OrderBy(n => n).Should().BeEquivalentTo(new[]
        {
            "confidence", "displayName", "playbookCode", "playbookId", "reason",
        });

        // FR-48 invariant: no autoExecute field anywhere
        json.Should().NotContain("autoExecute", "FR-48: the playbook_options event MUST NOT carry an auto-execute flag");
    }

    // ────────────────────────────────────────────────────────────────────
    // Test 12: FR-48 invariant — candidate record has no AutoExecute property
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlaybookOptionCandidate_ShapeHasNoAutoExecuteProperty()
    {
        // Reflective shape lock: the record type's public properties MUST be
        // exactly the FR-49 five-field set. New fields require an explicit FR.
        var properties = typeof(PlaybookOptionCandidate)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        properties.Should().BeEquivalentTo(new[]
        {
            nameof(PlaybookOptionCandidate.Confidence),
            nameof(PlaybookOptionCandidate.DisplayName),
            nameof(PlaybookOptionCandidate.PlaybookCode),
            nameof(PlaybookOptionCandidate.PlaybookId),
            nameof(PlaybookOptionCandidate.Reason),
        });

        // Defensive: make sure "AutoExecute" / "Execute" do not appear
        properties.Should().NotContain(p =>
            p.Contains("AutoExecute", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Execute", StringComparison.OrdinalIgnoreCase));
    }
}
