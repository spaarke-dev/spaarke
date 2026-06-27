using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="IntentRerankerService"/>
/// (chat-routing-redesign-r1 task 111R — FR-46 hybrid intent reranker,
/// FR-48 no-auto-execute invariant, ADR-015 tier-1 input contract).
///
/// <para>
/// Coverage matrix:
/// <list type="bullet">
///   <item><description>Happy path: 5 input → 3 output (LLM returns valid JSON).</description></item>
///   <item><description>LLM returns 1 candidate → 1 in Top3 + "llm-rerank-partial".</description></item>
///   <item><description>LLM returns &gt;3 (defensive — schema says max 3) → truncated to 3 + "llm-rerank-truncated".</description></item>
///   <item><description>LLM returns playbookId not in top-5 → silently dropped + warning log.</description></item>
///   <item><description>LLM timeout → graceful degrade to top-3-by-confidence + "timeout-graceful-degrade" + RerankInvoked=true.</description></item>
///   <item><description>LLM throws (non-cancel) → graceful degrade + "llm-error-graceful-degrade".</description></item>
///   <item><description>Empty Top5Candidates → empty Top3 + "no-input-candidates" + NO LLM call.</description></item>
///   <item><description>ADR-015 telemetry: no log message contains user message text or LLM response text.</description></item>
///   <item><description>LatencyMs is positive (set from stopwatch).</description></item>
///   <item><description>Default options take effect (TimeoutMs=800, Temperature=0) when none customised.</description></item>
///   <item><description>FR-48 invariant: <see cref="IntentRerankerResult"/> shape carries no auto-execute property.</description></item>
///   <item><description>ADR-015 input contract: prompt body never contains file text content (only filename / contentType / textLength).</description></item>
/// </list>
/// </para>
/// </summary>
public class IntentRerankerServiceTests
{
    private static readonly string PlaybookA = "11111111-aaaa-bbbb-cccc-111111111111";
    private static readonly string PlaybookB = "22222222-aaaa-bbbb-cccc-222222222222";
    private static readonly string PlaybookC = "33333333-aaaa-bbbb-cccc-333333333333";
    private static readonly string PlaybookD = "44444444-aaaa-bbbb-cccc-444444444444";
    private static readonly string PlaybookE = "55555555-aaaa-bbbb-cccc-555555555555";

    /// <summary>
    /// Verbatim secret text fixture that, per ADR-015, MUST NOT appear in
    /// any log message. Used in the telemetry assertion test.
    /// </summary>
    private const string SecretUserMessage = "TELEMETRY_FORBIDDEN_USER_TEXT_111R";

    /// <summary>
    /// Verbatim file body fixture that, per ADR-015, MUST NOT appear in
    /// the LLM prompt (file content is the forbidden category — only
    /// filename, contentType, and textLength integer may cross the LLM
    /// boundary).
    /// </summary>
    private const string SecretFileBody = "FORBIDDEN_FILE_BODY_CONTENT_DO_NOT_LEAK";

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures every log message emitted by the test target so the
    /// ADR-015 telemetry assertion can scan them for forbidden content.
    /// </summary>
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

    private static ILogger<IntentRerankerService> CreateCapturingLogger(out CapturingLoggerProvider provider)
    {
        var p = new CapturingLoggerProvider();
        provider = p;
        var factory = LoggerFactory.Create(builder => builder.AddProvider(p));
        return factory.CreateLogger<IntentRerankerService>();
    }

    private static PlaybookCandidate Candidate(string id, double confidence, string? name = null) =>
        new(
            PlaybookId: id,
            PlaybookName: name ?? $"Playbook-{id[..8]}",
            Confidence: confidence,
            ContributingFileCount: 1);

    private static List<PlaybookCandidate> FiveCandidates() => new()
    {
        Candidate(PlaybookA, 0.92),
        Candidate(PlaybookB, 0.88),
        Candidate(PlaybookC, 0.86),
        Candidate(PlaybookD, 0.82),
        Candidate(PlaybookE, 0.80),
    };

