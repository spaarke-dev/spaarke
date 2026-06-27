using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Insights.Nodes;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Wave C1 task 020 — unit-integration tests for the universal-ingest@v1 JPS playbook
/// components: <see cref="SanitizerNodeExecutor"/>, <see cref="ObservationEmitterNodeExecutor"/>,
/// and the two engine patches (Gap #1 EvidenceSufficiencyNode <c>predicate: "in"</c> + Gap #2
/// branch-aware skip via <see cref="EvidenceSufficiencyResult.SelectedBranch"/>).
/// </summary>
/// <remarks>
/// <para>
/// These tests exercise the executors in isolation + a focused 3-node mini-orchestration
/// (sanitize → checkLayer2Gate → emitObservations) that validates the Gap #1 + Gap #2 patches
/// without needing a full Dataverse + Azure Search round-trip. Full end-to-end smoke against a
/// real BFF + real Dataverse is deferred to deploy-time per design-a5 §7 + bff-extensions §F.4
/// (deploy coordination across parallel projects).
/// </para>
/// <para>
/// <b>Parity check (vs r1 IngestOrchestrator.cs)</b>: assertions in
/// <see cref="ObservationEmitter_ResultShape_MatchesR1InsightsIngestResult"/> verify that the
/// output shape produced by <see cref="ObservationEmitterNodeExecutor"/> mirrors the r1
/// <see cref="Sprk.Bff.Api.Models.Ai.PublicContracts.InsightsIngestResult"/> exactly, so
/// Wave C4 (task 023) can rewire <c>IInsightsAi.RunIngestAsync</c> without translation.
/// </para>
/// </remarks>
public sealed class UniversalIngestPlaybookTests
{
    // =========================================================================
    // Engine patch Gap #1 — EvidenceSufficiencyNode predicate: "in"
    // =========================================================================

    [Fact]
    public async Task EvidenceSufficiency_PredicateIn_OutcomeBearingClassification_PassesWhenInList()
    {
        // Arrange — upstream layer1 emits classification=Settlement (outcome-bearing per Phase 1 defaults).
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "layer1",
            new { classification = "Settlement", confidence = 0.85 });

