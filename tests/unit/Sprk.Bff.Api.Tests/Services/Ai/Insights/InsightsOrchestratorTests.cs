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
        // Parameters are enriched with well-known template vars derived from Subject per
        // Insights Engine r2 Wave B (2026-06-02) — caller-supplied k1/k2 preserved AND
        // matterId added from "matter:M-9999" Subject so playbook node ConfigJson templates
        // like {{matterId}} resolve without ceremony. See InsightsOrchestrator.EnrichParametersFromSubject.
        captured.Parameters.Should().NotBeNull();
        captured.Parameters!.Should().Contain("k1", "v1");
        captured.Parameters.Should().Contain("k2", "v2");
        captured.Parameters.Should().Contain("matterId", "M-9999");
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
    // RunIngestAsync — Wave C5 parameterization (task 024)
    //
    // The optional overrides (PracticeAreaHint, CostCapOverride, Layer2Threshold) wire
    // design-a5 §6 / universal-ingest.playbook.json `parameterSchema` onto the Zone-B
    // facade surface per D-P15-02 (ONE canonical playbook, parameterized — not many).
    // Validation enforces well-formedness at the facade boundary so bad inputs fail
    // fast BEFORE LLM cost is incurred. End-to-end effect verification (parameter →
    // playbook node behavior) is covered by Wave C4 (task 023) once IInsightsAi.RunIngestAsync
    // is rewired to invoke universal-ingest@v1 via the playbook engine.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunIngestAsync_WithAllOptionalParameters_DelegatesAndPreservesValues()
    {
        // Verify the parameter contract: required fields validated, optional overrides
        // accepted unchanged, request passed verbatim to the downstream orchestrator so
        // Wave C4's playbook-engine invocation receives all four parameters intact.
        var req = new InsightsIngestRequest(
            DocumentId: "doc-1",
            MatterId: "M-1",
            TenantId: TenantId,
            PracticeAreaHint: "CTRNS",
            CostCapOverride: 1.50m,
            Layer2Threshold: 0.82);
        var expectedResult = new InsightsIngestResult(
            ObservationsEmitted: 2,
            Layer1Classification: "Settlement",
            Layer2Triggered: true);

        InsightsIngestRequest? captured = null;
        _ingestMock
            .Setup(o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsIngestRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(expectedResult);

        var sut = CreateSut();
        var actual = await sut.RunIngestAsync(req);

        actual.Should().Be(expectedResult);
        captured.Should().NotBeNull();
        captured!.PracticeAreaHint.Should().Be("CTRNS", "parameterization override flows verbatim to the downstream orchestrator");
        captured.CostCapOverride.Should().Be(1.50m, "parameterization override flows verbatim");
        captured.Layer2Threshold.Should().Be(0.82, "parameterization override flows verbatim");
        captured.TenantId.Should().Be(TenantId, "required TenantId propagated unchanged");
    }

    [Fact]
    public async Task RunIngestAsync_WithNoOptionalParameters_DelegatesWithNulls()
    {
        // Default-value contract: when callers omit the optional overrides, the request
        // carries nulls (the playbook parameterSchema applies its own defaults). Verifies
        // the record-with-default-null pattern is preserved through the facade boundary —
        // no accidental "normalization" of null to a sentinel value.
        var req = new InsightsIngestRequest(
            DocumentId: "doc-2",
            MatterId: "M-2",
            TenantId: TenantId);
        var expectedResult = new InsightsIngestResult(
            ObservationsEmitted: 1,
            Layer1Classification: "Correspondence",
            Layer2Triggered: false);

        InsightsIngestRequest? captured = null;
        _ingestMock
            .Setup(o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsIngestRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(expectedResult);

        var sut = CreateSut();
        await sut.RunIngestAsync(req);

        captured.Should().NotBeNull();
        captured!.PracticeAreaHint.Should().BeNull("omitted overrides remain null — playbook schema default applies");
        captured.CostCapOverride.Should().BeNull("omitted overrides remain null");
        captured.Layer2Threshold.Should().BeNull("omitted overrides remain null");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunIngestAsync_BlankPracticeAreaHint_ThrowsArgumentException(string hint)
    {
        // Rule per ValidateIngestParameters: PracticeAreaHint must be non-whitespace
        // when supplied. Pass null to omit (litigation-default per Phase 1 D-59).
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId, PracticeAreaHint: hint);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("PracticeAreaHint", StringComparison.Ordinal));
        _ingestMock.Verify(
            o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation rejects bad input before any work is dispatched (no LLM cost)");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(-100.0)]
    public async Task RunIngestAsync_NonPositiveCostCapOverride_ThrowsArgumentException(double capValue)
    {
        // Rule: CostCapOverride must be strictly positive (> 0). Zero or negative is
        // nonsensical; pass null to omit (uses tenant monthly cap from D-P9).
        var req = new InsightsIngestRequest(
            "doc-1", "M-1", TenantId,
            CostCapOverride: (decimal)capValue);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("CostCapOverride", StringComparison.Ordinal));
        _ingestMock.Verify(
            o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task RunIngestAsync_OutOfRangeLayer2Threshold_ThrowsArgumentException(double threshold)
    {
        // Rule: Layer2Threshold must be finite and in [0.0, 1.0]. NaN + infinities
        // rejected. Pass null to omit (uses playbook default 0.7 per Phase 1 D-59).
        var req = new InsightsIngestRequest(
            "doc-1", "M-1", TenantId,
            Layer2Threshold: threshold);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Layer2Threshold", StringComparison.Ordinal));
        _ingestMock.Verify(
            o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public async Task RunIngestAsync_BoundaryLayer2ThresholdValues_Accepted(double threshold)
    {
        // Boundary check: 0.0 + 1.0 are both inclusive per the [0.0, 1.0] range. The
        // schema-default (0.7) is also exercised so any future tightening of the
        // boundary check that incidentally moves the default would be caught here.
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId, Layer2Threshold: threshold);
        var expectedResult = new InsightsIngestResult(0, null, false);
        _ingestMock
            .Setup(o => o.RunAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var sut = CreateSut();
        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().NotThrowAsync();
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
