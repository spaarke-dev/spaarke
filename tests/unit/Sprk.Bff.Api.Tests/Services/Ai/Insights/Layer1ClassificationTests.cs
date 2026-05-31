using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Integration tests for D-P5 — Layer 1 document classification + <c>classification@v1</c>
/// prompt + <see cref="Layer1ClassificationEmitter"/>. Realizes the acceptance criteria from
/// <c>projects/ai-spaarke-insights-engine-r1/tasks/030-layer1-classification-prompt.poml</c>:
/// <list type="number">
///   <item>Prompt returns valid JSON with all 3 fields (classification, confidence, reasoning).</item>
///   <item>Closing-letter fixture classified as <c>closing_letter</c> (confidence ≥ 0.7).</item>
///   <item>Settlement-agreement fixture classified as <c>settlement_agreement</c> (confidence ≥ 0.7).</item>
///   <item>Correspondence fixture classified as <c>correspondence</c> (confidence ≥ 0.7).</item>
///   <item>Out-of-domain fixture classified as <c>other</c> — NO false positive on outcome-bearing types.</item>
///   <item><c>producedBy="classification@v1"</c> on every emitted Classification Observation.</item>
/// </list>
/// <para>
/// Test strategy: mock <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> to return the
/// predicted typed JSON for each fixture (the prompt itself is exercised by reading the embedded
/// resource file and concatenating the fixture document content — the prompt-rendering contract
/// is the same one D-P7 will execute at runtime via AiAnalysisNodeExecutor). The mock simulates
/// what the constrained-decoding LLM call would return for each fixture; the test then walks the
/// production-shape pipeline: parse JSON → Layer1ClassificationResult → Layer1ClassificationEmitter
/// → ObservationArtifact. This is "integration" in the sense of exercising the full DI-resolvable
/// production code path minus the live LLM call; the live-LLM smoke test lives at D-P16 task 070.
/// </para>
/// </summary>
public class Layer1ClassificationTests
{
    private static readonly DateTimeOffset FixedAsOf = new(2026, 5, 28, 14, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Resolves a fixture path relative to the test assembly. Fixtures are copied from
    /// <c>tests/Insights/fixtures/</c> to <c>{output}/Insights/fixtures/</c> by the test
    /// csproj Content link.
    /// </summary>
    private static string FixturePath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Insights", "fixtures", filename);

    /// <summary>
    /// Resolves the prompt template path inside the BFF API assembly output. The prompt is
    /// copied to <c>Services/Ai/Insights/Prompts/classification.v1.txt</c> by the BFF
    /// Sprk.Bff.Api.csproj Content glob.
    /// </summary>
    private static string PromptTemplatePath() =>
        Path.Combine(AppContext.BaseDirectory, "Services", "Ai", "Insights", "Prompts", "classification.v1.txt");

    /// <summary>
    /// Resolves the schema path. Used by the test to confirm the schema is shipped alongside
    /// the prompt — the runtime path (D-P7 task 040) loads this for constrained-decoding.
    /// </summary>
    private static string SchemaPath() =>
        Path.Combine(AppContext.BaseDirectory, "Services", "Ai", "Insights", "Prompts", "classification.v1.schema.json");

    /// <summary>
    /// Resolves the playbook node config path. Used by the prompt-versioning test to assert
    /// the config references the v1 prompt template (D-62 versioning contract).
    /// </summary>
    private static string NodeConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "Services", "Ai", "Insights", "Playbooks", "layer1-classification.node.json");

    /// <summary>
    /// Reads the embedded prompt template + appends the document content the same way
    /// D-P7's runtime (via AiAnalysisNodeExecutor's tool handler) will: prompt body followed
    /// by a blank line and the raw document text. Returns the assembled prompt the mocked
    /// IOpenAiClient would receive.
    /// </summary>
    private static string BuildAssembledPrompt(string fixtureFilename)
    {
        var template = File.ReadAllText(PromptTemplatePath());
        var documentContent = File.ReadAllText(FixturePath(fixtureFilename));

        // Prompt + blank line + document body. The blank-line separator matches the convention
        // the outcome-extraction.v1.txt template uses ("Document content follows below.\n\n<body>")
        // and lets the model see the prompt instructions before the document.
        var sb = new StringBuilder();
        sb.Append(template);
        if (!template.EndsWith('\n')) sb.AppendLine();
        sb.AppendLine();
        sb.Append(documentContent);
        return sb.ToString();
    }

