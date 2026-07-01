using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Mini-playbook integration tests wiring multiple D-P12 (task 022) nodes end-to-end against
/// in-process mocks. Exercises:
///  - <see cref="LiveFactNode"/> →
///  - <see cref="EvidenceSufficiencyNode"/> →
///  - <see cref="DeclineToFindNode"/> OR <see cref="ReturnInsightArtifactNode"/>
///
/// Also verifies <see cref="NodeExecutorRegistry"/> dispatch by ExecutorType so the registry
/// contract (auto-discovery via DI's <c>IEnumerable&lt;INodeExecutor&gt;</c> pattern) is
/// covered end-to-end, not just per-node.
/// </summary>
public sealed class InsightsNodesIntegrationTests
{
    [Fact]
    public void NodeExecutorRegistry_ResolvesAllFiveNewActionTypes()
    {
        var registry = BuildRegistry(out _, out _);

        registry.HasExecutor(ExecutorType.LiveFact).Should().BeTrue();
        registry.HasExecutor(ExecutorType.IndexRetrieve).Should().BeTrue();
        registry.HasExecutor(ExecutorType.EvidenceSufficiency).Should().BeTrue();
        registry.HasExecutor(ExecutorType.DeclineToFind).Should().BeTrue();
        registry.HasExecutor(ExecutorType.ReturnInsightArtifact).Should().BeTrue();

        // Task 020's GroundingVerify must still resolve too — we did NOT renumber it.
        // (Not explicitly registered here, just verifying the new five did not clobber it.)
        registry.GetExecutor(ExecutorType.LiveFact).Should().BeOfType<LiveFactNode>();
        registry.GetExecutor(ExecutorType.IndexRetrieve).Should().BeOfType<IndexRetrieveNode>();
        registry.GetExecutor(ExecutorType.EvidenceSufficiency).Should().BeOfType<EvidenceSufficiencyNode>();
        registry.GetExecutor(ExecutorType.DeclineToFind).Should().BeOfType<DeclineToFindNode>();
        registry.GetExecutor(ExecutorType.ReturnInsightArtifact).Should().BeOfType<ReturnInsightArtifactNode>();
    }

    /// <summary>
    /// Three-node chain: LiveFact → EvidenceSufficiency → DeclineToFind.
    /// Simulates the predict-matter-cost insufficient-evidence path: a Live Fact resolves the
    /// matter, sufficiency runs against a (deliberately empty) comparable-matters store, and
    /// DeclineToFind emits the structured response.
    /// </summary>
    [Fact]
    public async Task MiniPlaybook_InsufficientEvidence_EmitsStructuredDecline()
    {
        var registry = BuildRegistry(out var resolverMock, out _);

        // Step 1 — LiveFactNode
        // r2 Wave D5 (task 034): subjects MUST now parse as <scheme>:<guid> per design-a6
        // §2.1 (the dispatcher's ISubjectParser rejects non-GUID forms before reaching the
        // resolver). The r1 legacy stub-id "matter:M-1234" is replaced with a real GUID; the
        // mock setup is wired to that subject so the LiveFactNode dispatch correctly resolves.
        const string MatterSubject = "matter:11111111-1111-1111-1111-111111111111";
        var liveFact = new FactArtifact
        {
            Id = $"fact:{MatterSubject}:matterType",
            Subject = MatterSubject,
            Predicate = "matterType",
            Value = new Value
            {
                Raw = JsonDocument.Parse("\"IP-licensing\"").RootElement,
                DisplayHint = "enum"
            },
            Evidence = new[] { new EvidenceRef { RefType = "fact-source", Ref = $"dataverse://sprk_matter/11111111-1111-1111-1111-111111111111#matterType" } },
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "query", Id = "query://matter-type", Version = "v1" },
            Scope = new Scope { TenantId = InsightsNodeTestHelpers.DefaultTenantId },
            TenantId = InsightsNodeTestHelpers.DefaultTenantId
        };
        resolverMock.Setup(r => r.ResolveAsync(MatterSubject, "matterType", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveFact);

        var liveFactNode = registry.GetExecutor(ExecutorType.LiveFact)!;
        var liveFactCtx = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.LiveFact,
            $$"""{ "subject": "{{MatterSubject}}", "predicate": "matterType" }""",
            outputVariable: "matterType");
        var liveFactOut = await liveFactNode.ExecuteAsync(liveFactCtx, CancellationToken.None);
        liveFactOut.Success.Should().BeTrue();