        var config = """
        {
          "rules": [
            {
              "name": "outcomeBearingClassification",
              "from": "layer1",
              "readFrom": "classification",
              "predicate": "in",
              "value": ["Order", "Settlement", "Verdict", "Judgment"]
            }
          ],
          "sufficientBranch": "layer2Extract",
          "insufficientBranch": "emitObservations"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["layer1"] = upstream });

        // Act
        var node = new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict.Should().NotBeNull();
        verdict!.Sufficient.Should().BeTrue();
        verdict.SelectedBranch.Should().Be("layer2Extract");
        verdict.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceSufficiency_PredicateIn_OutcomeBearingClassification_FailsWhenNotInList()
    {
        // Arrange — upstream layer1 emits classification=Correspondence (NOT outcome-bearing).
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "layer1",
            new { classification = "Correspondence", confidence = 0.95 });

        var config = """
        {
          "rules": [
            {
              "name": "outcomeBearingClassification",
              "from": "layer1",
              "readFrom": "classification",
              "predicate": "in",
              "value": ["Order", "Settlement", "Verdict", "Judgment"]
            }
          ],
          "sufficientBranch": "layer2Extract",
          "insufficientBranch": "emitObservations"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["layer1"] = upstream });

        // Act
        var node = new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        // Assert — insufficient verdict routes to emitObservations (L1-only path).
        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict!.Sufficient.Should().BeFalse();
        verdict.SelectedBranch.Should().Be("emitObservations");
        verdict.Gaps.Should().ContainSingle();
        verdict.Gaps[0].RuleName.Should().Be("outcomeBearingClassification");
        verdict.Gaps[0].Reason.Should().Contain("Correspondence");
    }

    [Fact]
    public void EvidenceSufficiency_PredicateIn_ValidationRejectsMissingValueOrReadFrom()
    {
        var configMissingValue = """
        {
          "rules": [
            { "name": "bad", "from": "layer1", "readFrom": "classification", "predicate": "in" }
          ]
        }
        """;
        var contextMissingValue = InsightsNodeTestHelpers.CreateContext(
            ActionType.EvidenceSufficiency, configMissingValue);

        var node = new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance);
        var validation = node.Validate(contextMissingValue);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("requires a 'value' field"));
    }

    [Fact]
    public void EvidenceSufficiency_PredicateUnknown_ValidationRejects()
    {
        var config = """
        {
          "rules": [
            { "name": "bad", "from": "layer1", "readFrom": "classification", "predicate": "eq", "value": ["X"] }
          ]
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(ActionType.EvidenceSufficiency, config);
        var node = new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance);

        var validation = node.Validate(context);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("Only 'in' is implemented"));
    }

    [Fact]
    public async Task EvidenceSufficiency_ExistingMinCountRule_StillWorks_NoRegression()
    {
        // Regression check — predict-matter-cost@v1's minCount rule shape must continue to work.
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "retrieveCohort", new { count = 15 });
        var config = """
        {
          "rules": [
            { "name": "comparableMatters", "from": "retrieveCohort", "countFrom": "count", "minCount": 12 }
          ],
          "sufficientBranch": "synthesize",
          "insufficientBranch": "decline"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["retrieveCohort"] = upstream });

        var node = new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict!.Sufficient.Should().BeTrue();
        verdict.SelectedBranch.Should().Be("synthesize");
    }

    // =========================================================================
    // SanitizerNodeExecutor
    // =========================================================================

    [Fact]
    public async Task Sanitizer_HappyPath_EmitsSanitizedTextAndChunksPassthrough()
    {
        // Arrange — fake sanitizer that returns predictable output.
        var sanitizer = new Mock<IInsightsContentSanitizer>(MockBehavior.Strict);
        sanitizer.Setup(s => s.SanitizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanitizationResult(
                SanitizedText: "Clean text from sanitizer.",
                OriginalLength: 32,
                SanitizedLength: 27,
                WasTruncated: false,
                HadInjectionPrefix: false));

        var executor = new SanitizerNodeExecutor(sanitizer.Object, NullLogger<SanitizerNodeExecutor>.Instance);

        var chunksJson = """[{"id":"c1","text":"raw chunk 1"},{"id":"c2","text":"raw chunk 2"}]""";
        var parameters = new Dictionary<string, string>
        {
            ["documentText"] = "Raw document text with noise.",
            ["chunksJson"] = chunksJson,
            ["documentRef"] = "doc:M-1234:test.pdf"
        };

        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.Sanitization,
            configJson: null,
            outputVariable: "sanitization",
            parameters: parameters);

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var output = result.GetData<SanitizationNodeOutput>();
        output.Should().NotBeNull();
        output!.SanitizedText.Should().Be("Clean text from sanitizer.");
        output.OriginalLength.Should().Be(32);
        output.DocumentRef.Should().Be("doc:M-1234:test.pdf");
        // Chunks passthrough — JSON shape preserved.
        output.Chunks.ValueKind.Should().Be(JsonValueKind.Array);
        output.Chunks.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Sanitizer_EmptySanitizedText_ReturnsErrorSkippingDownstream()
    {
        // Arrange — sanitizer returns empty (per r1 IngestOrchestrator early-return semantic).
        var sanitizer = new Mock<IInsightsContentSanitizer>(MockBehavior.Strict);
        sanitizer.Setup(s => s.SanitizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanitizationResult("", 50, 0, false, false));

        var executor = new SanitizerNodeExecutor(sanitizer.Object, NullLogger<SanitizerNodeExecutor>.Instance);
        var parameters = new Dictionary<string, string> { ["documentText"] = "Only retrieval blocks." };
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.Sanitization, configJson: null, parameters: parameters);

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SANITIZE_EMPTY");
    }

    [Fact]
    public void Sanitizer_MissingDocumentTextParam_ValidationFails()
    {
        var sanitizer = new Mock<IInsightsContentSanitizer>();
        var executor = new SanitizerNodeExecutor(sanitizer.Object, NullLogger<SanitizerNodeExecutor>.Instance);

        var context = InsightsNodeTestHelpers.CreateContext(ActionType.Sanitization, configJson: null);

        var validation = executor.Validate(context);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("documentText"));
    }

    // =========================================================================
    // ObservationEmitterNodeExecutor
    // =========================================================================

    [Fact]
    public async Task ObservationEmitter_SufficientPath_EmitsLayer1PlusLayer2Observations()
    {
        // Arrange — grounded upstream with 2 surviving candidates; layer1 with Settlement classification.
        // Use object[] explicit array so the mixed value-types (string vs int) do not break inference.
        var groundedCandidates = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                fieldName = "outcomeCategory",
                value = (object)"Settlement",
                quote = "The parties agreed to settle for $250,000.",
                confidence = 0.92,
                displayHint = "enum"
            },
            new
            {
                fieldName = "settlementAmount",
                value = (object)250000,
                quote = "$250,000.",
                confidence = 0.95,
                displayHint = "currency-usd"
            }
        });

        var grounded = NodeOutput.Ok(Guid.NewGuid(), "grounded", new { candidates = groundedCandidates });
        var layer1 = NodeOutput.Ok(Guid.NewGuid(), "layer1",
            new { classification = "Settlement", confidence = 0.85 });

        var emittedObservations = new List<ObservationArtifact>
        {
            CreateMinimalObservation("outcomeCategory"),
            CreateMinimalObservation("settlementAmount")
        };
        var emitter = new Mock<IObservationEmitter>();
        emitter.Setup(e => e.EmitFromExtractionAsync(
                It.IsAny<ExtractionResult>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emittedObservations);

        var upserter = new Mock<IObservationIndexUpserter>();
        var mirror = new Mock<IObservationMirror>();
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        var executor = new ObservationEmitterNodeExecutor(
            emitter.Object,
            upserter.Object,
            mirror.Object,
            timeProvider.Object,
            NullLogger<ObservationEmitterNodeExecutor>.Instance);

        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = "M-1234",
            ["tenantId"] = "tenant-A",
            ["documentRef"] = "doc:M-1234:settlement.pdf"
        };

        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ObservationEmit,
            configJson: null,
            outputVariable: "emission",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["grounded"] = grounded,
                ["layer1"] = layer1
            },
            parameters: parameters);

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var output = result.GetData<ObservationEmissionResult>();
        output.Should().NotBeNull();
        output!.ObservationsEmitted.Should().Be(3); // 1 L1 + 2 L2 candidates
        output.Layer1Classification.Should().Be("Settlement");
        output.Layer2Triggered.Should().BeTrue();

        // Emitter called once with extraction containing both fields.
        emitter.Verify(e => e.EmitFromExtractionAsync(
            It.Is<ExtractionResult>(er =>
                er.Subject == "matter:M-1234"
                && er.TenantId == "tenant-A"
                && er.Fields.Count == 2
                && er.Fields.ContainsKey("outcomeCategory")
                && er.Fields.ContainsKey("settlementAmount")),
            It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObservationEmitter_InsufficientPath_GroundedNull_EmitsOnlyLayer1()
    {
        // Arrange — checkLayer2Gate=insufficient → groundingVerify skipped → grounded upstream is null/skipped.
        // Per Wave C1 Gap #2 patch semantics, the upstream is Ok(null) (skip-success).
        var groundedSkipped = NodeOutput.Ok(Guid.NewGuid(), "grounded", null,
            textContent: "Branch not selected");
        var layer1 = NodeOutput.Ok(Guid.NewGuid(), "layer1",
            new { classification = "Correspondence", confidence = 0.95 });

        var emitter = new Mock<IObservationEmitter>(MockBehavior.Strict); // strict — should NOT be called
        var upserter = new Mock<IObservationIndexUpserter>();
        var mirror = new Mock<IObservationMirror>();
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        var executor = new ObservationEmitterNodeExecutor(
            emitter.Object, upserter.Object, mirror.Object, timeProvider.Object,
            NullLogger<ObservationEmitterNodeExecutor>.Instance);

        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = "M-1234",
            ["tenantId"] = "tenant-A",
            ["documentRef"] = "doc:M-1234:letter.pdf"
        };

        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ObservationEmit,
            configJson: null,
            outputVariable: "emission",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["grounded"] = groundedSkipped,
                ["layer1"] = layer1
            },
            parameters: parameters);

        // Act
        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — L1 only (count = 1), L2 not triggered, emitter not invoked.
        result.Success.Should().BeTrue();
        var output = result.GetData<ObservationEmissionResult>();
        output!.ObservationsEmitted.Should().Be(1);
        output.Layer1Classification.Should().Be("Correspondence");
        output.Layer2Triggered.Should().BeFalse();

        emitter.Verify(e => e.EmitFromExtractionAsync(
            It.IsAny<ExtractionResult>(),
            It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObservationEmitter_ResultShape_MatchesR1InsightsIngestResult()
    {
        // Parity check (vs r1 IngestOrchestrator.cs final return). The shape MUST mirror
        // InsightsIngestResult exactly so Wave C4 IInsightsAi.RunIngestAsync can return without
        // translation.
        var layer1 = NodeOutput.Ok(Guid.NewGuid(), "layer1",
            new { classification = "Settlement", confidence = 0.85 });
        var grounded = NodeOutput.Ok(Guid.NewGuid(), "grounded",
            new { candidates = new List<object>() }); // 0 surviving fields → 0 L2 obs

        var emitter = new Mock<IObservationEmitter>();
        emitter.Setup(e => e.EmitFromExtractionAsync(
                It.IsAny<ExtractionResult>(),
                It.IsAny<Func<ObservationArtifact, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ObservationArtifact>());

        var executor = new ObservationEmitterNodeExecutor(
            emitter.Object,
            new Mock<IObservationIndexUpserter>().Object,
            new Mock<IObservationMirror>().Object,
            TimeProvider.System,
            NullLogger<ObservationEmitterNodeExecutor>.Instance);

        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = "M-1234",
            ["tenantId"] = "tenant-A",
            ["documentRef"] = "doc:M-1234:order.pdf"
        };

        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ObservationEmit,
            configJson: null,
            outputVariable: "emission",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["grounded"] = grounded,
                ["layer1"] = layer1
            },
            parameters: parameters);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);
        var output = result.GetData<ObservationEmissionResult>();

        // Shape parity: r1 InsightsIngestResult = (int ObservationsEmitted, string? Layer1Classification, bool Layer2Triggered)
        output.Should().NotBeNull();
        output!.GetType().GetProperty("ObservationsEmitted").Should().NotBeNull();
        output.GetType().GetProperty("Layer1Classification").Should().NotBeNull();
        output.GetType().GetProperty("Layer2Triggered").Should().NotBeNull();
        // 1 L1 + 0 L2 = 1 (L2 was triggered because grounded was Ok with candidates array, even if empty).
        output.ObservationsEmitted.Should().Be(1);
        output.Layer1Classification.Should().Be("Settlement");
    }

    [Fact]
    public void ObservationEmitter_MissingRequiredParam_ValidationFails()
    {
        var executor = new ObservationEmitterNodeExecutor(
            new Mock<IObservationEmitter>().Object,
            new Mock<IObservationIndexUpserter>().Object,
            new Mock<IObservationMirror>().Object,
            TimeProvider.System,
            NullLogger<ObservationEmitterNodeExecutor>.Instance);

        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ObservationEmit, configJson: null,
            parameters: new Dictionary<string, string> { ["tenantId"] = "tenant-A" }); // missing matterId

        var validation = executor.Validate(context);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("matterId"));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ObservationArtifact CreateMinimalObservation(string predicate)
    {
        return new ObservationArtifact
        {
            Id = $"obs:{Guid.NewGuid()}",
            Subject = "matter:M-1234",
            Predicate = predicate,
            Value = new Value
            {
                Raw = JsonSerializer.SerializeToElement("test"),
                DisplayHint = "text"
            },
            Confidence = 0.9,
            Evidence = Array.Empty<EvidenceRef>(),
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://outcome-extraction@v1",
                Version = "v1"
            },
            Scope = new Scope
            {
                TenantId = "tenant-A",
                MatterId = "M-1234"
            },
            AsOf = DateTimeOffset.UtcNow,
            TenantId = "tenant-A"
        };
    }
}
