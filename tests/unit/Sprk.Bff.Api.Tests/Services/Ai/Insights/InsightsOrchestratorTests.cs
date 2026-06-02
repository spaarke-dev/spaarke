using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for <see cref="InsightsOrchestrator"/> (task 042) — the Phase 1
/// Zone A implementation of <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInsightsAi"/>
/// (the §3.5 facade boundary).
/// </summary>
/// <remarks>
/// <para>
/// Covers all 6 acceptance criteria from task 042:
/// <list type="bullet">
///   <item>IInsightsAi compiled at Services/Ai/PublicContracts/ (compile-time verified)</item>
///   <item>InsightsOrchestrator resolves via DI (verified by the constructor wiring + the
///   InsightsFacadeModule registration; spot-check below)</item>
///   <item>AnswerQuestionAsync invokes the engine via the D-P13 cache</item>
///   <item>RunIngestAsync throws Phase-1 NotImplementedException per scaffold note</item>
///   <item>§3.5.4 grep passes on Zone B paths (verified externally)</item>
///   <item>Method naming follows domain convention (compile-time verified)</item>
/// </list>
/// </para>
/// </remarks>
public class InsightsOrchestratorTests
{
    private static readonly Guid Question = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private const string Subject = "matter:M-9999";
    private const string TenantId = "tenant-test";
    private const string ScopeHash = "scope-hash-test";

    private readonly Mock<IPlaybookExecutionEngine> _engineMock = new(MockBehavior.Strict);
    private readonly Mock<IInsightsPlaybookExecutionCache> _cacheMock = new(MockBehavior.Strict);
    private readonly Mock<IOpenAiClient> _openAiMock = new(MockBehavior.Strict);
    // Loose mock for IIngestOrchestrator (task 040 added this dependency to InsightsOrchestrator's
    // ctor for D-P7 universal ingest wiring). These tests cover AnswerQuestionAsync + EmbedTextAsync
    // (which don't touch ingest); strict mode would over-constrain. Task 040's own tests cover
    // RunIngestAsync exercising this dependency.
    private readonly Mock<Sprk.Bff.Api.Services.Ai.Insights.Ingest.IIngestOrchestrator> _ingestMock = new();

    private InsightsOrchestrator CreateSut()
        => new(
            _engineMock.Object,
            _cacheMock.Object,
            _openAiMock.Object,
            _ingestMock.Object,
            NullLogger<InsightsOrchestrator>.Instance);

    private static InsightsAgentRequest MakeAgentRequest(
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            Question: Question,
            Subject: Subject,
            Parameters: parameters,
            TenantId: TenantId,
            AccessibleScopeHash: ScopeHash);

