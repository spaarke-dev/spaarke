using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.CitationVerification;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Prompts;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Ingest;

/// <summary>
/// Unit tests for <see cref="IngestOrchestrator"/> (task 040, D-P7 universal ingest playbook).
/// </summary>
/// <remarks>
/// <para>
/// Covers all 6 acceptance criteria from POML:
/// <list type="number">
///   <item>Ingest playbook publishes to Dataverse playbook entity — REINTERPRETED per task brief:
///   universal-ingest is code-defined orchestration with a JSON contract spec
///   (universal-ingest.v1.json in Playbooks/), NOT a Dataverse playbook row (matches the
///   existing layer1-classification.node.json + layer2-outcome-extraction.node.json pattern).
///   Verified by file existence + JSON parse-ability test.</item>
///   <item>End-to-end test: closing-letter fixture → Layer 1 classifies → Layer 2 extracts →
///   gates pass → Observations written to spaarke-insights-index — covered by
///   <see cref="RunAsync_ClosingLetterFixture_FullPipelineEmitsObservations"/>.</item>
///   <item>End-to-end test: correspondence-email fixture → Layer 1 classifies → ConditionNode
///   skips Layer 2 → only Classification Observation written — covered by
///   <see cref="RunAsync_CorrespondenceFixture_GatesOffLayer2"/>.</item>
///   <item>sprk_analysis mirror row written for every persisted Observation — SCAFFOLD per
///   task brief (D-P11 mirror seam): test verifies <see cref="IObservationMirror.MirrorAsync"/>
///   is INVOKED for every emitted Observation, not that a real Dataverse row is written.
///   Covered by <see cref="RunAsync_AllEmittedObservations_AreMirrored"/>.</item>
///   <item>ISanitizer invoked before any LLM step (verified via capture) — covered by
///   <see cref="RunAsync_SanitizerInvokedBeforeLayer1"/>.</item>
///   <item>IInsightsAi.RunIngestAsync invokes this playbook (facade boundary respected) —
///   verified in <see cref="InsightsOrchestratorTests.RunIngestAsync_WithValidRequest_DelegatesToIngestOrchestrator"/>.</item>
/// </list>
/// </para>
/// </remarks>
public class IngestOrchestratorTests
{
    private const string DocumentId = "doc-abc-123";
    private const string MatterId = "M-2024-0341";
    private const string TenantId = "tenant-test";
    private const string DocumentRef = "spe://drive/drive-1/item/item-1";
    private static readonly DateTimeOffset FixedAsOf = new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid FixedRunId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IIngestDocumentSource> _docSource = new(MockBehavior.Strict);
    private readonly Mock<IInsightsContentSanitizer> _sanitizer = new(MockBehavior.Strict);
    private readonly Mock<IOpenAiClient> _openAi = new(MockBehavior.Strict);
    private readonly Mock<IInsightsPromptLoader> _promptLoader = new(MockBehavior.Strict);
    private readonly Mock<ILayer1ClassificationEmitter> _layer1Emitter = new(MockBehavior.Strict);
    private readonly Mock<IGroundingVerifier> _groundingVerifier = new(MockBehavior.Strict);
    private readonly Mock<IObservationEmitter> _observationEmitter = new(MockBehavior.Strict);
    private readonly Mock<IObservationIndexUpserter> _indexUpserter = new(MockBehavior.Strict);
    private readonly Mock<IObservationMirror> _mirror = new(MockBehavior.Strict);
    private readonly FixedTimeProvider _time = new(FixedAsOf);

    private IngestOrchestrator CreateSut() =>
        new(
            _docSource.Object,
            _sanitizer.Object,
            _openAi.Object,
            _promptLoader.Object,
            _layer1Emitter.Object,
            _groundingVerifier.Object,
            _observationEmitter.Object,
            _indexUpserter.Object,
            _mirror.Object,
            _time,
            NullLogger<IngestOrchestrator>.Instance);

