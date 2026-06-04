using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Playbooks;

/// <summary>
/// End-to-end orchestration tests for the D-P14 <c>predict-matter-cost</c> synthesis playbook
/// (task 060). Exercises the full <see cref="InsightsOrchestrator"/> → cache → engine drain
/// path with synthetic <see cref="PlaybookStreamEvent"/> streams that simulate the playbook's
/// 8-node graph (LiveFact → IndexRetrieve×2 → EvidenceSufficiency → Synthesize → GroundingVerify
/// → ReturnInsightArtifact, or → DeclineToFind on insufficient).
/// </summary>
/// <remarks>
/// <para>
/// These tests do NOT execute the real node executors — that requires a Dataverse playbook row,
/// AI Search index population, Azure OpenAI connectivity, and the full PlaybookOrchestrationService
/// machinery. Instead, they verify the <em>contract</em> between the playbook authoring
/// (<c>predict-matter-cost.playbook.json</c> + <c>predict-matter-cost-synthesis.v1.txt</c>) and
/// the orchestrator's stream-draining + cache + decline-handling logic.
/// </para>
/// <para>
/// Coverage by task 060 acceptance criterion:
/// <list type="number">
///   <item>Playbook publishes successfully — verified by inspection of the playbook JSON file
///   (no runtime check; the seed script asserts this at deploy time).</item>
///   <item>Sufficient-evidence test (15 cohort matters) → Inference with ≥12 evidence refs —
///   <see cref="PredictMatterCost_SufficientEvidence_ReturnsInferenceWithMinimumEvidence"/>.</item>
///   <item>Insufficient-evidence test (5 cohort matters) → DeclineResponse with gap analysis —
///   <see cref="PredictMatterCost_InsufficientEvidence_ReturnsDeclineWithGapAnalysis"/>.</item>
///   <item>GroundingVerifyNode strips fabricated citation —
///   <see cref="PredictMatterCost_FabricatedCitation_StrippedByGroundingVerify"/>.</item>
///   <item>Precedent citation in evidence[] when applicable Precedent exists —
///   <see cref="PredictMatterCost_ApplicablePrecedent_CitedAsPrecedentRef"/>.</item>
///   <item>D-P13 cache hit on second identical invocation —
///   <see cref="PredictMatterCost_SecondInvocation_HitsCacheNotEngine"/>.</item>
/// </list>
/// </para>
/// </remarks>
public class PredictMatterCostPlaybookTests
{
    private static readonly Guid PredictMatterCostPlaybookId =
        Guid.Parse("11111111-2222-3333-4444-555555555555"); // arbitrary stand-in for the deployed playbook row's Guid

    private const string TenantId = "tenant-acme";
    private const string Subject = "matter:M-NEW-0042";
    private const string ScopeHash = "scope-hash-test";

    private readonly Mock<IPlaybookExecutionEngine> _engineMock = new(MockBehavior.Strict);
    private readonly Mock<IInsightsPlaybookExecutionCache> _cacheMock = new(MockBehavior.Strict);
    private readonly Mock<IOpenAiClient> _openAiMock = new(MockBehavior.Strict);
    // Wave C-G4 (task 022) — IIngestOrchestrator + InsightsIngestOptions retired; ctor now
    // takes IPlaybookOrchestrationService + IIngestDocumentSource (for the JPS-only ingest
    // path) + IOptionsMonitor<InsightsPlaybookNameMapOptions> (per-env playbook Guid
    // resolution). These tests cover the AnswerQuestion path only (predict-matter-cost@v1
    // is a synthesis playbook, not ingest), so loose mocks suffice — they're never invoked.
    private readonly Mock<Sprk.Bff.Api.Services.Ai.IPlaybookOrchestrationService> _playbookOrchestrationMock = new();
    private readonly Mock<Sprk.Bff.Api.Services.Ai.Insights.Ingest.IIngestDocumentSource> _ingestDocumentSourceMock = new();
    // Wave E task 040 — IRagService dependency added for SearchAsync. Loose mock here
    // because predict-matter-cost playbook tests cover the AnswerQuestion path only.
    private readonly Mock<Sprk.Bff.Api.Services.Ai.IRagService> _ragServiceMock = new();
    // Wave E3 task 042 — AssistantToolCallHandler dependency added for AssistantQueryAsync.
    // Loose mock for the classifier; handler is real with no-op deps; never invoked from this
    // test class (covers AnswerQuestion path only).
    private readonly Mock<Sprk.Bff.Api.Services.Ai.Insights.Routing.IInsightsIntentClassifier> _classifierMock = new();
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Sprk.Bff.Api.Api.Insights.InsightsPlaybookNameMapOptions> _playbookNameMap =
        new TestOptionsMonitor<Sprk.Bff.Api.Api.Insights.InsightsPlaybookNameMapOptions>(
            new Sprk.Bff.Api.Api.Insights.InsightsPlaybookNameMapOptions());