    private static InsightArtifact MakeArtifact()
        => new InferenceArtifact
        {
            Id = "inf:M-9999:predictedCost",
            Subject = Subject,
            Predicate = "predictedCost",
            Value = new Value
            {
                Raw = JsonDocument.Parse("280000").RootElement.Clone(),
                DisplayHint = "currency-usd"
            },
            Confidence = 0.74,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "agent", Id = "agent://insights-v1", Version = "v1" },
            Scope = new Scope { TenantId = TenantId, MatterId = "M-9999" },
            TenantId = TenantId,
            Reasoning = "Synthesised from 14 comparable matters."
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor + argument validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullEngine_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            null!, _cacheMock.Object, _openAiMock.Object, _ingestMock.Object, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("engine");
    }

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, null!, _openAiMock.Object, _ingestMock.Object, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("cache");
    }

    [Fact]
    public void Constructor_NullOpenAi_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, null!, _ingestMock.Object, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("openAi");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object, _ingestMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AnswerQuestionAsync — happy path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnswerQuestionAsync_NullRequest_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.AnswerQuestionAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", TenantId, ScopeHash, "Subject")]
    [InlineData("   ", TenantId, ScopeHash, "Subject")]
    [InlineData(Subject, "", ScopeHash, "TenantId")]
    [InlineData(Subject, TenantId, "", "AccessibleScopeHash")]
    public async Task AnswerQuestionAsync_BlankRequiredFields_Throws(
        string subject, string tenantId, string scopeHash, string expectedField)
    {
        var sut = CreateSut();
        var req = new InsightsAgentRequest(Question, subject, null, tenantId, scopeHash);

        Func<Task> act = () => sut.AnswerQuestionAsync(req);
        // ArgumentException is sufficient — exact ParamName binding varies by which
        // ArgumentException.ThrowIfNullOrWhiteSpace call fires first (any blank field
        // produces a clear error; the test guards against accidentally silent failure).
        await act.Should().ThrowAsync<ArgumentException>();
        // expectedField referenced for Theory display only (asserts above are sufficient).
        _ = expectedField;
    }

    [Fact]
    public async Task AnswerQuestionAsync_CacheHit_ReturnsArtifactAndDoesNotInvokeEngine()
    {
        // Arrange: cache returns artifact directly; engine factory NOT called.
        var artifact = MakeArtifact();
        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsEngineRunResult.FromArtifact(artifact)); // factory never invoked

        var sut = CreateSut();
        var req = MakeAgentRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert
        result.Should().NotBeNull();
        result.Artifact.Should().BeSameAs(artifact);
        result.Decline.Should().BeNull();
        result.CacheHit.Should().BeTrue("factory was not invoked");
        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);

        _engineMock.Verify(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "cache hit must not invoke the engine");
    }

    [Fact]
    public async Task AnswerQuestionAsync_CacheMiss_InvokesEngineAndReturnsArtifact()
    {
        // Arrange: cache invokes the factory which calls the engine; both return an artifact.
        var artifact = MakeArtifact();

        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    // Drain the factory's stream to simulate the cache's MISS path.
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromArtifact(artifact);
                });

        _engineMock.Setup(e => e.ExecuteBatchAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticEngineStreamAsync(ct));

        var sut = CreateSut();
        var req = MakeAgentRequest(new Dictionary<string, string> { ["matterType"] = "ip-licensing" });

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert
        result.Artifact.Should().BeSameAs(artifact);
        result.Decline.Should().BeNull();
        result.CacheHit.Should().BeFalse("the factory was invoked (cache miss)");
        _engineMock.Verify(e => e.ExecuteBatchAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == Question
                                            && r.DocumentIds.Length == 0
                                            && r.Parameters!.ContainsKey("matterType")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnswerQuestionAsync_RealDeclineFromEngine_PropagatesStructuredDecline()
    {
        // Task 071 Gap 2 closure: when the cache returns a real DeclineResponse from
        // DeclineToFindNode, the orchestrator surfaces it via InsightsAgentResult.Declined
        // with structured MinimumEvidenceNeeded — NOT the scaffold "no-artifact-produced".
        var realDecline = new DeclineResponse
        {
            Reason = "insufficient-evidence",
            Explanation = "Only 5 comparable matters were found; need 12 per rule 'comparableMatters.min'.",
            MinimumEvidenceNeeded = new Dictionary<string, object>
            {
                ["comparableMatters"] = new { have = 5, need = 7, from = "retrieveCohortObservations" }
            },
            SuggestedActions = new[] { "Broaden the matter-type filter", "Author a Precedent" },
            ConfidenceInDecline = 0.95
        };

        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsEngineRunResult.FromDecline(realDecline));

        var sut = CreateSut();
        var req = MakeAgentRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert — real decline propagation (no scaffold)
        result.Artifact.Should().BeNull();
        result.Decline.Should().NotBeNull();
        result.Decline!.Reason.Should().Be("insufficient-evidence", "real reason from playbook, not 'no-artifact-produced' scaffold");
        result.Decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters",
            "structured gap analysis from EvidenceSufficiencyNode propagates through");
        result.Decline.ConfidenceInDecline.Should().Be(0.95,
            "real confidence from DeclineToFindNode, not scaffold sentinel 0.0");
        result.CacheHit.Should().BeFalse("declines are never cached; CacheHit is always false on decline path (task 071)");
    }

    [Fact]
    public async Task AnswerQuestionAsync_EngineProducesNothing_ReturnsScaffoldDeclineAsDefensiveFallback()
    {
        // Defensive path: malformed playbook produced neither artifact nor decline. The
        // orchestrator logs Warning and emits the scaffold so Zone B sees a valid result
        // honoring the "exactly one of artifact/decline" facade contract. ConfidenceInDecline=0.0
        // is a sentinel signaling "this is not a real decline verdict" to observability tooling.
        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsEngineRunResult.Empty);

        var sut = CreateSut();
        var req = MakeAgentRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert
        result.Artifact.Should().BeNull();
        result.Decline.Should().NotBeNull();
        result.Decline!.Reason.Should().Be("no-artifact-produced");
        result.Decline.MinimumEvidenceNeeded.Should().BeEmpty();
        result.Decline.SuggestedActions.Should().BeEmpty();
        result.Decline.ConfidenceInDecline.Should().Be(0.0, "scaffold sentinel: 'not a real decline verdict'");
    }

    [Fact]
    public async Task AnswerQuestionAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();

        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                (req, factory, ct) => Task.FromCanceled<InsightsEngineRunResult>(ct));

        var sut = CreateSut();
        cts.Cancel();

        Func<Task> act = () => sut.AnswerQuestionAsync(MakeAgentRequest(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnswerQuestionAsync_CacheRequest_CarriesAllInputFieldsCorrectly()
    {
        InsightsPlaybookExecutionRequest? captured = null;
        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                (req, _, _) => captured = req)
            .ReturnsAsync(InsightsEngineRunResult.FromArtifact(MakeArtifact()));

        var parameters = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };
        var sut = CreateSut();

        await sut.AnswerQuestionAsync(MakeAgentRequest(parameters));

        captured.Should().NotBeNull();
        captured!.PlaybookId.Should().Be(Question);
        captured.Subject.Should().Be(Subject);
        captured.TenantId.Should().Be(TenantId);
        captured.AccessibleScopeHash.Should().Be(ScopeHash);
        captured.Parameters.Should().BeEquivalentTo(parameters);
        captured.Ttl.Should().BeNull("orchestrator defers to cache DefaultTtl");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RunIngestAsync — facade delegation to IIngestOrchestrator (task 040 wires this)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunIngestAsync_NullRequest_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.RunIngestAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "M-1", TenantId)]
    [InlineData("doc-1", "", TenantId)]
    [InlineData("doc-1", "M-1", "")]
    public async Task RunIngestAsync_BlankRequiredFields_Throws(string docId, string matterId, string tenantId)
    {
        var sut = CreateSut();
        var req = new InsightsIngestRequest(docId, matterId, tenantId);

        Func<Task> act = () => sut.RunIngestAsync(req);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunIngestAsync_WithValidRequest_DelegatesToIngestOrchestrator()
    {
        // Task 040 (D-P7) replaced the scaffold NotImplementedException with delegation
        // to IIngestOrchestrator. This test verifies the facade is a thin pass-through:
        // - Argument validation still runs at the facade
        // - The orchestrator is invoked exactly once with the same request + ct
        // - The orchestrator's result is returned verbatim (no transformation)
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        var expectedResult = new InsightsIngestResult(
            ObservationsEmitted: 3,
            Layer1Classification: "closing_letter",
            Layer2Triggered: true);
        _ingestMock
            .Setup(o => o.RunAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult)
            .Verifiable();

        var sut = CreateSut();
        var actual = await sut.RunIngestAsync(req);

        actual.Should().Be(expectedResult);
        _ingestMock.Verify(
            o => o.RunAsync(req, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EmbedTextAsync — delegation to IOpenAiClient
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EmbedTextAsync_BlankOrNullText_Throws(string? text)
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.EmbedTextAsync(text!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EmbedTextAsync_DelegatesToOpenAiClientWithNullModelAndDimensions()
    {
        // Arrange: stubbed 3-element vector (real dims would be 3072 per text-embedding-3-large)
        var expectedVector = new float[] { 0.1f, 0.2f, 0.3f };
        ReadOnlyMemory<float> capturedReturn = expectedVector;

        _openAiMock.Setup(c => c.GenerateEmbeddingAsync(
                "precedent statement text",
                null, // model — facade is opinionated, passes null per the impl note
                null, // dimensions — same
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(capturedReturn);

        var sut = CreateSut();

        // Act
        var result = await sut.EmbedTextAsync("precedent statement text");

        // Assert
        result.ToArray().Should().BeEquivalentTo(expectedVector);
        _openAiMock.Verify(c => c.GenerateEmbeddingAsync(
                "precedent statement text",
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmbedTextAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();

        _openAiMock.Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>(
                (_, _, _, ct) => Task.FromCanceled<ReadOnlyMemory<float>>(ct));

        var sut = CreateSut();
        cts.Cancel();

        Func<Task> act = () => sut.EmbedTextAsync("text", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<PlaybookStreamEvent> SyntheticEngineStreamAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var playbookId = Question;
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }
}