    // ───────────────────────────────────────────────────────────────────────────
    // Argument validation (defensive)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullRequest_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.RunAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "M-1", TenantId)]
    [InlineData("doc-1", "", TenantId)]
    [InlineData("doc-1", "M-1", "")]
    public async Task RunAsync_BlankRequiredField_Throws(string docId, string matterId, string tenantId)
    {
        var sut = CreateSut();
        var req = new InsightsIngestRequest(docId, matterId, tenantId);
        Func<Task> act = () => sut.RunAsync(req, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Document-not-found path (acceptance: non-indexable upload is a no-op, not an error)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DocumentNotInFilesIndex_ReturnsEmptyResult()
    {
        _docSource
            .Setup(s => s.FetchAsync(DocumentId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestDocumentContent?)null);

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.Should().Be(new InsightsIngestResult(0, null, false));
        // No other dependency should be called when the document is not indexable.
        _sanitizer.VerifyNoOtherCalls();
        _openAi.VerifyNoOtherCalls();
        _layer1Emitter.VerifyNoOtherCalls();
        _observationEmitter.VerifyNoOtherCalls();
        _indexUpserter.VerifyNoOtherCalls();
        _mirror.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_SanitizerReturnsEmpty_ReturnsEmptyResult()
    {
        SetupDocumentSource("non-empty raw text");
        _sanitizer
            .Setup(s => s.SanitizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanitizationResult(string.Empty, 0, 0, false, false));

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.Should().Be(new InsightsIngestResult(0, null, false));
        _openAi.VerifyNoOtherCalls();
        _layer1Emitter.VerifyNoOtherCalls();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 5: ISanitizer invoked BEFORE any LLM step
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SanitizerInvokedBeforeLayer1()
    {
        var callOrder = new List<string>();

        SetupDocumentSource("raw document text");
        _sanitizer
            .Setup(s => s.SanitizeAsync("raw document text", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("sanitize"))
            .ReturnsAsync(new SanitizationResult("sanitized text", 17, 14, false, false));

        SetupLayer1Prompt();
        _openAi
            .Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("llm"))
            .ReturnsAsync(SerializeLayer1("correspondence", 0.95, "looks like email"));

        SetupLayer1EmissionCapture();
        // No Layer 2 (correspondence is not outcome-bearing).
        var sut = CreateSut();
        await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        callOrder.Should().BeEquivalentTo(new[] { "sanitize", "llm" }, opt => opt.WithStrictOrdering(),
            "the sanitizer MUST run before any LLM call per D-50 / D-A25");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 3: correspondence-email fixture gates off Layer 2
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CorrespondenceFixture_GatesOffLayer2()
    {
        SetupDocumentSource("Dear counsel, please find attached...");
        SetupSanitizer(in_: "Dear counsel, please find attached...", out_: "Dear counsel, please find attached...");
        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("correspondence", 0.92, "letterhead + salutation + sign-off"));
        SetupLayer1EmissionCapture();

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.ObservationsEmitted.Should().Be(1, "only Layer 1 Observation is emitted for correspondence");
        result.Layer1Classification.Should().Be("correspondence");
        result.Layer2Triggered.Should().BeFalse("correspondence is not outcome-bearing per D-59");

        // Layer 2 LLM call MUST NOT happen.
        _openAi.Verify(
            o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), "outcome_extraction_v1",
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _observationEmitter.VerifyNoOtherCalls();
        _groundingVerifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_OutcomeBearingButLowConfidence_GatesOffLayer2()
    {
        SetupDocumentSource("settlement-like doc");
        SetupSanitizer(in_: "settlement-like doc", out_: "settlement-like doc");
        SetupLayer1Prompt();
        // Confidence below the 0.7 threshold even though classification IS outcome-bearing.
        SetupLayer1LlmCall(SerializeLayer1("settlement_agreement", 0.65, "uncertain"));
        SetupLayer1EmissionCapture();

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.Layer2Triggered.Should().BeFalse("confidence below 0.7 gates Layer 2 off per SPEC §3.3");
        result.ObservationsEmitted.Should().Be(1);
        _observationEmitter.VerifyNoOtherCalls();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 2: closing-letter fixture → full pipeline emits Observations
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ClosingLetterFixture_FullPipelineEmitsObservations()
    {
        const string rawText = "Closing letter for M-2024-0341. Settled for $310,000 on 2024-08-15.";
        const string sanitized = "Closing letter for M-2024-0341. Settled for $310,000 on 2024-08-15.";

        SetupDocumentSource(rawText, chunks: new[]
        {
            new ChunkRef("doc-abc-123#chunk-0", rawText)
        });
        SetupSanitizer(in_: rawText, out_: sanitized);

        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("closing_letter", 0.92, "matter-closure narrative + outcome citation"));
        SetupLayer1EmissionCapture();

        SetupLayer2Prompt();
        SetupLayer2LlmCall(SerializeLayer2(
            outcomeCategory: "favorable_to_client",
            outcomeCategoryQuote: "Settled for $310,000",
            outcomeCategoryConfidence: 0.88,
            settlementAmount: 310000m,
            settlementAmountQuote: "$310,000",
            settlementAmountConfidence: 0.93,
            outcomeDate: "2024-08-15",
            outcomeDateQuote: "on 2024-08-15",
            outcomeDateConfidence: 0.90));

        // GroundingVerifier — all three quotes are substrings of the chunk → Verified.
        _groundingVerifier
            .Setup(g => g.VerifyAsync(
                It.IsAny<IEnumerable<EvidenceRef>>(),
                It.IsAny<IEnumerable<ChunkRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VerificationResult>
            {
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "Settled for $310,000" }, Verdict = VerificationVerdict.Verified, Reason = "exact" },
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "$310,000" }, Verdict = VerificationVerdict.Verified, Reason = "exact" },
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "on 2024-08-15" }, Verdict = VerificationVerdict.Verified, Reason = "exact" }
            } as IReadOnlyList<VerificationResult>);

        // Layer 2 emitter — capture the ExtractionResult, simulate emission of 3 Observations,
        // and invoke the upsert callback exactly as ObservationEmitter would.
        ExtractionResult? capturedExtraction = null;
        _observationEmitter
            .Setup(e => e.EmitFromExtractionAsync(
                It.IsAny<ExtractionResult>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExtractionResult, Func<ObservationArtifact, CancellationToken, Task>?, CancellationToken>(
                async (ext, upsert, ct) =>
                {
                    capturedExtraction = ext;
                    if (upsert is null) return;
                    foreach (var fieldName in ext.Fields.Keys)
                    {
                        var obs = BuildObservation(ext, fieldName);
                        await upsert(obs, ct).ConfigureAwait(false);
                    }
                })
            .ReturnsAsync((ExtractionResult ext, Func<ObservationArtifact, CancellationToken, Task>? _, CancellationToken __)
                => ext.Fields.Keys.Select(k => BuildObservation(ext, k)).ToList() as IReadOnlyList<ObservationArtifact>);

        // Substrate writes succeed for everything.
        _indexUpserter
            .Setup(u => u.UpsertAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mirror
            .Setup(m => m.MirrorAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        // 1 Layer 1 + 3 Layer 2 = 4 Observations.
        result.Should().Be(new InsightsIngestResult(
            ObservationsEmitted: 4,
            Layer1Classification: "closing_letter",
            Layer2Triggered: true));

        capturedExtraction.Should().NotBeNull("Layer 2 emitter must be invoked");
        capturedExtraction!.Subject.Should().Be($"matter:{MatterId}");
        capturedExtraction.TenantId.Should().Be(TenantId);
        capturedExtraction.DocumentRef.Should().Be(DocumentRef);
        capturedExtraction.AsOf.Should().Be(FixedAsOf);
        capturedExtraction.Fields.Should().ContainKey("outcomeCategory");
        capturedExtraction.Fields.Should().ContainKey("settlementAmount");
        capturedExtraction.Fields.Should().ContainKey("outcomeDate");
        capturedExtraction.ProducedBy.Id.Should().Be("playbook://outcome-extraction@v1");
        capturedExtraction.ProducedBy.Version.Should().Be("v1");

        // 1 Layer 1 upsert + 3 Layer 2 upserts = 4 substrate writes total.
        _indexUpserter.Verify(
            u => u.UpsertAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 4 (SCAFFOLD): mirror is INVOKED for every Observation
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AllEmittedObservations_AreMirrored()
    {
        // Reuse the closing-letter scenario; assert mirror calls.
        const string rawText = "Closing letter. Settled for $50K.";
        SetupDocumentSource(rawText, chunks: new[] { new ChunkRef("c#0", rawText) });
        SetupSanitizer(in_: rawText, out_: rawText);
        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("closing_letter", 0.85, "outcome narrative"));
        SetupLayer1EmissionCapture();

        SetupLayer2Prompt();
        SetupLayer2LlmCall(SerializeLayer2(
            outcomeCategory: "favorable_to_client",
            outcomeCategoryQuote: "Settled for $50K",
            outcomeCategoryConfidence: 0.90,
            settlementAmount: 50000m,
            settlementAmountQuote: "$50K",
            settlementAmountConfidence: 0.92));

        _groundingVerifier
            .Setup(g => g.VerifyAsync(It.IsAny<IEnumerable<EvidenceRef>>(),
                It.IsAny<IEnumerable<ChunkRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VerificationResult>
            {
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "Settled for $50K" }, Verdict = VerificationVerdict.Verified, Reason = "exact" },
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "$50K" }, Verdict = VerificationVerdict.Verified, Reason = "exact" }
            } as IReadOnlyList<VerificationResult>);

        SetupLayer2EmissionWithUpsert();

        _indexUpserter
            .Setup(u => u.UpsertAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mirror
            .Setup(m => m.MirrorAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.ObservationsEmitted.Should().Be(3); // 1 Layer 1 + 2 Layer 2
        _mirror.Verify(
            m => m.MirrorAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "every emitted Observation must invoke the mirror seam (D-P11; NoOp in Phase 1)");
    }

    [Fact]
    public async Task RunAsync_MirrorFailure_DoesNotPropagate()
    {
        // Mirror throws — substrate write succeeded — orchestrator must continue (NOT throw).
        const string rawText = "Correspondence";
        SetupDocumentSource(rawText, chunks: new[] { new ChunkRef("c#0", rawText) });
        SetupSanitizer(rawText, rawText);
        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("correspondence", 0.9, "email"));
        SetupLayer1EmissionCapture();

        _mirror
            .Setup(m => m.MirrorAsync(It.IsAny<ObservationArtifact>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse failure"));

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        // Substrate write happened; ingest completed.
        result.ObservationsEmitted.Should().Be(1);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Grounding-drop path: unverified quotes drop the field BEFORE confidence gating
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_UnverifiedQuotes_DroppedFromExtractionResult()
    {
        const string rawText = "Closing letter. Settled for $310,000.";
        SetupDocumentSource(rawText, chunks: new[] { new ChunkRef("c#0", rawText) });
        SetupSanitizer(rawText, rawText);
        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("closing_letter", 0.9, "outcome narrative"));
        SetupLayer1EmissionCapture();

        SetupLayer2Prompt();
        // LLM returns 2 fields. One quote is fabricated.
        SetupLayer2LlmCall(SerializeLayer2(
            outcomeCategory: "favorable_to_client",
            outcomeCategoryQuote: "Settled for $310,000", // grounded
            outcomeCategoryConfidence: 0.90,
            settlementAmount: 500000m, // FABRICATED — quote doesn't appear in source
            settlementAmountQuote: "$500,000",
            settlementAmountConfidence: 0.95));

        _groundingVerifier
            .Setup(g => g.VerifyAsync(It.IsAny<IEnumerable<EvidenceRef>>(),
                It.IsAny<IEnumerable<ChunkRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VerificationResult>
            {
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "Settled for $310,000" }, Verdict = VerificationVerdict.Verified, Reason = "exact" },
                new() { Citation = new EvidenceRef { RefType = "document", Ref = DocumentRef, Quote = "$500,000" }, Verdict = VerificationVerdict.NotFound, Reason = "no overlap" }
            } as IReadOnlyList<VerificationResult>);

        ExtractionResult? captured = null;
        _observationEmitter
            .Setup(e => e.EmitFromExtractionAsync(
                It.IsAny<ExtractionResult>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExtractionResult, Func<ObservationArtifact, CancellationToken, Task>?, CancellationToken>(
                (ext, _, _) => captured = ext)
            .ReturnsAsync(Array.Empty<ObservationArtifact>() as IReadOnlyList<ObservationArtifact>);

        var sut = CreateSut();
        await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        captured.Should().NotBeNull();
        captured!.Fields.Should().ContainKey("outcomeCategory");
        captured.Fields.Should().NotContainKey("settlementAmount",
            "fabricated quote → NotFound verdict → field dropped before reaching IObservationEmitter");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Layer 2 validation failure → orchestrator returns with Layer 1 Observation only
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Layer2ValidationFailure_ReturnsWithLayer1Only()
    {
        const string rawText = "Settlement agreement.";
        SetupDocumentSource(rawText, chunks: new[] { new ChunkRef("c#0", rawText) });
        SetupSanitizer(rawText, rawText);
        SetupLayer1Prompt();
        SetupLayer1LlmCall(SerializeLayer1("settlement_agreement", 0.9, "binding settlement language"));
        SetupLayer1EmissionCapture();

        SetupLayer2Prompt();
        // Return malformed JSON — validator will reject.
        SetupLayer2LlmCall("{ this is not valid json");

        var sut = CreateSut();
        var result = await sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), default);

        result.ObservationsEmitted.Should().Be(1, "Layer 1 still emits; Layer 2 validation failure suppresses Layer 2 emission");
        result.Layer2Triggered.Should().BeTrue("Layer 2 LLM was called even though its output was invalid");
        _observationEmitter.VerifyNoOtherCalls();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Cancellation propagation
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancellationToken_Propagates()
    {
        SetupDocumentSource("text");
        SetupSanitizer("text", "text");
        SetupLayer1Prompt();
        _openAi
            .Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();
        Func<Task> act = () => sut.RunAsync(new InsightsIngestRequest(DocumentId, MatterId, TenantId), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────

    private void SetupDocumentSource(string fullText, IReadOnlyList<ChunkRef>? chunks = null)
    {
        chunks ??= new[] { new ChunkRef("default-chunk-id", fullText) };
        _docSource
            .Setup(s => s.FetchAsync(DocumentId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestDocumentContent(DocumentRef, fullText, chunks));
    }

    private void SetupSanitizer(string in_, string out_)
    {
        _sanitizer
            .Setup(s => s.SanitizeAsync(in_, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanitizationResult(out_, in_.Length, out_.Length, false, false));
    }

    private void SetupLayer1Prompt()
    {
        _promptLoader
            .Setup(p => p.Get("classification.v1"))
            .Returns(new InsightsPrompt("classify this:", "{\"type\":\"object\"}", "classification_v1"));
    }

    private void SetupLayer2Prompt()
    {
        _promptLoader
            .Setup(p => p.Get("outcome-extraction.v1"))
            .Returns(new InsightsPrompt("extract this:", "{\"type\":\"object\"}", "outcome_extraction_v1"));
    }

    private void SetupLayer1LlmCall(string json) =>
        _openAi
            .Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), "classification_v1",
                "gpt-4o-mini", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

    private void SetupLayer2LlmCall(string json) =>
        _openAi
            .Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), "outcome_extraction_v1",
                "gpt-4o", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

    /// <summary>
    /// Wire the Layer 1 emitter to honor its upsertAsync callback (so mirror + substrate writes
    /// happen for Layer 1 Observations exactly like Layer 2 ones).
    /// </summary>
    private void SetupLayer1EmissionCapture()
    {
        _layer1Emitter
            .Setup(l => l.EmitAsync(
                It.IsAny<Layer1ClassificationResult>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ExtractionScope?>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Layer1ClassificationResult, string, string, ExtractionScope?, DateTimeOffset, Func<ObservationArtifact, CancellationToken, Task>?, CancellationToken>(
                async (cls, docRef, tenant, scope, asOf, upsert, ct) =>
                {
                    var obs = new ObservationArtifact
                    {
                        Id = $"obs:{docRef.Split('/').Last()}:classification",
                        Subject = docRef,
                        Predicate = "classification",
                        Value = new Value
                        {
                            Raw = ToJsonElement(cls.Classification),
                            DisplayHint = "enum"
                        },
                        Confidence = cls.Confidence,
                        Evidence = new[]
                        {
                            new EvidenceRef { RefType = "document", Ref = docRef, Quote = null }
                        },
                        AsOf = asOf,
                        ProducedBy = new ProducedBy { Kind = "playbook", Id = "playbook://classification@v1", Version = "v1" },
                        Scope = new Scope { TenantId = tenant, MatterId = scope?.MatterId },
                        TenantId = tenant
                    };
                    if (upsert is not null)
                    {
                        await upsert(obs, ct).ConfigureAwait(false);
                    }
                    return obs;
                });

        // Layer 1 substrate writes succeed by default.
        _indexUpserter
            .Setup(u => u.UpsertAsync(It.Is<ObservationArtifact>(o => o.Predicate == "classification"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mirror
            .Setup(m => m.MirrorAsync(It.Is<ObservationArtifact>(o => o.Predicate == "classification"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Setup IObservationEmitter to invoke its upsertAsync callback for every field in the
    /// extraction (simulating the real ObservationEmitter's "above threshold → emit + upsert" loop).
    /// </summary>
    private void SetupLayer2EmissionWithUpsert()
    {
        _observationEmitter
            .Setup(e => e.EmitFromExtractionAsync(
                It.IsAny<ExtractionResult>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<ExtractionResult, Func<ObservationArtifact, CancellationToken, Task>?, CancellationToken>(
                async (ext, upsert, ct) =>
                {
                    var emitted = new List<ObservationArtifact>();
                    foreach (var fieldName in ext.Fields.Keys)
                    {
                        var obs = BuildObservation(ext, fieldName);
                        emitted.Add(obs);
                        if (upsert is not null)
                        {
                            await upsert(obs, ct).ConfigureAwait(false);
                        }
                    }
                    return emitted;
                });
    }

    private static ObservationArtifact BuildObservation(ExtractionResult ext, string fieldName)
    {
        var field = ext.Fields[fieldName];
        return new ObservationArtifact
        {
            Id = $"obs:{ext.Subject}:{fieldName}",
            Subject = ext.Subject,
            Predicate = fieldName,
            Value = new Value { Raw = field.Value, DisplayHint = field.DisplayHint },
            Confidence = field.Confidence,
            Evidence = new[]
            {
                new EvidenceRef { RefType = "document", Ref = ext.DocumentRef, Quote = field.Quote }
            },
            AsOf = ext.AsOf,
            ProducedBy = new ProducedBy
            {
                Kind = ext.ProducedBy.Kind,
                Id = ext.ProducedBy.Id,
                Version = ext.ProducedBy.Version
            },
            Scope = new Scope { TenantId = ext.TenantId, MatterId = ext.Scope?.MatterId },
            TenantId = ext.TenantId
        };
    }

    private static string SerializeLayer1(string classification, double confidence, string reasoning) =>
        JsonSerializer.Serialize(new
        {
            classification,
            confidence,
            reasoning
        });

    private static string SerializeLayer2(
        string? outcomeCategory = null, string? outcomeCategoryQuote = null, double outcomeCategoryConfidence = 0.0,
        decimal? settlementAmount = null, string? settlementAmountQuote = null, double settlementAmountConfidence = 0.0,
        string? outcomeDate = null, string? outcomeDateQuote = null, double outcomeDateConfidence = 0.0,
        int? matterDurationDays = null, string? matterDurationDaysQuote = null, double matterDurationDaysConfidence = 0.0)
    {
        return JsonSerializer.Serialize(new
        {
            outcomeCategory,
            settlementAmount,
            settlementCurrency = settlementAmount.HasValue ? "USD" : null,
            outcomeDate,
            matterDurationDays,
            keyTerms = Array.Empty<object>(),
            evidence = new
            {
                outcomeCategory = outcomeCategoryQuote,
                settlementAmount = settlementAmountQuote,
                outcomeDate = outcomeDateQuote,
                matterDurationDays = matterDurationDaysQuote
            },
            confidence = new
            {
                outcomeCategory = outcomeCategoryConfidence,
                settlementAmount = settlementAmountConfidence,
                outcomeDate = outcomeDateConfidence,
                matterDurationDays = matterDurationDaysConfidence
            }
        }, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

/// <summary>
/// Minimal in-test fake for <see cref="TimeProvider"/>. Avoids adding the
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> package per CLAUDE.md §10 BFF
/// hygiene (minimize new package adds — package count is a CVE-surface multiplier).
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

// ─────────────────────────────────────────────────────────────────────────────
// Acceptance criterion 1: universal-ingest.v1.json playbook spec file exists + parses
// ─────────────────────────────────────────────────────────────────────────────

public class UniversalIngestPlaybookSpecTests
{
    /// <summary>
    /// Per task brief: acceptance criterion 1 ("Ingest playbook publishes successfully to
    /// Dataverse playbook entity") is REINTERPRETED — task 040 ships a code-defined orchestrator
    /// with a documented JSON contract spec sitting alongside layer1-classification.node.json +
    /// layer2-outcome-extraction.node.json. This test verifies the spec file exists and parses.
    /// </summary>
    [Fact]
    public void UniversalIngestPlaybookSpecFile_ExistsAndParses()
    {
        // The spec file is shipped as Content in the BFF csproj (not copied to the test
        // assembly). Resolve relative to the test assembly's location, walk up to repo root,
        // then descend into the BFF source path.
        var candidates = new[]
        {
            // From {repo}/tests/unit/Sprk.Bff.Api.Tests/bin/Debug/net8.0/* up to repo root.
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                "src", "server", "api", "Sprk.Bff.Api", "Services", "Ai", "Insights", "Playbooks", "universal-ingest.v1.json")),
            // Or relative to the BFF output dir if the file was copied there (Content + CopyToOutputDirectory).
            Path.Combine(AppContext.BaseDirectory, "Services", "Ai", "Insights", "Playbooks", "universal-ingest.v1.json")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        path.Should().NotBeNull(
            $"universal-ingest.v1.json must ship alongside layer1/layer2 node specs. " +
            $"Searched: {string.Join(", ", candidates)}");

        var json = File.ReadAllText(path!);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("id").GetString().Should().Be("insights:ingest:universal");
        root.GetProperty("version").GetString().Should().Be("v1");
        root.GetProperty("producedBy").GetString().Should().Be("playbook://universal-ingest@v1");
        root.GetProperty("tier").GetString().Should().Be("observation");
        root.GetProperty("sequence").GetArrayLength().Should().BeGreaterThan(10,
            "the pipeline composes Sanitizer → Layer 1 → gate → Layer 2 → validate → ground → confidence → emit → upsert → mirror");
    }
}