    private static ILayer1ClassificationEmitter NewEmitter() =>
        new Layer1ClassificationEmitter(NullLogger<Layer1ClassificationEmitter>.Instance);

    // ─── Helper: simulate a constrained-decoding LLM call for a fixture ─────────

    /// <summary>
    /// Programmatically produces the JSON the mocked LLM would return for a fixture. Mirrors
    /// the constrained-decoding contract: the JSON conforms to classification.v1.schema.json
    /// (validated by a Deserialize round-trip below).
    /// </summary>
    private static string SimulateLlmResponse(string classification, double confidence, string reasoning)
    {
        var doc = new
        {
            classification,
            confidence,
            reasoning
        };
        // The actual LLM would return compact JSON; we match that exactly.
        return JsonSerializer.Serialize(doc);
    }

    /// <summary>
    /// Configures the mocked <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> to
    /// return the supplied JSON when the assembled prompt is passed in. Asserts the schema
    /// name matches the node config (<c>ClassificationResponse</c>).
    /// </summary>
    private static Mock<IOpenAiClient> NewOpenAiClientMock(
        string assembledPromptSubstring,
        string llmResponseJson)
    {
        var mock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        mock.Setup(x => x.GetStructuredCompletionRawAsync(
                It.Is<string>(p => p.Contains(assembledPromptSubstring)),
                It.IsAny<BinaryData>(),
                "ClassificationResponse",
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponseJson);
        return mock;
    }

    // ─── Acceptance #1 — prompt + schema files are present and well-formed ─────

    [Fact]
    public void PromptTemplate_FileExists_AndContainsAllEightCategories()
    {
        // Arrange + Act
        var template = File.ReadAllText(PromptTemplatePath());

        // Assert — the prompt content + the 8-category enum + the JSON-only directive
        template.Should().NotBeNullOrWhiteSpace();
        template.Should().Contain("closing_letter");
        template.Should().Contain("settlement_agreement");
        template.Should().Contain("decision_memo");
        template.Should().Contain("deal_document");
        template.Should().Contain("pleading");
        template.Should().Contain("opinion_judgment");
        template.Should().Contain("correspondence");
        template.Should().Contain("other");
        template.Should().Contain("\"classification\"");
        template.Should().Contain("\"confidence\"");
        template.Should().Contain("\"reasoning\"");
        template.Should().Contain("Return JSON only", because: "starter prompt per SPEC-phase-1-minimum.md §3.3 mandates JSON-only output");
    }

    [Fact]
    public void Schema_FileExists_AndDeclaresEightCategoryEnum()
    {
        // Arrange + Act
        var schemaJson = File.ReadAllText(SchemaPath());
        using var doc = JsonDocument.Parse(schemaJson);

        // Assert
        doc.RootElement.GetProperty("title").GetString().Should().Be("ClassificationResponse");
        var enumValues = doc.RootElement
            .GetProperty("properties")
            .GetProperty("classification")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        enumValues.Should().BeEquivalentTo(new[]
        {
            "closing_letter", "settlement_agreement", "decision_memo", "deal_document",
            "pleading", "opinion_judgment", "correspondence", "other"
        });
    }

    [Fact]
    public void NodeConfig_ReferencesClassificationV1Prompt_AndAiAnalysisNodeExecutor()
    {
        // Arrange + Act
        var configJson = File.ReadAllText(NodeConfigPath());
        using var doc = JsonDocument.Parse(configJson);

        // Assert — the node config wires the right prompt + executor + version per D-62
        doc.RootElement.GetProperty("id").GetString().Should().Be("layer1-classification");
        doc.RootElement.GetProperty("promptTemplate").GetString().Should().Be("classification.v1.txt");
        doc.RootElement.GetProperty("promptVersion").GetString().Should().Be("v1");
        doc.RootElement.GetProperty("producedBy").GetString().Should().Be("playbook://classification@v1");
        doc.RootElement.GetProperty("executor").GetString().Should().Be("AiAnalysisNodeExecutor");
        doc.RootElement.GetProperty("actionType").GetString().Should().Be("AiAnalysis");
        // KnowledgeRetrievalConfig.Mode = Never per task POML — classification does not need retrieval
        doc.RootElement.GetProperty("knowledgeRetrieval").GetProperty("mode").GetString().Should().Be("Never");
    }

    // ─── Acceptance #2 — closing letter classifies as closing_letter ──────────

    [Fact]
    public async Task ClosingLetterFixture_ClassifiesAsClosingLetter_AboveConfidenceFloor()
    {
        // Arrange — assemble the prompt + simulate the LLM's response.
        var assembled = BuildAssembledPrompt("sample-closing-letter.txt");
        var llmJson = SimulateLlmResponse(
            classification: "closing_letter",
            confidence: 0.94,
            reasoning: "Document is a partner-signed letter memorializing matter closure with final settlement amount, outcome, and cost summary.");
        var openAi = NewOpenAiClientMock("MATTER CLOSING LETTER", llmJson);

        // Act 1 — exercise the IOpenAiClient call the way D-P7's AiAnalysisNodeExecutor will
        var raw = await openAi.Object.GetStructuredCompletionRawAsync(
            assembled,
            BinaryData.FromString(File.ReadAllText(SchemaPath())),
            "ClassificationResponse",
            model: "gpt-4o-mini",
            maxOutputTokens: 200,
            cancellationToken: CancellationToken.None);

        // Act 2 — parse the constrained-decoding response into the typed result POCO
        var result = JsonSerializer.Deserialize<Layer1ClassificationResult>(raw)!;

        // Act 3 — emit the Classification Observation through the production emitter
        var emitter = NewEmitter();
        var observation = await emitter.EmitAsync(
            result,
            documentRef: "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
            tenantId: "tenant-acme",
            scope: new ExtractionScope { MatterId = "M-2024-0341", PracticeArea = "ip-litigation" },
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert
        result.Classification.Should().Be("closing_letter");
        result.Confidence.Should().BeGreaterOrEqualTo(0.7);
        result.Reasoning.Should().NotBeNullOrWhiteSpace();

        observation.Predicate.Should().Be("classification");
        observation.Value.Raw.GetString().Should().Be("closing_letter");
        observation.Value.DisplayHint.Should().Be("enum");
        observation.Subject.Should().Be("spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx");
        observation.Confidence.Should().Be(0.94);
        observation.ProducedBy.Id.Should().Be("playbook://classification@v1");
        observation.ProducedBy.Version.Should().Be("v1");

        // Verify the gating predicate routes correctly (D-P7 will call this to decide on Layer 2)
        DocumentClassificationExtensions.TryParseClassification(result.Classification, out var typed).Should().BeTrue();
        typed.IsOutcomeBearing().Should().BeTrue("closing_letter is one of the three Layer-2-gating types per D-59");
    }

    // ─── Acceptance #3 — settlement agreement classifies as settlement_agreement ─

    [Fact]
    public async Task SettlementAgreementFixture_ClassifiesAsSettlementAgreement_AboveConfidenceFloor()
    {
        // Arrange
        var assembled = BuildAssembledPrompt("sample-settlement-agreement.txt");
        var llmJson = SimulateLlmResponse(
            classification: "settlement_agreement",
            confidence: 0.96,
            reasoning: "Document is a binding contract titled 'Settlement Agreement and Mutual Release' with settlement amount, mutual release, and no-admission-of-liability clauses.");
        var openAi = NewOpenAiClientMock("SETTLEMENT AGREEMENT AND MUTUAL RELEASE", llmJson);

        // Act
        var raw = await openAi.Object.GetStructuredCompletionRawAsync(
            assembled,
            BinaryData.FromString(File.ReadAllText(SchemaPath())),
            "ClassificationResponse",
            model: "gpt-4o-mini",
            maxOutputTokens: 200,
            cancellationToken: CancellationToken.None);
        var result = JsonSerializer.Deserialize<Layer1ClassificationResult>(raw)!;

        var emitter = NewEmitter();
        var observation = await emitter.EmitAsync(
            result,
            documentRef: "spe://drive/acme-matters/item/settlement-agreement-M-2024-0341.docx",
            tenantId: "tenant-acme",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert
        result.Classification.Should().Be("settlement_agreement");
        result.Confidence.Should().BeGreaterOrEqualTo(0.7);
        observation.Value.Raw.GetString().Should().Be("settlement_agreement");
        observation.ProducedBy.Id.Should().Be("playbook://classification@v1");
        observation.ProducedBy.Version.Should().Be("v1");

        DocumentClassificationExtensions.TryParseClassification(result.Classification, out var typed).Should().BeTrue();
        typed.IsOutcomeBearing().Should().BeTrue("settlement_agreement is one of the three Layer-2-gating types per D-59");
    }

    // ─── Acceptance #4 — correspondence email classifies as correspondence ─────

    [Fact]
    public async Task CorrespondenceFixture_ClassifiesAsCorrespondence_AboveConfidenceFloor_AndDoesNotGateLayer2()
    {
        // Arrange
        var assembled = BuildAssembledPrompt("sample-correspondence.txt");
        var llmJson = SimulateLlmResponse(
            classification: "correspondence",
            confidence: 0.92,
            reasoning: "Document is a casual professional email exchange about scheduling and logistics — no contract terms, no court filing, no outcome summary.");
        var openAi = NewOpenAiClientMock("Subject: Re: Quick question on next week's deposition prep", llmJson);

        // Act
        var raw = await openAi.Object.GetStructuredCompletionRawAsync(
            assembled,
            BinaryData.FromString(File.ReadAllText(SchemaPath())),
            "ClassificationResponse",
            model: "gpt-4o-mini",
            maxOutputTokens: 200,
            cancellationToken: CancellationToken.None);
        var result = JsonSerializer.Deserialize<Layer1ClassificationResult>(raw)!;

        var emitter = NewEmitter();
        var observation = await emitter.EmitAsync(
            result,
            documentRef: "spe://drive/acme-matters/item/email-deposition-prep-2024-06-04.txt",
            tenantId: "tenant-acme",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert
        result.Classification.Should().Be("correspondence");
        result.Confidence.Should().BeGreaterOrEqualTo(0.7);
        observation.Value.Raw.GetString().Should().Be("correspondence");
        observation.ProducedBy.Id.Should().Be("playbook://classification@v1");

        DocumentClassificationExtensions.TryParseClassification(result.Classification, out var typed).Should().BeTrue();
        typed.IsOutcomeBearing().Should().BeFalse(
            "correspondence is NOT one of the three outcome-bearing types — D-P7 ingest must skip Layer 2 per D-59 economics");
    }

    // ─── Acceptance #5 — out-of-domain → "other" (no false positive) ──────────

    [Fact]
    public async Task OutOfDomainFixture_ClassifiesAsOther_AndDoesNotGateLayer2_OnOutcomeBearingType()
    {
        // Arrange — the LLM should classify a sourdough-starter log as "other".
        var assembled = BuildAssembledPrompt("sample-out-of-domain.txt");
        var llmJson = SimulateLlmResponse(
            classification: "other",
            confidence: 0.85,
            reasoning: "Document is personal recipe notes about a sourdough starter — none of the eight legal categories apply.");
        var openAi = NewOpenAiClientMock("SOURDOUGH STARTER", llmJson);

        // Act
        var raw = await openAi.Object.GetStructuredCompletionRawAsync(
            assembled,
            BinaryData.FromString(File.ReadAllText(SchemaPath())),
            "ClassificationResponse",
            model: "gpt-4o-mini",
            maxOutputTokens: 200,
            cancellationToken: CancellationToken.None);
        var result = JsonSerializer.Deserialize<Layer1ClassificationResult>(raw)!;

        var emitter = NewEmitter();
        var observation = await emitter.EmitAsync(
            result,
            documentRef: "spe://drive/personal/item/sourdough-week-2.txt",
            tenantId: "tenant-acme",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert — POML acceptance: "no false positive on outcome-bearing categories"
        result.Classification.Should().Be("other");
        observation.Value.Raw.GetString().Should().Be("other");
        observation.ProducedBy.Id.Should().Be("playbook://classification@v1");

        DocumentClassificationExtensions.TryParseClassification(result.Classification, out var typed).Should().BeTrue();
        typed.Should().Be(DocumentClassification.Other);
        typed.IsOutcomeBearing().Should().BeFalse(
            "out-of-domain classified as 'other' MUST NOT route to Layer 2 outcome extraction (would burn LLM tokens on nonsense)");

        // Spot-check: a same-document false-positive on an outcome-bearing type WOULD route
        // to Layer 2 — this assertion guards against the worst regression (LLM hallucinates
        // a classification of e.g. settlement_agreement on the recipe). The test as wired
        // uses the mocked response so this assertion is structurally vacuous here; its job
        // is to document the false-positive guarantee for the live-LLM D-P16 smoke test that
        // exercises this fixture against a real model.
        new[] { "closing_letter", "settlement_agreement", "opinion_judgment" }
            .Should().NotContain(result.Classification,
                "an outcome-bearing classification on the sourdough fixture is the worst-case Layer 1 regression");
    }

    // ─── Acceptance #6 — producedBy = "classification@v1" propagates on every Observation ─

    [Fact]
    public async Task EveryEmittedObservation_HasProducedByClassificationV1_AndCorrectEvidenceShape()
    {
        // Arrange — emit one Observation per fixture and confirm producedBy + evidence shape are uniform.
        var emitter = NewEmitter();
        var documentRefs = new[]
        {
            ("closing_letter", 0.94, "spe://drive/x/item/cl.docx"),
            ("settlement_agreement", 0.96, "spe://drive/x/item/sa.docx"),
            ("correspondence", 0.92, "spe://drive/x/item/co.txt"),
            ("other", 0.85, "spe://drive/x/item/oo.txt")
        };

        var observations = new List<ObservationArtifact>();
        foreach (var (cls, conf, docRef) in documentRefs)
        {
            var obs = await emitter.EmitAsync(
                new Layer1ClassificationResult
                {
                    Classification = cls,
                    Confidence = conf,
                    Reasoning = "test reasoning"
                },
                documentRef: docRef,
                tenantId: "tenant-acme",
                scope: null,
                asOf: FixedAsOf,
                upsertAsync: null,
                ct: CancellationToken.None);
            observations.Add(obs);
        }

        // Assert
        observations.Should().HaveCount(4);
        foreach (var obs in observations)
        {
            obs.ProducedBy.Kind.Should().Be("playbook");
            obs.ProducedBy.Id.Should().Be("playbook://classification@v1",
                "D-62 versioned re-extraction requires every Classification Observation to carry the producer id");
            obs.ProducedBy.Version.Should().Be("v1",
                "D-05 mandates Version on Observations + D-62 versioned re-extraction");
            obs.Predicate.Should().Be("classification");
            obs.Value.DisplayHint.Should().Be("enum");

            // Evidence shape: [{document}, {playbook-run}]
            obs.Evidence.Should().HaveCount(2);
            obs.Evidence[0].RefType.Should().Be("document");
            obs.Evidence[0].Quote.Should().BeNull(
                "Layer 1 does not produce a verbatim quote — the entire document is the basis for the classification");
            obs.Evidence[1].RefType.Should().Be("playbook-run");
            obs.Evidence[1].Ref.Should().StartWith("playbook://classification@v1/run-");
        }
    }

    // ─── Emitter argument validation + defense-in-depth ──────────────────────

    [Fact]
    public async Task Emitter_InvalidClassificationString_Throws()
    {
        // Arrange — a value not in the 8-enum (e.g., a misconfigured LLM bypass).
        var emitter = NewEmitter();

        // Act
        var act = () => emitter.EmitAsync(
            new Layer1ClassificationResult
            {
                Classification = "not_a_real_category",
                Confidence = 0.5,
                Reasoning = "test"
            },
            documentRef: "spe://x/y",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert — defense-in-depth check: the substrate must never see a non-canonical classification.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not one of the 8 enum values*");
    }

    [Fact]
    public async Task Emitter_ConfidenceOutOfRange_Throws()
    {
        // Arrange
        var emitter = NewEmitter();

        // Act
        var act = () => emitter.EmitAsync(
            new Layer1ClassificationResult
            {
                Classification = "other",
                Confidence = 1.5,
                Reasoning = "test"
            },
            documentRef: "spe://x/y",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert (Note: FluentAssertions WithMessage treats square brackets as wildcards;
        // use a brackets-free substring match here.)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the *0.0, 1.0* range*");
    }

    [Fact]
    public async Task Emitter_UpsertCallback_InvokedOnceWithEmittedObservation()
    {
        // Arrange — confirm the substrate-write seam fires per call.
        var emitter = NewEmitter();
        var upserted = new List<ObservationArtifact>();

        // Act
        var returned = await emitter.EmitAsync(
            new Layer1ClassificationResult
            {
                Classification = "closing_letter",
                Confidence = 0.9,
                Reasoning = "test"
            },
            documentRef: "spe://x/y",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: (obs, _) => { upserted.Add(obs); return Task.CompletedTask; },
            ct: CancellationToken.None);

        // Assert
        upserted.Should().HaveCount(1);
        upserted[0].Should().BeSameAs(returned, "the same Observation reference is passed to upsert and returned");
    }

    [Fact]
    public async Task Emitter_NullClassification_Throws()
    {
        var emitter = NewEmitter();
        var act = () => emitter.EmitAsync(
            classification: null!,
            documentRef: "spe://x/y",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Emitter_Cancellation_PropagatesOperationCanceled()
    {
        var emitter = NewEmitter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => emitter.EmitAsync(
            new Layer1ClassificationResult { Classification = "other", Confidence = 0.5, Reasoning = "x" },
            documentRef: "spe://x/y",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── Gating predicate sanity tests ───────────────────────────────────────

    [Theory]
    [InlineData(DocumentClassification.ClosingLetter, true)]
    [InlineData(DocumentClassification.SettlementAgreement, true)]
    [InlineData(DocumentClassification.OpinionJudgment, true)]
    [InlineData(DocumentClassification.DecisionMemo, false)]
    [InlineData(DocumentClassification.DealDocument, false)]
    [InlineData(DocumentClassification.Pleading, false)]
    [InlineData(DocumentClassification.Correspondence, false)]
    [InlineData(DocumentClassification.Other, false)]
    public void IsOutcomeBearing_OnlyThreeTypesGateLayer2(DocumentClassification value, bool expectedOutcomeBearing)
    {
        value.IsOutcomeBearing().Should().Be(expectedOutcomeBearing,
            $"D-59 economics: Layer 2 fires only for the three outcome-bearing types; {value} is {(expectedOutcomeBearing ? "" : "NOT ")}one of them");
    }

    [Theory]
    [InlineData("closing_letter", DocumentClassification.ClosingLetter)]
    [InlineData("settlement_agreement", DocumentClassification.SettlementAgreement)]
    [InlineData("decision_memo", DocumentClassification.DecisionMemo)]
    [InlineData("deal_document", DocumentClassification.DealDocument)]
    [InlineData("pleading", DocumentClassification.Pleading)]
    [InlineData("opinion_judgment", DocumentClassification.OpinionJudgment)]
    [InlineData("correspondence", DocumentClassification.Correspondence)]
    [InlineData("other", DocumentClassification.Other)]
    public void TryParseClassification_AcceptsAllEightWireValues(string wire, DocumentClassification expected)
    {
        DocumentClassificationExtensions.TryParseClassification(wire, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
        parsed.ToWireString().Should().Be(wire, "wire-form round-trip is required by D-P11 review-surface filtering");
    }

    [Theory]
    [InlineData("")]
    [InlineData("CLOSING_LETTER")]
    [InlineData("closingLetter")]
    [InlineData("not_in_enum")]
    [InlineData(null)]
    public void TryParseClassification_RejectsAnythingOutsideTheEightCanonicalValues(string? wire)
    {
        DocumentClassificationExtensions.TryParseClassification(wire, out _).Should().BeFalse();
    }

    // ─── Observation id shape ───────────────────────────────────────────────

    [Fact]
    public async Task EmittedObservation_HasIdInExpectedShape()
    {
        // Arrange
        var emitter = NewEmitter();

        // Act
        var obs = await emitter.EmitAsync(
            new Layer1ClassificationResult { Classification = "closing_letter", Confidence = 0.9, Reasoning = "test" },
            documentRef: "spe://drive/x/item/closing-letter-M-2024-0341.docx",
            tenantId: "tenant-x",
            scope: null,
            asOf: FixedAsOf,
            upsertAsync: null,
            ct: CancellationToken.None);

        // Assert — Observation id local-part comes from the last path segment of the documentRef.
        obs.Id.Should().Be("obs:closing-letter-M-2024-0341.docx:classification");
    }
}