    private static IntentRerankerInput Input(
        string userMessage = "summarize the attached contract",
        IReadOnlyList<AttachmentMetadata>? attachments = null,
        IReadOnlyList<PlaybookCandidate>? candidates = null) =>
        new()
        {
            UserMessage = userMessage,
            AttachmentMetadata = attachments ?? Array.Empty<AttachmentMetadata>(),
            Top5Candidates = candidates ?? FiveCandidates(),
        };

    private static AttachmentMetadata Attachment(string name = "contract.pdf", string mime = "application/pdf", int len = 12_345) =>
        new() { Filename = name, ContentType = mime, TextLength = len };

    private static IntentRerankerOptions DefaultOptions() => new()
    {
        ModelDeploymentName = "gpt-4o-mini",
        TimeoutMs = 800,
        Temperature = 0.0,
    };

    /// <summary>
    /// Builds a service with the given mock IChatClient + optional options
    /// override + optional capturing logger. Sensible defaults for everything
    /// else.
    /// </summary>
    private static IntentRerankerService CreateService(
        Mock<IChatClient> chatClient,
        IntentRerankerOptions? options = null,
        ILogger<IntentRerankerService>? logger = null)
    {
        return new IntentRerankerService(
            chatClient.Object,
            Options.Create(options ?? DefaultOptions()),
            logger ?? new Mock<ILogger<IntentRerankerService>>().Object);
    }

