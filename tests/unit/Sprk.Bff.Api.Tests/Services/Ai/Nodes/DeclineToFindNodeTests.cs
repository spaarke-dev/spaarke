using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="DeclineToFindNode"/> (D-P12 task 022). Verifies the deterministic
/// emission of <see cref="DeclineResponse"/> per D-49 (LAVERN Pattern #7) and that the
/// structured shape is never traded for free-form prose.
/// </summary>
public sealed class DeclineToFindNodeTests
{
    private static DeclineToFindNode CreateNode() =>
        new(NullLogger<DeclineToFindNode>.Instance);

    [Fact]
    public void SupportedActionTypes_ContainsDeclineToFind()
    {
        CreateNode().SupportedActionTypes.Should()
            .ContainSingle().Which.Should().Be(ExecutorType.DeclineToFind);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_EmitsStructuredDeclineResponse()
    {
        // Upstream EvidenceSufficiencyNode said insufficient with a comparable-matters gap.
        var sufficiencyResult = new EvidenceSufficiencyResult
        {
            Sufficient = false,
            SelectedBranch = "decline",
            Gaps = new[]
            {
                new EvidenceGap
                {
                    RuleName = "comparableMatters",
                    From = "retrieveComparableMatters",
                    Reason = "Upstream had 4 items; minCount=12.",
                    Have = 4,
                    Need = 12
                }
            }
        };
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "checkSufficiency", sufficiencyResult);

        var config = """
        {
          "reason": "insufficient-evidence",
          "from": "checkSufficiency",
          "explanationTemplate": "Only {have} comparable matters were found; {need} are required.",
          "suggestedActions": [ "Broaden the matter-type filter", "Author a Precedent" ],
          "confidenceInDecline": 0.95
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.DeclineToFind,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["checkSufficiency"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Confidence.Should().Be(0.95);

        var decline = result.GetData<DeclineResponse>();
        decline.Should().NotBeNull();
        decline!.Reason.Should().Be("insufficient-evidence");
        decline.Explanation.Should().Be("Only 4 comparable matters were found; 12 are required.");
        decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters");
        decline.SuggestedActions.Should().HaveCount(2);
        decline.ConfidenceInDecline.Should().Be(0.95);
    }

    [Fact]
    public async Task ExecuteAsync_MissingFromConfig_ReturnsValidationError()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.DeclineToFind,
            """{ "reason": "insufficient-evidence" }""");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_NoUpstreamGapsAndNoTemplate_FallsBackToGenericExplanation()
    {
        // Tolerant degradation: missing upstream should NOT throw — emits generic decline.
        var config = """{ "from": "missingUpstream" }""";
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.DeclineToFind, config);

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var decline = result.GetData<DeclineResponse>();
        decline!.Reason.Should().Be(DeclineToFindNode.DefaultReason);
        decline.Explanation.Should().NotBeNullOrEmpty();
        decline.MinimumEvidenceNeeded.Should().BeEmpty();
    }
}
