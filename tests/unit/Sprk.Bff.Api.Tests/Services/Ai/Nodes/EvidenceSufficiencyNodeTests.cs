using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="EvidenceSufficiencyNode"/> (D-P12 task 022). Covers the
/// predict-matter-cost canonical rule (minComparableMatters: 12) — sufficient + insufficient
/// paths + validation errors.
/// </summary>
public sealed class EvidenceSufficiencyNodeTests
{
    private static EvidenceSufficiencyNode CreateNode() =>
        new(NullLogger<EvidenceSufficiencyNode>.Instance);

    [Fact]
    public void SupportedActionTypes_ContainsEvidenceSufficiency()
    {
        CreateNode().SupportedActionTypes.Should()
            .ContainSingle().Which.Should().Be(ExecutorType.EvidenceSufficiency);
    }

    [Fact]
    public async Task ExecuteAsync_SufficientMatters_VerdictSufficient()
    {
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "retrieveComparableMatters",
            new { count = 15, items = new[] { 1, 2, 3 } });

        var config = """
        {
          "rules": [
            { "name": "comparableMatters", "from": "retrieveComparableMatters", "minCount": 12 }
          ],
          "sufficientBranch": "synthesize",
          "insufficientBranch": "decline"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["retrieveComparableMatters"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict.Should().NotBeNull();
        verdict!.Sufficient.Should().BeTrue();
        verdict.SelectedBranch.Should().Be("synthesize");
        verdict.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_BelowThreshold_VerdictInsufficientWithGap()
    {
        // Per SPEC §3.4.3 worked example — 4 comparable matters, minimum is 12.
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "retrieveComparableMatters", new { count = 4 });

        var config = """
        {
          "rules": [
            { "name": "comparableMatters", "from": "retrieveComparableMatters", "minCount": 12 }
          ],
          "insufficientBranch": "decline"
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["retrieveComparableMatters"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict.Should().NotBeNull();
        verdict!.Sufficient.Should().BeFalse();
        verdict.SelectedBranch.Should().Be("decline");
        verdict.Gaps.Should().ContainSingle();
        verdict.Gaps[0].RuleName.Should().Be("comparableMatters");
        verdict.Gaps[0].Have.Should().Be(4);
        verdict.Gaps[0].Need.Should().Be(12);
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamMissing_GapEmittedRequirementNotMet()
    {
        var config = """{ "rules": [ { "name": "missing", "from": "neverProduced", "minCount": 1 } ] }""";
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.EvidenceSufficiency, config);

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict!.Sufficient.Should().BeFalse();
        verdict.Gaps[0].Reason.Should().Contain("was not found");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidConfig_ReturnsValidationError()
    {
        var node = CreateNode();
        var context = InsightsNodeTestHelpers.CreateContext(ExecutorType.EvidenceSufficiency, "{}");

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_RequireNonEmpty_ReadsArtifactsArrayLength()
    {
        // IndexRetrieveNode emits its rows under "artifacts" — the fallback enumeration should
        // find it without an explicit countFrom path.
        var upstream = NodeOutput.Ok(Guid.NewGuid(), "retrievePrecedent",
            new { indexName = "spaarke-insights-index", artifacts = new[] { new { id = "p:1" }, new { id = "p:2" } } });

        var config = """
        {
          "rules": [
            { "name": "confirmedPrecedent", "from": "retrievePrecedent", "requireNonEmpty": true }
          ]
        }
        """;
        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.EvidenceSufficiency,
            config,
            previousOutputs: new Dictionary<string, NodeOutput> { ["retrievePrecedent"] = upstream });

        var result = await CreateNode().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        var verdict = result.GetData<EvidenceSufficiencyResult>();
        verdict!.Sufficient.Should().BeTrue();
    }
}