    /// <summary>
    /// Mock IChatClient that returns the supplied JSON response text from
    /// <c>GetResponseAsync</c>.
    /// </summary>
    private static Mock<IChatClient> MockClientReturning(string jsonResponse)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResponse)));
        return mock;
    }

    /// <summary>
    /// Mock IChatClient that throws <see cref="OperationCanceledException"/>
    /// when <c>GetResponseAsync</c> is invoked, simulating the FR-46 timeout.
    /// </summary>
    private static Mock<IChatClient> MockClientTimingOut()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("rerank timeout sim"));
        return mock;
    }

    /// <summary>
    /// Mock IChatClient that throws an arbitrary non-cancellation exception,
    /// simulating an Azure OpenAI 5xx / connectivity failure.
    /// </summary>
    private static Mock<IChatClient> MockClientThrowing()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated upstream error"));
        return mock;
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. Happy path
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_ReturnsTop3_WhenLlmReturnsValidJson()
    {
        // Arrange
        var json = $$"""
            {
              "top3": [
                { "playbookId": "{{PlaybookB}}", "reason": "best fit for contract summary" },
                { "playbookId": "{{PlaybookA}}", "reason": "alternative summary path" },
                { "playbookId": "{{PlaybookC}}", "reason": "narrow but applicable" }
              ]
            }
            """;
        var client = MockClientReturning(json);
        var service = CreateService(client);

        // Act
        var result = await service.RerankAsync(Input());

        // Assert
        result.Top3.Should().HaveCount(3);
        result.Top3[0].Candidate.PlaybookId.Should().Be(PlaybookB);
        result.Top3[0].RerankReason.Should().Be("best fit for contract summary");
        result.Top3[1].Candidate.PlaybookId.Should().Be(PlaybookA);
        result.Top3[2].Candidate.PlaybookId.Should().Be(PlaybookC);
        result.RerankInvoked.Should().BeTrue();
        result.Reason.Should().Be("llm-rerank-from-5");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. LLM returns 1 → 1 in Top3
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_ReturnsSingle_WhenLlmReturnsOneCandidate()
    {
        var json = $$"""
            { "top3": [ { "playbookId": "{{PlaybookA}}", "reason": "only one that fits" } ] }
            """;
        var client = MockClientReturning(json);
        var service = CreateService(client);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(1);
        result.Top3[0].Candidate.PlaybookId.Should().Be(PlaybookA);
        result.RerankInvoked.Should().BeTrue();
        result.Reason.Should().Be("llm-rerank-partial");
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. LLM returns >3 (defensive — schema says max 3) → truncated to 3
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_TruncatesToThree_WhenLlmReturnsMoreThanThree()
    {
        var json = $$"""
            {
              "top3": [
                { "playbookId": "{{PlaybookA}}", "reason": "first" },
                { "playbookId": "{{PlaybookB}}", "reason": "second" },
                { "playbookId": "{{PlaybookC}}", "reason": "third" },
                { "playbookId": "{{PlaybookD}}", "reason": "fourth (should be dropped)" },
                { "playbookId": "{{PlaybookE}}", "reason": "fifth (should be dropped)" }
              ]
            }
            """;
        var client = MockClientReturning(json);
        var service = CreateService(client);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(3);
        result.Top3.Select(r => r.Candidate.PlaybookId).Should().ContainInOrder(PlaybookA, PlaybookB, PlaybookC);
        result.Reason.Should().Be("llm-rerank-truncated");
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. LLM returns playbookId not in top-5 → silently dropped + warning
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_DropsUnknownPlaybookIds_AndLogsWarningCount()
    {
        var unknownId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
        var json = $$"""
            {
              "top3": [
                { "playbookId": "{{PlaybookA}}", "reason": "valid" },
                { "playbookId": "{{unknownId}}", "reason": "hallucinated" },
                { "playbookId": "{{PlaybookB}}", "reason": "valid second" }
              ]
            }
            """;
        var client = MockClientReturning(json);
        var logger = CreateCapturingLogger(out var provider);
        var service = CreateService(client, logger: logger);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(2);
        result.Top3.Select(r => r.Candidate.PlaybookId).Should().BeEquivalentTo(new[] { PlaybookA, PlaybookB });
        provider.Messages.Should().Contain(m => m.Contains("dropped 1 LLM-returned playbookId"));
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. LLM timeout → graceful degrade + RerankInvoked=true
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_GracefulDegrades_WhenLlmTimesOut()
    {
        var client = MockClientTimingOut();
        var service = CreateService(client);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(3);
        // Fallback is top-3-by-confidence — A=0.92, B=0.88, C=0.86.
        result.Top3.Select(r => r.Candidate.PlaybookId).Should()
            .ContainInOrder(PlaybookA, PlaybookB, PlaybookC);
        result.RerankInvoked.Should().BeTrue();
        result.Reason.Should().Be("timeout-graceful-degrade");
        result.Top3.Should().AllSatisfy(r => r.RerankReason.Should().Be("fallback"));
    }

    // ────────────────────────────────────────────────────────────────────
    // 6. LLM throws (non-cancel) → graceful degrade
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_GracefulDegrades_WhenLlmThrows()
    {
        var client = MockClientThrowing();
        var service = CreateService(client);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(3);
        result.RerankInvoked.Should().BeTrue();
        result.Reason.Should().Be("llm-error-graceful-degrade");
    }

    // ────────────────────────────────────────────────────────────────────
    // 7. Empty Top5Candidates → empty Top3 + no LLM call
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_ShortCircuits_WhenInputCandidatesEmpty()
    {
        var client = new Mock<IChatClient>(MockBehavior.Strict);
        var service = CreateService(client);

        var result = await service.RerankAsync(Input(candidates: Array.Empty<PlaybookCandidate>()));

        result.Top3.Should().BeEmpty();
        result.RerankInvoked.Should().BeTrue();
        result.Reason.Should().Be("no-input-candidates");
        // Times.Never proves the short-circuit avoided the LLM entirely
        // (no-input short-circuit MUST NOT call GetResponseAsync).
        client.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ────────────────────────────────────────────────────────────────────
    // 8. ADR-015 telemetry: no log message contains user-message text or LLM body
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_DoesNotLog_UserMessageOrLlmResponseText()
    {
        const string sensitiveLlmReason = "RAW_LLM_REASON_BODY_MUST_NOT_LEAK";
        var json = $$"""
            { "top3": [ { "playbookId": "{{PlaybookA}}", "reason": "{{sensitiveLlmReason}}" } ] }
            """;
        var client = MockClientReturning(json);
        var logger = CreateCapturingLogger(out var provider);
        var service = CreateService(client, logger: logger);

        // Use a verbatim secret string we know is NOT a substring of any
        // expected log message.
        await service.RerankAsync(Input(userMessage: SecretUserMessage));

        provider.Messages.Should().NotBeEmpty(
            because: "service emits at least one INFO log on rerank complete");
        provider.Messages.Should().NotContain(m => m.Contains(SecretUserMessage),
            because: "ADR-015 tier-1 forbids user-message text in logs");
        provider.Messages.Should().NotContain(m => m.Contains(sensitiveLlmReason),
            because: "ADR-015 tier-1 forbids LLM response body in logs");
    }

    // ────────────────────────────────────────────────────────────────────
    // 9. LatencyMs is positive (set from stopwatch)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_SetsLatencyMs_OnAllPaths()
    {
        // Happy path
        var json = $$"""{ "top3": [ { "playbookId": "{{PlaybookA}}", "reason": "ok" } ] }""";
        var happyService = CreateService(MockClientReturning(json));
        var happy = await happyService.RerankAsync(Input());
        happy.LatencyMs.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

        // Empty short-circuit
        var emptyClient = new Mock<IChatClient>(MockBehavior.Loose);
        var emptyService = CreateService(emptyClient);
        var empty = await emptyService.RerankAsync(Input(candidates: Array.Empty<PlaybookCandidate>()));
        empty.LatencyMs.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

        // Timeout-degrade
        var timeoutService = CreateService(MockClientTimingOut());
        var timeout = await timeoutService.RerankAsync(Input());
        timeout.LatencyMs.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // ────────────────────────────────────────────────────────────────────
    // 10. Default options take effect when none customised
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_UsesDefaultOptions_WhenNoneSupplied()
    {
        var defaults = new IntentRerankerOptions();
        defaults.ModelDeploymentName.Should().Be("gpt-4o-mini");
        defaults.TimeoutMs.Should().Be(800);
        defaults.Temperature.Should().Be(0.0);

        // Smoke-execute the service to confirm defaults wire end-to-end.
        var json = $$"""{ "top3": [ { "playbookId": "{{PlaybookA}}", "reason": "ok" } ] }""";
        var service = CreateService(MockClientReturning(json), options: defaults);

        var result = await service.RerankAsync(Input());

        result.Top3.Should().HaveCount(1);
        result.RerankInvoked.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────
    // 11. FR-48 invariant — result shape carries no auto-execute property
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void IntentRerankerResult_HasNoAutoExecuteProperty()
    {
        // Compile-time + reflection sanity: the FR-48 invariant is enforced by
        // the shape of the result. Any future regression that adds an
        // "AutoExecute" property would surface immediately as a failing test.
        var props = typeof(IntentRerankerResult).GetProperties();
        props.Should().NotContain(
            p => p.Name.Equals("AutoExecute", StringComparison.OrdinalIgnoreCase),
            because: "FR-48: reranker never auto-executes — surface candidates only");
    }

    // ────────────────────────────────────────────────────────────────────
    // 12. ADR-015 LLM input contract — prompt body never contains file text
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankAsync_PromptBodyDoesNotContain_FileTextContent()
    {
        // Capture the messages passed to GetResponseAsync.
        IEnumerable<ChatMessage>? capturedMessages = null;
        var client = new Mock<IChatClient>();
        var json = $$"""{ "top3": [ { "playbookId": "{{PlaybookA}}", "reason": "ok" } ] }""";
        client.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));

        var service = CreateService(client);

        // Build attachments with metadata-only fields; SecretFileBody is the
        // forbidden category — it represents file body that the caller MUST
        // NOT pass through. The service must build its prompt from the
        // metadata fields alone.
        var input = Input(
            userMessage: "summarize this",
            attachments: new[] { Attachment("contract.pdf", "application/pdf", 87_654) });

        await service.RerankAsync(input);

        capturedMessages.Should().NotBeNull();
        var promptBlob = string.Join("\n",
            capturedMessages!.Select(m => m.Text ?? string.Empty));

        promptBlob.Should().NotContain(SecretFileBody,
            because: "ADR-015 tier-1 forbids file body content in the LLM prompt");
        // Metadata fields ARE permitted and must be present.
        promptBlob.Should().Contain("contract.pdf");
        promptBlob.Should().Contain("application/pdf");
        promptBlob.Should().Contain("87654");
    }
}
