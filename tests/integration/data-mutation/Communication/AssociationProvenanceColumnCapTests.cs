using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Communication;

/// <summary>
/// spaarkeai-word-add-in-r1 task 140 (ISS-043, #3194): the provenance a filing writes to <c>sprk_associationprovenance</c>
/// (10,000 characters) keeps its candidates when the full trail runs past the column. The dev row of ISS-043 carried
/// <c>"candidates": []</c> and <c>"signals": []</c> although ExplicitReference and RecordNameMatch fired and the decision's top
/// confidence was 0.99 — the shape of the writer's old compact fallback, which dropped every candidate and signal at once, so
/// the review panel showed "No confident match" for a row with a matter written to it.
/// </summary>
/// <remarks>Through the resolver's public field build (<see cref="IncomingAssociationResolver.BuildNewRecordFieldsAsync"/>),
/// which every filing path's write goes through; only the Dataverse boundary is doubled (ADR-038 §4). Ids are synthetic.</remarks>
[Trait("status", "task-140-word-add-in-r1")]
public sealed class AssociationProvenanceColumnCapTests
{
    private const int ColumnLength = 10000;
    private static readonly Guid WrittenMatter = Guid.Parse("14000000-0000-4000-8000-0000000000b1");

    private static IncomingAssociationResolver Resolver()
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        dataverse.Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Guid id, string[] _, CancellationToken _) => new Entity(name, id) { ["sprk_name"] = "Acme v Beta" });
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        return new IncomingAssociationResolver(
            Array.Empty<IAssociationRung>(), dataverse.Object, dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), new RecordOwnershipResolverDouble(), NullLogger<IncomingAssociationResolver>.Instance);
    }

    /// <summary>A long RecordNameMatch candidate, as a body full of party names produces (about 600 characters each).</summary>
    private static CandidateTrace NameMatch(int i, double confidence, bool written = false, Guid? id = null) => new()
    {
        Field = "sprk_regardingmatter",
        TargetEntity = "sprk_matter",
        TargetId = (id ?? Guid.Parse($"14000000-0000-4000-8000-{i:D12}")).ToString("D"),
        ReinforcedConfidence = confidence,
        DeterministicConfidence = 0.65,
        Written = written,
        Conflict = false,
        Contributors =
        [
            new ContributorTrace
            {
                Rung = "RecordNameMatch",
                Confidence = confidence,
                Provenance = $"record-name-match:sprk_matter:where=body:matched=name:name=\"Party {i} Holdings International " +
                    $"Limited v Counterparty {i} Group\":number=\"LIT-2026-{i:D6}\":reason=\"{new string('r', 260)}\"",
            },
            new ContributorTrace { Rung = "ParticipantCorrelation", Confidence = 0.65, Provenance = $"participant:sender:contact:{i}" },
        ],
    };

    private static SignalTrace Signal(int i) => new()
    {
        Category = "matter-update",
        Confidence = 0.9,
        Provenance = $"ai-classify:types=[sprk_matter]:reason=\"{new string('s', 200)}\":{i}",
        Obligations = ["deadline-response"],
    };

    private static AssociationDecision Decision(IReadOnlyList<CandidateTrace> candidates, IReadOnlyList<SignalTrace> signals) => new()
    {
        Status = AssociationStatusCodes.Suggested,
        RegardingWrites = new Dictionary<string, EntityReference> { ["sprk_regardingmatter"] = new("sprk_matter", WrittenMatter) },
        AutoFiled = false,
        Provenance = new AssociationProvenance
        {
            Direction = "Incoming",
            Decision = new AssociationDecisionTrace
            {
                Status = "Suggested",
                AutoFiled = false,
                KillSwitchEnabled = true,
                AutoFileThreshold = 0.85,
                TopDeterministicConfidence = 0.65,
                TopConfidence = 0.9895,
                AiInvolved = false,
                Reason = "Reinforced confidence 0.99 ≥ threshold 0.85 only via rung(s) outside the auto-file-eligible set ⇒ Suggested.",
            },
            RungsFired = ["AiClassification", "ExplicitReference", "ParticipantCorrelation", "RecordNameMatch"],
            Candidates = candidates,
            Signals = signals,
        },
    };

    private static async Task<JsonElement> WrittenProvenanceAsync(AssociationDecision decision)
    {
        var fields = await Resolver().BuildNewRecordFieldsAsync(decision, CancellationToken.None);
        var json = (string)fields["sprk_associationprovenance"];
        json.Length.Should().BeLessThanOrEqualTo(ColumnLength, "the column holds 10,000 characters");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task ATrailPastTheColumn_KeepsTheWrittenCandidate_AndAsManyOthersAsFit_HighestConfidenceFirst()
    {
        // 40 long name matches (far past 10,000 characters); the WRITTEN matter is the LOWEST-confidence one and sits last.
        var candidates = Enumerable.Range(1, 39).Select(i => NameMatch(i, 0.70 + i * 0.005)).ToList();
        candidates.Add(NameMatch(40, 0.60, written: true, id: WrittenMatter));
        var signals = Enumerable.Range(1, 3).Select(Signal).ToList();
        JsonSerializer.Serialize(Decision(candidates, signals).Provenance).Length.Should().BeGreaterThan(ColumnLength,
            "precondition: the full trail does not fit");

        var provenance = await WrittenProvenanceAsync(Decision(candidates, signals));

        var kept = provenance.GetProperty("candidates").EnumerateArray().ToList();
        kept.Should().NotBeEmpty("the review surface reads the candidates; an empty list reads as 'No confident match'");
        kept.Should().ContainSingle(c => c.GetProperty("written").GetBoolean(), "the written record always survives")
            .Which.GetProperty("targetId").GetString().Should().Be(WrittenMatter.ToString("D"));

        // The others kept are the highest-confidence ones, and every kept candidate stays in its original order.
        var keptIds = kept.Select(c => c.GetProperty("targetId").GetString()).ToList();
        var others = keptIds.Where(id => id != WrittenMatter.ToString("D")).ToList();
        others.Should().NotBeEmpty("more than the written candidate fits");
        var expectedOthers = candidates.Where(c => !c.Written).OrderByDescending(c => c.ReinforcedConfidence)
            .Take(others.Count).Select(c => c.TargetId).ToHashSet();
        others.Should().BeEquivalentTo(expectedOthers);
        keptIds.Should().Equal(candidates.Select(c => c.TargetId).Where(keptIds.Contains));

        provenance.GetProperty("decision").GetProperty("status").GetString().Should().Be("Suggested");
        provenance.GetProperty("rungsFired").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task ATrailPastTheColumnOnlyBecauseOfItsSignals_KeepsEveryCandidate_AndTheSignalsThatFit()
    {
        var candidates = new List<CandidateTrace> { NameMatch(1, 0.95, written: true, id: WrittenMatter), NameMatch(2, 0.8) };
        var signals = Enumerable.Range(1, 60).Select(Signal).ToList();

        var provenance = await WrittenProvenanceAsync(Decision(candidates, signals));

        provenance.GetProperty("candidates").GetArrayLength().Should().Be(2);
        var keptSignals = provenance.GetProperty("signals").GetArrayLength();
        keptSignals.Should().BeGreaterThan(0).And.BeLessThan(60);
    }

    [Fact]
    public async Task ATrailThatFits_IsWrittenWhole()
    {
        var candidates = new List<CandidateTrace> { NameMatch(1, 0.95, written: true, id: WrittenMatter), NameMatch(2, 0.8) };
        var signals = new List<SignalTrace> { Signal(1) };
        var decision = Decision(candidates, signals);

        var fields = await Resolver().BuildNewRecordFieldsAsync(decision, CancellationToken.None);

        var json = (string)fields["sprk_associationprovenance"];
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("candidates").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("signals").GetArrayLength().Should().Be(1);
    }
}