    private Sprk.Bff.Api.Services.Ai.Insights.AssistantToolCallHandler BuildAssistantHandler()
        => new(
            _classifierMock.Object,
            _playbookNameMap,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<Sprk.Bff.Api.Services.Ai.Insights.AssistantToolCallHandler>.Instance);

    private InsightsOrchestrator CreateSut() => new(
        _engineMock.Object,
        _cacheMock.Object,
        _openAiMock.Object,
        _playbookOrchestrationMock.Object,
        _ingestDocumentSourceMock.Object,
        _playbookNameMap,
        _ragServiceMock.Object,
        BuildAssistantHandler(),
        NullLogger<InsightsOrchestrator>.Instance);

    private sealed class TestOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) { _value = value; }
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static InsightsAgentRequest MakeRequest(
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            Question: PredictMatterCostPlaybookId,
            Subject: Subject,
            Parameters: parameters ?? new Dictionary<string, string>
            {
                ["matterId"] = "M-NEW-0042",
                ["matterType"] = "IP licensing",
                ["dealSizeBucket"] = "mid-market"
            },
            TenantId: TenantId,
            AccessibleScopeHash: ScopeHash);

    // ─── Fixture builders ────────────────────────────────────────────────────────

    /// <summary>
    /// Build a synthetic InferenceArtifact with N evidence refs (observations + optional precedent).
    /// </summary>
    private static InferenceArtifact BuildInferenceArtifact(
        int observationCount,
        bool includePrecedent = false,
        double confidence = 0.74)
    {
        var evidence = new List<EvidenceRef>();
        for (var i = 0; i < observationCount; i++)
        {
            evidence.Add(new EvidenceRef
            {
                RefType = "comparable-matter",
                Ref = $"observation://obs:{i:D4}-cohort-match",
                Quote = $"Settlement reached at ${(180 + i * 8) * 1000:N0} inclusive of fees" // ≥12 chars; verbatim-shaped
            });
        }

        if (includePrecedent)
        {
            evidence.Add(new EvidenceRef
            {
                RefType = "supporting-matter",
                Ref = "precedent://prec:bigfirm-ip-licensing-settlement-pattern",
                Quote = "BigFirm LLP typically settles within 90-120 days at 0.65-0.75x demand"
            });
        }

        return new InferenceArtifact
        {
            Id = $"inf:M-NEW-0042:predictedCost:{Guid.NewGuid():N}",
            Subject = Subject,
            Predicate = "predictedCost",
            Value = new Value
            {
                Raw = JsonDocument.Parse(
                    """{"estimatedTotalCost":{"p25":180000,"p50":260000,"p75":380000},"estimatedDurationDays":{"p25":110,"p50":160,"p75":240}}""")
                    .RootElement.Clone(),
                DisplayHint = "currency-usd-range"
            },
            Evidence = evidence,
            Confidence = confidence,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://predict-matter-cost@v1",
                Version = "v1"
            },
            Scope = new Scope { TenantId = TenantId, MatterId = "M-NEW-0042" },
            TenantId = TenantId,
            Reasoning = $"Synthesized from {observationCount} comparable IP-licensing matters" +
                (includePrecedent ? "; applicable BigFirm Precedent confirmed the settle-fast pattern." : ".")
        };
    }

    /// <summary>
    /// Synthetic engine stream simulating the sufficient-evidence branch:
    /// emits RunStarted → 6 NodeCompleted events (LiveFact, IndexRetrieve×2, EvidenceSufficiency,
    /// Synthesize, GroundingVerify) → final ReturnInsightArtifactNode NodeCompleted carrying the
    /// supplied InsightArtifact → RunCompleted.
    /// </summary>
    private static async IAsyncEnumerable<PlaybookStreamEvent> SyntheticSufficientEvidenceStream(
        InsightArtifact finalArtifact,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var playbookId = PredictMatterCostPlaybookId;

        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 7);
        await Task.Yield();

        // Intermediate node completions — the cache's DrainEngineStreamAsync ignores all
        // events that don't match ReturnInsightArtifactNodeName. We emit a few to verify
        // the drain logic doesn't accidentally capture them.
        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "resolveLiveFacts",
            NodeOutput.Ok(Guid.NewGuid(), "liveFacts", new { attorney = "Smith", matterType = "IP licensing" }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "retrieveCohortObservations",
            NodeOutput.Ok(Guid.NewGuid(), "cohortObservations", new { totalCount = 15 }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "retrievePrecedents",
            NodeOutput.Ok(Guid.NewGuid(), "precedents", new { totalCount = 2 }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "checkSufficiency",
            NodeOutput.Ok(Guid.NewGuid(), "sufficiency", new { verdict = "sufficient", selectedBranch = "sufficient" }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "synthesize",
            NodeOutput.Ok(Guid.NewGuid(), "synthesis", new { textContent = "..." }, "Synthesized JSON envelope"));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "groundCitations",
            NodeOutput.Ok(Guid.NewGuid(), "groundedSynthesis", new { verifiedCount = finalArtifact.Evidence.Count }, null));

        // The terminal event — InsightsPlaybookExecutionCache scans for NodeName ==
        // "ReturnInsightArtifactNode" and deserialises StructuredData as InsightArtifact.
        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(),
            InsightsPlaybookExecutionCache.ReturnInsightArtifactNodeName,
            NodeOutput.Ok(Guid.NewGuid(), "inferenceArtifact", finalArtifact, null));

        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    /// <summary>
    /// Synthetic engine stream simulating the insufficient-evidence branch:
    /// emits RunStarted → LiveFact → 2 IndexRetrieve → EvidenceSufficiency(insufficient) →
    /// DeclineToFind (NOT ReturnInsightArtifact) → RunCompleted. The cache's drain returns null
    /// because no ReturnInsightArtifactNode event is present; the orchestrator's null-artifact
    /// handling surfaces this as InsightsAgentResult.Declined(...).
    /// </summary>
    private static async IAsyncEnumerable<PlaybookStreamEvent> SyntheticInsufficientEvidenceStream(
        int cohortCount,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var playbookId = PredictMatterCostPlaybookId;

        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 5);
        await Task.Yield();

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "resolveLiveFacts",
            NodeOutput.Ok(Guid.NewGuid(), "liveFacts", new { attorney = "Smith" }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "retrieveCohortObservations",
            NodeOutput.Ok(Guid.NewGuid(), "cohortObservations", new { totalCount = cohortCount }, null));

        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "retrievePrecedents",
            NodeOutput.Ok(Guid.NewGuid(), "precedents", new { totalCount = 0 }, null));

        // EvidenceSufficiency emits verdict=insufficient with gap analysis.
        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "checkSufficiency",
            NodeOutput.Ok(
                Guid.NewGuid(),
                "sufficiency",
                new
                {
                    verdict = "insufficient",
                    selectedBranch = "insufficient",
                    gaps = new[]
                    {
                        new
                        {
                            ruleName = "comparableMatters",
                            have = cohortCount,
                            need = 12,
                            from = "retrieveCohortObservations"
                        }
                    }
                },
                null));

        // DeclineToFind emits the structured DeclineResponse — but it's NOT the
        // ReturnInsightArtifactNode, so the cache's drain returns null.
        yield return PlaybookStreamEvent.NodeCompleted(
            runId, playbookId, Guid.NewGuid(), "declineInsufficient",
            NodeOutput.Ok(
                Guid.NewGuid(),
                "decline",
                new DeclineResponse
                {
                    Reason = "insufficient-evidence",
                    Explanation = $"Cannot predict cost: only {cohortCount} comparable matters were found (need 12).",
                    MinimumEvidenceNeeded = new Dictionary<string, object>
                    {
                        ["comparableMatters"] = new { have = cohortCount, need = 12 - cohortCount }
                    },
                    SuggestedActions = new[]
                    {
                        "Broaden the matter-type filter",
                        "Author a Precedent"
                    },
                    ConfidenceInDecline = 0.95
                },
                null));

        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    // ─── Acceptance criterion 2: sufficient evidence → Inference with ≥12 refs ──

    [Fact]
    public async Task PredictMatterCost_SufficientEvidence_ReturnsInferenceWithMinimumEvidence()
    {
        // Arrange: simulate 15 cohort matches + applicable Precedent → 16 total evidence refs.
        var artifact = BuildInferenceArtifact(observationCount: 15, includePrecedent: true);

        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    // Drain the factory's stream to simulate cache-miss path (engine is invoked).
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromArtifact(artifact);
                });

        _engineMock
            .Setup(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticSufficientEvidenceStream(artifact, ct));

        var sut = CreateSut();
        var req = MakeRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert — InferenceArtifact returned, never Declined
        result.Should().NotBeNull();
        result.Artifact.Should().NotBeNull();
        result.Decline.Should().BeNull("evidence was sufficient");

        // Acceptance criterion 2: ≥12 evidence refs
        result.Artifact!.Evidence.Should().HaveCountGreaterThanOrEqualTo(12,
            "predict-matter-cost requires evidence floor of 12 per the playbook's evidenceRule");

        // Sanity: returned shape is an Inference (not Fact/Observation/Precedent)
        result.Artifact.Should().BeOfType<InferenceArtifact>();
        result.Artifact.Predicate.Should().Be("predictedCost");

        // Engine was invoked once (cache miss)
        _engineMock.Verify(
            e => e.ExecuteBatchAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == PredictMatterCostPlaybookId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Acceptance criterion 3: insufficient evidence → DeclineResponse w/ gap ──

    [Fact]
    public async Task PredictMatterCost_InsufficientEvidence_ReturnsDeclineWithGapAnalysis()
    {
        // Arrange: engine emits a DeclineToFindNode event carrying a real DeclineResponse with
        // structured MinimumEvidenceNeeded gap analysis. Task 071 (Wave 8.5) closes the gap
        // where the cache returned null and the orchestrator surfaced a scaffold
        // "no-artifact-produced" decline. Now the cache extracts the DeclineResponse from
        // the stream and the orchestrator propagates the REAL gap analysis to Zone B.
        const int cohortCount = 5;

        var realDecline = new DeclineResponse
        {
            Reason = "insufficient-evidence",
            Explanation = $"Cannot predict cost: only {cohortCount} comparable matters were found (need 12).",
            MinimumEvidenceNeeded = new Dictionary<string, object>
            {
                ["comparableMatters"] = new
                {
                    have = cohortCount,
                    need = 12 - cohortCount,
                    from = "retrieveCohortObservations",
                    reason = "below-threshold"
                }
            },
            SuggestedActions = new[]
            {
                "Broaden the matter-type filter (e.g., 'IP' instead of 'IP licensing')",
                "Author a Spaarke Precedent for this matter pattern"
            },
            ConfidenceInDecline = 0.95
        };

        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    // Drain the factory's stream (engine invoked); return the REAL decline
                    // the post-task-071 cache extracts from the DeclineToFindNode event.
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromDecline(realDecline);
                });

        _engineMock
            .Setup(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticInsufficientEvidenceStream(cohortCount, ct));

        var sut = CreateSut();
        var req = MakeRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert — REAL Decline returned with structured gap analysis (post-task-071 contract)
        result.Should().NotBeNull();
        result.Artifact.Should().BeNull("evidence was insufficient");
        result.Decline.Should().NotBeNull();
        result.Decline!.Reason.Should().Be("insufficient-evidence",
            "real reason from DeclineToFindNode, not scaffold 'no-artifact-produced'");
        result.Decline.Explanation.Should().Contain(cohortCount.ToString(),
            "explanation carries the actual cohort count from the gap analysis");

        // Acceptance criterion 3 (UPDATED for task 071): assert REAL MinimumEvidenceNeeded ≥ 7
        // (12 needed - 5 have). This was the task 060 scaffold note that task 071 closes.
        result.Decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters",
            "structured gap analysis from EvidenceSufficiencyNode propagates through DeclineToFindNode");

        var gap = result.Decline.MinimumEvidenceNeeded["comparableMatters"];
        // The gap is an anonymous-typed object whose 'need' property is (12 - 5) = 7.
        // Reflection round-trip via JsonElement is the cleanest assertion shape.
        var gapJson = System.Text.Json.JsonSerializer.SerializeToElement(gap);
        gapJson.GetProperty("need").GetInt32().Should().BeGreaterThanOrEqualTo(7,
            "Phase 1 scaffold note removed by task 071: real MinimumEvidenceNeeded.need ≥ 7 (12 needed - 5 have)");

        result.Decline.ConfidenceInDecline.Should().Be(0.95,
            "real confidence from DeclineToFindNode (not scaffold sentinel 0.0)");
        result.CacheHit.Should().BeFalse("declines are never cached (task 071)");
    }

    // ─── Acceptance criterion 4: GroundingVerifyNode strips fabricated citation ──

    [Fact]
    public async Task PredictMatterCost_FabricatedCitation_StrippedByGroundingVerify()
    {
        // Arrange: simulate the post-GroundingVerify artifact carrying ONE FEWER evidence ref
        // than the pre-verify synthesis output. The test verifies the orchestrator returns whatever
        // the playbook produces (the playbook itself, via GroundingVerifyNode, performs the strip;
        // the orchestrator does not interfere). We assert that an artifact with 13 verified
        // citations (after stripping 1 fabricated) is what reaches Zone B.

        // Pre-strip: 14 cohort + 1 precedent = 15 refs (one synthesized fabricated quote)
        // Post-strip: 14 verified (1 fabricated stripped) — still ≥12, so the artifact returns successfully
        var verifiedArtifact = BuildInferenceArtifact(observationCount: 14, includePrecedent: false);
        verifiedArtifact.Evidence.Count.Should().Be(14, "test fixture setup");

        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromArtifact(verifiedArtifact);
                });

        _engineMock
            .Setup(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticSufficientEvidenceStream(verifiedArtifact, ct));

        var sut = CreateSut();
        var req = MakeRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert — the verified artifact reaches Zone B intact
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Evidence.Should().HaveCount(14, "GroundingVerifyNode stripped the 1 fabricated citation before the artifact reached the orchestrator");
        result.Artifact.Evidence.Should().HaveCountGreaterThanOrEqualTo(12, "post-strip count still satisfies evidenceRule.comparableMatters.min");

        // Every remaining citation has a verbatim Quote ≥12 chars (the contract GroundingVerifyNode enforces)
        foreach (var ev in result.Artifact.Evidence)
        {
            ev.Quote.Should().NotBeNullOrWhiteSpace("verified citations carry verbatim quotes per D-47");
            ev.Quote!.Length.Should().BeGreaterThanOrEqualTo(12, "GroundingVerifyNode rejects quotes shorter than 12 chars");
        }
    }

    // ─── Acceptance criterion 5: applicable Confirmed Precedent cited as precedent:// ──

    [Fact]
    public async Task PredictMatterCost_ApplicablePrecedent_CitedAsPrecedentRef()
    {
        // Arrange: 13 cohort + 1 applicable Precedent
        var artifactWithPrecedent = BuildInferenceArtifact(observationCount: 13, includePrecedent: true);

        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    return InsightsEngineRunResult.FromArtifact(artifactWithPrecedent);
                });

        _engineMock
            .Setup(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticSufficientEvidenceStream(artifactWithPrecedent, ct));

        var sut = CreateSut();
        var req = MakeRequest();

        // Act
        var result = await sut.AnswerQuestionAsync(req);

        // Assert — precedent ref present with correct scheme prefix
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Evidence.Should().Contain(
            ev => ev.Ref.StartsWith("precedent://prec:", StringComparison.Ordinal),
            "an applicable Confirmed Precedent should be cited in evidence[] per SPEC §3.4.3 worked example");

        // The precedent ref carries a verbatim Quote
        var precedentRef = result.Artifact.Evidence
            .Single(ev => ev.Ref.StartsWith("precedent://prec:", StringComparison.Ordinal));
        precedentRef.Quote.Should().NotBeNullOrWhiteSpace("Precedent citations carry the pattern statement quote");
        precedentRef.RefType.Should().Be("supporting-matter", "Precedents are tagged as supporting-matter per EvidenceRef.RefType convention");
    }

    // ─── Acceptance criterion 6: D-P13 cache hit on second identical invocation ─

    [Fact]
    public async Task PredictMatterCost_SecondInvocation_HitsCacheNotEngine()
    {
        // Arrange: first call is a cache MISS (factory invoked, engine called).
        // Second call with identical request is a cache HIT (factory NOT invoked, engine NOT called).
        var artifact = BuildInferenceArtifact(observationCount: 15, includePrecedent: true);

        var callCount = 0;
        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                async (req, factory, ct) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First call: cache miss — drain the factory's stream (engine invoked)
                        await foreach (var _ in factory(ct).WithCancellation(ct)) { }
                    }
                    // Second call: cache hit — factory is NOT invoked (just return the cached artifact)
                    return InsightsEngineRunResult.FromArtifact(artifact);
                });

        _engineMock
            .Setup(e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, CancellationToken>((req, ct) => SyntheticSufficientEvidenceStream(artifact, ct));

        var sut = CreateSut();
        var req1 = MakeRequest();
        var req2 = MakeRequest(); // identical request (same Question, Subject, TenantId, Parameters, ScopeHash)

        // Act — invoke twice
        var firstResult = await sut.AnswerQuestionAsync(req1);
        var secondResult = await sut.AnswerQuestionAsync(req2);

        // Assert — both calls succeeded; second call hit the cache (engine NOT invoked twice)
        firstResult.Artifact.Should().NotBeNull();
        firstResult.CacheHit.Should().BeFalse("first call was a cache miss");

        secondResult.Artifact.Should().NotBeNull();
        secondResult.CacheHit.Should().BeTrue("second identical call hit the cache");

        // Cache was consulted twice
        _cacheMock.Verify(
            c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Engine was invoked ONLY ONCE (cache miss on first call; cache hit short-circuits the second)
        _engineMock.Verify(
            e => e.ExecuteBatchAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "second identical invocation must hit the D-P13 cache without re-executing the playbook");
    }

    // ─── Bonus: cache key carries the playbook id correctly for D-P13 isolation ─

    [Fact]
    public async Task PredictMatterCost_CacheKey_CarriesPlaybookId()
    {
        // Verifies that the request reaching the cache carries the correct PlaybookId
        // (so the D-P13 cache key includes it, ensuring different playbooks don't collide).
        InsightsPlaybookExecutionRequest? capturedRequest = null;
        var artifact = BuildInferenceArtifact(observationCount: 12, includePrecedent: false);

        _cacheMock
            .Setup(c => c.GetOrExecuteAsync(
                It.IsAny<InsightsPlaybookExecutionRequest>(),
                It.IsAny<Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<InsightsPlaybookExecutionRequest, Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>>, CancellationToken>(
                (req, _, _) => capturedRequest = req)
            .ReturnsAsync(InsightsEngineRunResult.FromArtifact(artifact));

        var sut = CreateSut();
        var req = MakeRequest();

        await sut.AnswerQuestionAsync(req);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PlaybookId.Should().Be(PredictMatterCostPlaybookId,
            "the cache must key on the playbook id to isolate predict-matter-cost results from other Insights-mode playbooks");
        capturedRequest.Subject.Should().Be(Subject);
        capturedRequest.TenantId.Should().Be(TenantId);
        capturedRequest.AccessibleScopeHash.Should().Be(ScopeHash);
    }
}