        // Step 2 — EvidenceSufficiencyNode (insufficient: only 4 comparable matters, need 12)
        var comparableMatters = NodeOutput.Ok(Guid.NewGuid(), "retrieveComparableMatters", new { count = 4 });
        var sufficiencyNode = registry.GetExecutor(ExecutorType.EvidenceSufficiency)!;
        var sufficiencyCtx = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.EvidenceSufficiency,
            """
            {
              "rules": [ { "name": "comparableMatters", "from": "retrieveComparableMatters", "minCount": 12 } ],
              "sufficientBranch": "synthesize",
              "insufficientBranch": "decline"
            }
            """,
            outputVariable: "checkSufficiency",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["matterType"] = liveFactOut,
                ["retrieveComparableMatters"] = comparableMatters
            });
        var sufficiencyOut = await sufficiencyNode.ExecuteAsync(sufficiencyCtx, CancellationToken.None);
        sufficiencyOut.Success.Should().BeTrue();
        sufficiencyOut.GetData<EvidenceSufficiencyResult>()!.Sufficient.Should().BeFalse();

        // Step 3 — DeclineToFindNode
        var declineNode = registry.GetExecutor(ExecutorType.DeclineToFind)!;
        var declineCtx = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.DeclineToFind,
            """
            {
              "from": "checkSufficiency",
              "explanationTemplate": "Only {have} comparable matters were found; {need} required.",
              "suggestedActions": [ "Broaden matter-type filter" ],
              "confidenceInDecline": 0.92
            }
            """,
            outputVariable: "decline",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["checkSufficiency"] = sufficiencyOut
            });
        var declineOut = await declineNode.ExecuteAsync(declineCtx, CancellationToken.None);

        declineOut.Success.Should().BeTrue();
        var decline = declineOut.GetData<DeclineResponse>();
        decline.Should().NotBeNull();
        decline!.Reason.Should().Be("insufficient-evidence");
        decline.Explanation.Should().Be("Only 4 comparable matters were found; 12 required.");
        decline.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters");
        decline.ConfidenceInDecline.Should().Be(0.92);
    }

    /// <summary>
    /// Three-node chain: synthesize-output → ReturnInsightArtifact (sufficient path).
    /// Verifies the envelope assembly + EvidenceGuard pass through end-to-end.
    /// </summary>
    [Fact]
    public async Task MiniPlaybook_SufficientEvidence_EmitsInferenceArtifact()
    {
        var registry = BuildRegistry(out _, out _);

        // Simulate upstream synthesis result.
        var synth = new
        {
            value = 280000,
            evidence = new[]
            {
                new { refType = "comparable-matter", @ref = "matter://M-0567" },
                new { refType = "comparable-matter", @ref = "matter://M-0789" }
            },
            confidence = 0.74,
            reasoning = "Median of 12 comparable matters."
        };
        var synthOut = NodeOutput.Ok(Guid.NewGuid(), "synthesize", synth, confidence: 0.74);

        var returnNode = registry.GetExecutor(ExecutorType.ReturnInsightArtifact)!;
        var ctx = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.ReturnInsightArtifact,
            """
            {
              "from": "synthesize",
              "artifactKind": "inference",
              "subject": "matter:M-1234",
              "predicate": "predictedCost",
              "displayHint": "currency-usd",
              "producedById": "playbook://predict-matter-cost@v1",
              "producedByVersion": "v1"
            }
            """,
            outputVariable: "final",
            previousOutputs: new Dictionary<string, NodeOutput> { ["synthesize"] = synthOut });

        var result = await returnNode.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var artifact = result.GetData<InferenceArtifact>();
        artifact.Should().NotBeNull();
        artifact!.Predicate.Should().Be("predictedCost");
        artifact.Confidence.Should().Be(0.74);
        artifact.Evidence.Should().HaveCount(2);
        artifact.ProducedBy.Version.Should().Be("v1");
    }

    /// <summary>
    /// Builds a <see cref="NodeExecutorRegistry"/> over the five new task-022 executors so the
    /// integration tests can exercise the IEnumerable&lt;INodeExecutor&gt; DI pattern without
    /// spinning up the full AnalysisServicesModule.
    /// </summary>
    /// <remarks>
    /// r2 Wave D5 (task 034): <see cref="LiveFactNode"/> now consumes a multi-entity resolver
    /// dictionary (matter / project / invoice) + <see cref="ISubjectParser"/> per design-a6
    /// §3.4. This helper wires the matter scheme to the supplied resolver mock so existing
    /// matter-scoped tests continue to work unchanged.
    /// </remarks>
    private static INodeExecutorRegistry BuildRegistry(
        out Mock<ILiveFactResolver> resolverMock,
        out Mock<IOpenAiClient> openAiMock)
    {
        resolverMock = new Mock<ILiveFactResolver>();
        openAiMock = new Mock<IOpenAiClient>();
        var searchIndexClient = new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>();

        // r2 Wave D5: build the resolver registry expected by LiveFactNode. The same mock
        // backs all three entity-type keys so existing single-resolver test assertions remain
        // valid for matter subjects; project/invoice subjects route through the same mock too,
        // which is harmless because the test setups always specify the exact subject string.
        var resolvers = new Dictionary<string, ILiveFactResolver>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = resolverMock.Object,
            ["project"] = resolverMock.Object,
            ["invoice"] = resolverMock.Object
        };
        var subjectParser = new SubjectParser(
            Microsoft.Extensions.Options.Options.Create(new SubjectSchemeCatalogOptions()));

        var executors = new INodeExecutor[]
        {
            new LiveFactNode(resolvers, subjectParser, NullLogger<LiveFactNode>.Instance),
            new IndexRetrieveNode(searchIndexClient.Object, openAiMock.Object, NullLogger<IndexRetrieveNode>.Instance),
            new EvidenceSufficiencyNode(NullLogger<EvidenceSufficiencyNode>.Instance),
            new DeclineToFindNode(NullLogger<DeclineToFindNode>.Instance),
            new ReturnInsightArtifactNode(NullLogger<ReturnInsightArtifactNode>.Instance)
        };

        return new NodeExecutorRegistry(executors, NullLogger<NodeExecutorRegistry>.Instance);
    }
}
