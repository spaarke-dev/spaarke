using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="ReturnInsightArtifactNode"/> (D-P12 task 022). Verifies envelope
/// assembly + the D-A23 / D-48 EvidenceGuard that prevents empty-evidence Inferences from
/// reaching the wire.
/// </summary>
public sealed class ReturnInsightArtifactNodeTests
{
    private static ReturnInsightArtifactNode CreateNode() =>
        new(NullLogger<ReturnInsightArtifactNode>.Instance);

    [Fact]
    public void SupportedActionTypes_ContainsReturnInsightArtifact()
    {
        CreateNode().SupportedActionTypes.Should()
            .ContainSingle().Which.Should().Be(ActionType.ReturnInsightArtifact);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPathInference_EmitsTypedInsightArtifactWithEvidence()
    {
        // Upstream synthesis emitted a value + evidence array + confidence + reasoning.
        var synth = new
        {
            value = 280000,
            evidence = new[]
            {
                new { refType = "comparable-matter", @ref = "matter://M-0567" },
                new { refType = "comparable-matter", @ref = "matter://M-0789" }
            },
            confidence = 0.74,
            reasoning = "Median of 12 comparable matters; clustered around $280K with 15% spread."
        };
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "synthesize", synth);

        var config = """
        {
          "from": "synthesize",
          "artifactKind": "inference",
          "id": "inf:predict-cost:M-1234:{runId}",
          "subject": "matter:M-1234",
          "predicate": "predictedCost",
          "displayHint": "currency-usd",
          "producedById": "playbook://predict-matter-cost@v1",
          "producedByKind": "playbook",
          "producedByVersion": "v1"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ReturnInsightArtifact,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["synthesize"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var artifact = result.GetData<InferenceArtifact>();
        artifact.Should().NotBeNull();
        artifact!.Subject.Should().Be("matter:M-1234");
        artifact.Predicate.Should().Be("predictedCost");
        artifact.Confidence.Should().Be(0.74);
        artifact.Reasoning.Should().Contain("12 comparable matters");
        artifact.Evidence.Should().HaveCount(2);
        artifact.ProducedBy.Version.Should().Be("v1");
        artifact.TenantId.Should().Be(InsightsNodeTestHelpers.DefaultTenantId);
        artifact.Value.DisplayHint.Should().Be("currency-usd");
        artifact.Id.Should().StartWith("inf:predict-cost:M-1234:");
    }

    [Fact]
    public async Task ExecuteAsync_EvidenceGuard_RejectsEmptyEvidenceInference()
    {
        // Synthesis emitted a value but no evidence — EvidenceGuard MUST reject.
        var synth = new { value = 280000, evidence = Array.Empty<object>(), confidence = 0.8 };
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "synthesize", synth);

        var config = """
        {
          "from": "synthesize",
          "artifactKind": "inference",
          "subject": "matter:M-1234",
          "predicate": "predictedCost",
          "producedById": "playbook://predict-matter-cost@v1"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ReturnInsightArtifact,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["synthesize"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ReturnInsightArtifactNode.EvidenceRequiredErrorCode);
        result.ErrorMessage.Should().Contain("EvidenceGuard");
    }

    [Fact]
    public async Task ExecuteAsync_EvidenceGuardAllowEmptyForFact_AllowsEmptyEvidence()
    {
        var synth = new { value = 287, evidence = Array.Empty<object>() };
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "synthesize", synth);

        var config = """
        {
          "from": "synthesize",
          "artifactKind": "fact",
          "subject": "matter:M-1234",
          "predicate": "matterDurationDays",
          "displayHint": "duration-days",
          "producedById": "query://matter-duration"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ReturnInsightArtifact,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["synthesize"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        // Facts have a relaxed evidence rule per design.md §2.1.
        result.Success.Should().BeTrue();
        result.GetData<FactArtifact>().Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamFailed_DependencyFailedError()
    {
        var upstream = NodeOutput.Error(Guid.NewGuid(), "synthesize", "boom");
        var config = """
        {
          "from": "synthesize",
          "subject": "matter:M-1234",
          "predicate": "predictedCost",
          "producedById": "playbook://predict-matter-cost@v1"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ReturnInsightArtifact,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["synthesize"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.DependencyFailed);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredConfig_ValidationError()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ActionType.ReturnInsightArtifact,
            """{ "from": "synthesize" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("predicate");
    }
}
