using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for <see cref="InsightsOrchestrator"/> — the Phase 1.5 Zone A implementation of
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInsightsAi"/> (the §3.5 facade boundary).
/// </summary>
/// <remarks>
/// <para>
/// <b>Post Wave C-G4 (task 022)</b>: the legacy <c>IIngestOrchestrator</c> code path has been
/// retired. <see cref="InsightsOrchestrator.RunIngestAsync"/> invokes the universal-ingest@v1
/// JPS playbook directly via <see cref="IPlaybookOrchestrationService.ExecuteAppOnlyAsync"/>;
/// the per-env playbook Guid is resolved through
/// <see cref="InsightsPlaybookNameMapOptions"/>. Tests covering the legacy fallback
/// (RunFailed / PlaybookThrows / PlaybookPathDisabled / PlaybookGuidEmpty) have been deleted.
/// </para>
/// </remarks>
public class InsightsOrchestratorTests
{
    private static readonly Guid Question = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private const string Subject = "matter:M-9999";
    private const string TenantId = "tenant-test";
    private const string ScopeHash = "scope-hash-test";

    /// <summary>
    /// Canonical name key used by <see cref="InsightsOrchestrator"/> to resolve the
    /// universal-ingest@v1 playbook through <see cref="InsightsPlaybookNameMapOptions"/>.
    /// Matches the private constant in <c>InsightsOrchestrator</c>; kept in sync by tests.
    /// </summary>
    private const string UniversalIngestPlaybookCanonicalName = "universal_ingest_v1";

    private static readonly Guid UniversalIngestPlaybookId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IPlaybookExecutionEngine> _engineMock = new(MockBehavior.Strict);
    private readonly Mock<IInsightsPlaybookExecutionCache> _cacheMock = new(MockBehavior.Strict);
    private readonly Mock<IOpenAiClient> _openAiMock = new(MockBehavior.Strict);
    private readonly Mock<IPlaybookOrchestrationService> _playbookOrchestrationMock = new();
    private readonly Mock<IIngestDocumentSource> _ingestDocumentSourceMock = new();
    // Default name-map registers universal-ingest@v1 → UniversalIngestPlaybookId.
    // Tests can substitute an empty map to exercise the "unconfigured" failure path.
    private readonly TestOptionsMonitor<InsightsPlaybookNameMapOptions> _playbookNameMap =
        new(new InsightsPlaybookNameMapOptions
        {
            Map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                [UniversalIngestPlaybookCanonicalName] = UniversalIngestPlaybookId
            }
        });

    private InsightsOrchestrator CreateSut()
        => new(
            _engineMock.Object,
            _cacheMock.Object,
            _openAiMock.Object,
            _playbookOrchestrationMock.Object,
            _ingestDocumentSourceMock.Object,
            _playbookNameMap,
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
            null!, _cacheMock.Object, _openAiMock.Object,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            _playbookNameMap, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("engine");
    }

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, null!, _openAiMock.Object,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            _playbookNameMap, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("cache");
    }

    [Fact]
    public void Constructor_NullOpenAi_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, null!,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            _playbookNameMap, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("openAi");
    }

    [Fact]
    public void Constructor_NullPlaybookOrchestration_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object,
            null!, _ingestDocumentSourceMock.Object,
            _playbookNameMap, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookOrchestration");
    }

    [Fact]
    public void Constructor_NullIngestDocumentSource_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object,
            _playbookOrchestrationMock.Object, null!,
            _playbookNameMap, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ingestDocumentSource");
    }

    [Fact]
    public void Constructor_NullPlaybookNameMap_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            null!, NullLogger<InsightsOrchestrator>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookNameMap");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            _playbookNameMap, null!);
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
        await act.Should().ThrowAsync<ArgumentException>();
        _ = expectedField;
    }

    [Fact]
    public async Task AnswerQuestionAsync_CacheHit_ReturnsArtifactAndDoesNotInvokeEngine()
    {
        var artifact = MakeArtifact();
        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsEngineRunResult.FromArtifact(artifact));

        var sut = CreateSut();
        var req = MakeAgentRequest();

        var result = await sut.AnswerQuestionAsync(req);

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
        var artifact = MakeArtifact();

        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromArtifact(artifact);
                });

        _engineMock.Setup(e => e.ExecuteBatchAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticEngineStreamAsync(ct));

        var sut = CreateSut();
        var req = MakeAgentRequest(new Dictionary<string, string> { ["matterType"] = "ip-licensing" });

        var result = await sut.AnswerQuestionAsync(req);

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

        var result = await sut.AnswerQuestionAsync(req);

        result.Artifact.Should().BeNull();
        result.Decline.Should().NotBeNull();
        result.Decline!.Reason.Should().Be("insufficient-evidence");
        result.Decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters");
        result.Decline.ConfidenceInDecline.Should().Be(0.95);
        result.CacheHit.Should().BeFalse("declines are never cached; CacheHit is always false on decline path");
    }

    [Fact]
    public async Task AnswerQuestionAsync_EngineProducesNothing_ReturnsScaffoldDeclineAsDefensiveFallback()
    {
        _cacheMock.Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsEngineRunResult.Empty);

        var sut = CreateSut();
        var req = MakeAgentRequest();

        var result = await sut.AnswerQuestionAsync(req);

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
        captured.Parameters.Should().NotBeNull();
        captured.Parameters!.Should().Contain("k1", "v1");
        captured.Parameters.Should().Contain("k2", "v2");
        captured.Parameters.Should().Contain("matterId", "M-9999");
        captured.Ttl.Should().BeNull("orchestrator defers to cache DefaultTtl");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RunIngestAsync — argument validation + Wave C5 parameter validation
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunIngestAsync_BlankPracticeAreaHint_ThrowsArgumentException(string hint)
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId, PracticeAreaHint: hint);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("PracticeAreaHint", StringComparison.Ordinal));
        _playbookOrchestrationMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation rejects bad input before any work is dispatched (no LLM cost)");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(-100.0)]
    public async Task RunIngestAsync_NonPositiveCostCapOverride_ThrowsArgumentException(double capValue)
    {
        var req = new InsightsIngestRequest(
            "doc-1", "M-1", TenantId,
            CostCapOverride: (decimal)capValue);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("CostCapOverride", StringComparison.Ordinal));
        _playbookOrchestrationMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
        var req = new InsightsIngestRequest(
            "doc-1", "M-1", TenantId,
            Layer2Threshold: threshold);
        var sut = CreateSut();

        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Layer2Threshold", StringComparison.Ordinal));
        _playbookOrchestrationMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public async Task RunIngestAsync_BoundaryLayer2ThresholdValues_Accepted(double threshold)
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId, Layer2Threshold: threshold);
        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());
        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) => BuildEmissionStream(
                new ObservationEmissionResult { ObservationsEmitted = 0, Layer1Classification = null, Layer2Triggered = false },
                r.PlaybookId));

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
        var expectedVector = new float[] { 0.1f, 0.2f, 0.3f };
        ReadOnlyMemory<float> capturedReturn = expectedVector;

        _openAiMock.Setup(c => c.GenerateEmbeddingAsync(
                "precedent statement text",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(capturedReturn);

        var sut = CreateSut();
        var result = await sut.EmbedTextAsync("precedent statement text");

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
    // RunIngestAsync — universal-ingest@v1 JPS playbook path
    //
    // Post Wave C-G4 (task 022), this is the ONLY runtime path. Tests for the
    // retired legacy fallback (PlaybookPathDisabled / PlaybookGuidEmpty /
    // PlaybookThrows_FallsBackToLegacy / RunFailed_FallsBackToLegacy) have been
    // removed; failures now propagate instead of falling back.
    // ─────────────────────────────────────────────────────────────────────────

    private static IngestDocumentContent MakeIngestContent(
        string documentRef = "doc:M-1:test.pdf",
        string fullText = "Document body text.",
        int chunkCount = 2)
    {
        var chunks = new List<Sprk.Bff.Api.Services.Ai.CitationVerification.ChunkRef>();
        for (int i = 0; i < chunkCount; i++)
        {
            chunks.Add(new Sprk.Bff.Api.Services.Ai.CitationVerification.ChunkRef(
                ChunkId: $"chunk-{i}",
                Text: $"chunk text {i}"));
        }
        return new IngestDocumentContent(documentRef, fullText, chunks);
    }

    /// <summary>
    /// Build a 4-event stream that mimics the universal-ingest@v1 happy-path:
    /// RunStarted → NodeCompleted(layer1) → NodeCompleted(emitObservations) → RunCompleted.
    /// </summary>
    private static IAsyncEnumerable<PlaybookStreamEvent> BuildEmissionStream(
        ObservationEmissionResult emission,
        Guid playbookId,
        string layer1Classification = "Settlement")
    {
        var runId = Guid.NewGuid();
        var layer1NodeId = Guid.NewGuid();
        var emissionNodeId = Guid.NewGuid();

        var layer1Output = NodeOutput.Ok(
            nodeId: layer1NodeId,
            outputVariable: "layer1",
            data: new { classification = layer1Classification, confidence = 0.85 });
        var emissionOutput = NodeOutput.Ok(
            nodeId: emissionNodeId,
            outputVariable: "emission",
            data: emission);

        var events = new[]
        {
            PlaybookStreamEvent.RunStarted(runId, playbookId, 6),
            PlaybookStreamEvent.NodeCompleted(runId, playbookId, layer1NodeId, "layer1Classify", layer1Output),
            PlaybookStreamEvent.NodeCompleted(runId, playbookId, emissionNodeId, "emitObservations", emissionOutput),
            PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics())
        };
        return ToAsyncEnumerable(events);
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> ToAsyncEnumerable(
        IEnumerable<PlaybookStreamEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
    }

    [Fact]
    public async Task RunIngestAsync_HappyPath_AdaptsEmissionToInsightsIngestResult()
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        var content = MakeIngestContent();
        var expectedEmission = new ObservationEmissionResult
        {
            ObservationsEmitted = 3,
            Layer1Classification = "Settlement",
            Layer2Triggered = true
        };

        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync("doc-1", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        PlaybookRunRequest? capturedRequest = null;
        string? capturedTenantId = null;
        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, t, _) =>
            {
                capturedRequest = r;
                capturedTenantId = t;
                return BuildEmissionStream(expectedEmission, r.PlaybookId);
            });

        var sut = CreateSut();

        var result = await sut.RunIngestAsync(req);

        result.ObservationsEmitted.Should().Be(3);
        result.Layer1Classification.Should().Be("Settlement");
        result.Layer2Triggered.Should().BeTrue();

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PlaybookId.Should().Be(UniversalIngestPlaybookId,
            "facade resolves universal-ingest@v1 Guid through InsightsPlaybookNameMapOptions");
        capturedRequest.DocumentIds.Should().BeEmpty("playbook reads from parameters, not ad-hoc DocumentIds");
        capturedTenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task RunIngestAsync_AssemblesRequiredParametersPerDesignA5()
    {
        var req = new InsightsIngestRequest("doc-42", "M-42", TenantId);
        var content = MakeIngestContent("doc:M-42:contract.pdf", "Some contract content.", chunkCount: 1);

        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync("doc-42", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        IReadOnlyDictionary<string, string>? capturedParams = null;
        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
            {
                capturedParams = r.Parameters;
                return BuildEmissionStream(new ObservationEmissionResult
                {
                    ObservationsEmitted = 1,
                    Layer1Classification = null,
                    Layer2Triggered = false
                }, r.PlaybookId);
            });

        var sut = CreateSut();
        await sut.RunIngestAsync(req);

        capturedParams.Should().NotBeNull();
        capturedParams!.Should().ContainKey("documentId").WhoseValue.Should().Be("doc-42");
        capturedParams.Should().ContainKey("matterId").WhoseValue.Should().Be("M-42");
        capturedParams.Should().ContainKey("tenantId").WhoseValue.Should().Be(TenantId);
        capturedParams.Should().ContainKey("documentText").WhoseValue.Should().Be("Some contract content.");
        capturedParams.Should().ContainKey("documentRef").WhoseValue.Should().Be("doc:M-42:contract.pdf");
        capturedParams.Should().ContainKey("chunksJson");
        var chunksJsonValue = capturedParams!["chunksJson"];
        chunksJsonValue.Should().StartWith("[");
        chunksJsonValue.Should().Contain("chunk-0");
    }

    [Fact]
    public async Task RunIngestAsync_OptionalOverridesFlowThroughAsInvariantStrings()
    {
        var req = new InsightsIngestRequest(
            DocumentId: "doc-1", MatterId: "M-1", TenantId: TenantId,
            PracticeAreaHint: "CTRNS",
            CostCapOverride: 1.50m,
            Layer2Threshold: 0.82);
        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        IReadOnlyDictionary<string, string>? capturedParams = null;
        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
            {
                capturedParams = r.Parameters;
                return BuildEmissionStream(new ObservationEmissionResult
                { ObservationsEmitted = 0, Layer1Classification = null, Layer2Triggered = false }, r.PlaybookId);
            });

        var sut = CreateSut();
        await sut.RunIngestAsync(req);

        capturedParams.Should().NotBeNull();
        capturedParams!.Should().ContainKey("practiceAreaHint").WhoseValue.Should().Be("CTRNS");
        capturedParams.Should().ContainKey("costCapOverride").WhoseValue.Should().Be("1.50");
        capturedParams.Should().ContainKey("layer2Threshold").WhoseValue.Should().Be("0.82");
    }

    [Fact]
    public async Task RunIngestAsync_OmittedOverridesAreAbsentFromParameters()
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        IReadOnlyDictionary<string, string>? capturedParams = null;
        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
            {
                capturedParams = r.Parameters;
                return BuildEmissionStream(new ObservationEmissionResult
                { ObservationsEmitted = 0, Layer1Classification = null, Layer2Triggered = false }, r.PlaybookId);
            });

        var sut = CreateSut();
        await sut.RunIngestAsync(req);

        capturedParams.Should().NotBeNull();
        capturedParams!.Should().NotContainKey("practiceAreaHint");
        capturedParams.Should().NotContainKey("costCapOverride");
        capturedParams.Should().NotContainKey("layer2Threshold");
    }

    [Fact]
    public async Task RunIngestAsync_DocumentNotIndexable_ReturnsEmptyResultWithoutInvokingPlaybook()
    {
        var req = new InsightsIngestRequest("doc-skip", "M-1", TenantId);
        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync("doc-skip", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestDocumentContent?)null);

        var sut = CreateSut();
        var result = await sut.RunIngestAsync(req);

        result.ObservationsEmitted.Should().Be(0);
        result.Layer1Classification.Should().BeNull();
        result.Layer2Triggered.Should().BeFalse();

        _playbookOrchestrationMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "non-indexable document short-circuits BEFORE the playbook engine is invoked");
    }

    [Fact]
    public async Task RunIngestAsync_NoEmissionNodeOutput_SurfacesEmptyResultWithLayer1Fallback()
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
            {
                var runId = Guid.NewGuid();
                var layer1NodeId = Guid.NewGuid();
                var layer1 = NodeOutput.Ok(layer1NodeId, "layer1",
                    new { classification = "Correspondence", confidence = 0.95 });
                var events = new[]
                {
                    PlaybookStreamEvent.RunStarted(runId, r.PlaybookId, 6),
                    PlaybookStreamEvent.NodeCompleted(runId, r.PlaybookId, layer1NodeId, "layer1Classify", layer1),
                    PlaybookStreamEvent.RunCompleted(runId, r.PlaybookId, new PlaybookRunMetrics())
                };
                return ToAsyncEnumerable(events);
            });

        var sut = CreateSut();
        var result = await sut.RunIngestAsync(req);

        result.ObservationsEmitted.Should().Be(0);
        result.Layer1Classification.Should().Be("Correspondence",
            "fallback path surfaces Layer 1 classification when captured");
        result.Layer2Triggered.Should().BeFalse();
    }

    [Fact]
    public async Task RunIngestAsync_RunFailed_PropagatesAsException()
    {
        // Wave C-G4 (task 022): retired the legacy fallback. RunFailed events now
        // propagate as InvalidOperationException; ADR-004 retry/dead-letter policy
        // applies at the InsightsIngestJobHandler / ServiceBusJobProcessor layer.
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);

        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
            {
                var runId = Guid.NewGuid();
                var events = new[]
                {
                    PlaybookStreamEvent.RunStarted(runId, r.PlaybookId, 6),
                    PlaybookStreamEvent.RunFailed(runId, r.PlaybookId, "Simulated playbook engine failure")
                };
                return ToAsyncEnumerable(events);
            });

        var sut = CreateSut();
        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("universal-ingest@v1", StringComparison.Ordinal)
                      && ex.Message.Contains("Simulated playbook engine failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunIngestAsync_PlaybookThrows_PropagatesException()
    {
        // Wave C-G4 (task 022): retired the legacy fallback. Engine exceptions now
        // propagate; the orchestrator does NOT swallow them.
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);

        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        _playbookOrchestrationMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((r, _, _) =>
                ThrowingStreamAsync(new InvalidOperationException("Simulated engine boom")));

        var sut = CreateSut();
        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Simulated engine boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunIngestAsync_PlaybookGuidUnconfigured_ThrowsInvalidOperationException()
    {
        // When InsightsPlaybookNameMapOptions does not contain a mapping for
        // universal_ingest_v1, RunIngestAsync MUST fail loudly — not silently fall back.
        // (Wave C-G4 removes the legacy fallback.)
        var emptyNameMap = new TestOptionsMonitor<InsightsPlaybookNameMapOptions>(
            new InsightsPlaybookNameMapOptions { Map = new Dictionary<string, Guid>() });
        var sut = new InsightsOrchestrator(
            _engineMock.Object, _cacheMock.Object, _openAiMock.Object,
            _playbookOrchestrationMock.Object, _ingestDocumentSourceMock.Object,
            emptyNameMap, NullLogger<InsightsOrchestrator>.Instance);

        _ingestDocumentSourceMock
            .Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeIngestContent());

        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        Func<Task> act = () => sut.RunIngestAsync(req);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("universal_ingest_v1", StringComparison.Ordinal)
                      || ex.Message.Contains("InsightsPlaybookNameMapOptions", StringComparison.Ordinal));
        _playbookOrchestrationMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "playbook is never invoked when its Guid is unconfigured");
    }

    [Fact]
    public void AssemblePlaybookParameters_ChunksSerializedAsJsonArray()
    {
        var req = new InsightsIngestRequest("doc-1", "M-1", TenantId);
        var content = MakeIngestContent(chunkCount: 3);

        var parameters = InsightsOrchestrator.AssemblePlaybookParameters(req, content);

        parameters.Should().ContainKey("chunksJson");
        var chunksJson = parameters["chunksJson"];
        using var doc = JsonDocument.Parse(chunksJson);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(3);
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

    private static async IAsyncEnumerable<PlaybookStreamEvent> ThrowingStreamAsync(
        Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return PlaybookStreamEvent.RunStarted(Guid.NewGuid(), Guid.NewGuid(), 0);
        throw ex;
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) { _value = value; }
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
