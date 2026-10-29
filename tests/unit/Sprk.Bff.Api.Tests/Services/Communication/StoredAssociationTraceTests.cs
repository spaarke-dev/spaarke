using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// A stored association trace (<c>sprk_associationprovenance</c>) holds ids and reason codes only: no text taken from a
/// candidate record (name, number, description, match-reason prose), for written targets and review candidates alike.
/// Driven through the resolver's two write paths — the inbound job (<see cref="IncomingAssociationResolver.ResolveAsync"/>)
/// and the new-record fields the Office upload capture uses (<see cref="IncomingAssociationResolver.BuildNewRecordFieldsAsync"/>)
/// — with rung matches in the exact provenance formats the RecordNameMatch, SemanticMatch and ExplicitReference rungs emit.
/// </summary>
public class StoredAssociationTraceTests
{
    private const string WrittenMatterName = "Written Matter Record Title";
    private const string CandidateMatterName = "Candidate Matter Record Title";
    private const string CandidateMatterNumber = "CAND-NUMBER-0197";
    private const string ReferenceToken = "REF-TOKEN-0197";
    private const string ProjectName = "Candidate Project Record Title";
    private const string ProjectDescription = "Candidate project description highlight";

    /// <summary>Every piece of record-derived text the matches below carry; none may reach the stored column.</summary>
    private static readonly string[] RecordText =
    [
        WrittenMatterName, CandidateMatterName, CandidateMatterNumber, ReferenceToken, ProjectName, ProjectDescription,
        "name in subject", "reference number in body", "name=\"", "number=\"", "reason=\"",
    ];

    private static readonly Guid CommunicationId = Guid.NewGuid();
    private readonly Guid _writtenMatter = Guid.NewGuid();
    private readonly Guid _candidateMatter = Guid.NewGuid();
    private readonly Guid _candidateProject = Guid.NewGuid();
    private readonly Mock<IDataverseService> _dataverse = new();

    [Fact]
    public async Task ResolveAsync_TheStoredTrace_CarriesNoCandidateRecordText()
    {
        var stored = await StoredByInboundJobAsync();

        foreach (var text in RecordText)
        {
            stored.Should().NotContain(text, "a stored association trace holds ids and reason codes only");
        }
    }

    [Fact]
    public async Task BuildNewRecordFieldsAsync_TheStoredTrace_CarriesNoCandidateRecordText()
    {
        var resolver = Resolver();
        var decision = await resolver.EvaluateAsync(Envelope(), new AssociationContext(), CancellationToken.None);

        var fields = await resolver.BuildNewRecordFieldsAsync(decision, CancellationToken.None);

        var stored = (string)fields["sprk_associationprovenance"];
        foreach (var text in RecordText)
        {
            stored.Should().NotContain(text, "the Office upload capture writes the same stored form");
        }
    }

    [Fact]
    public async Task ResolveAsync_TheStoredTrace_KeepsIdsRungsReasonCodesAndScores()
    {
        using var doc = JsonDocument.Parse(await StoredByInboundJobAsync());
        var root = doc.RootElement;
        root.GetProperty("version").GetInt32().Should().Be(AssociationProvenance.StoredFormVersion);

        var candidates = root.GetProperty("candidates").EnumerateArray().ToList();

        var written = Candidate(candidates, _writtenMatter);
        written.GetProperty("written").GetBoolean().Should().BeTrue();
        written.GetProperty("targetEntity").GetString().Should().Be("sprk_matter");
        Contributors(written).Should().BeEquivalentTo(new[]
        {
            ("ExplicitReference", 1.0, "explicit-reference"),
            ("RecordNameMatch", 0.9, "record-name-match:where=subject:matched=name"),
        });

        var reviewMatter = Candidate(candidates, _candidateMatter);
        reviewMatter.GetProperty("written").GetBoolean().Should().BeFalse();
        Contributors(reviewMatter).Should().BeEquivalentTo(new[]
        {
            ("RecordNameMatch", 0.7, "record-name-match:where=body:matched=number"),
        });

        var reviewProject = Candidate(candidates, _candidateProject);
        reviewProject.GetProperty("written").GetBoolean().Should().BeFalse();
        reviewProject.GetProperty("reinforcedConfidence").GetDouble().Should().Be(0.6);
        Contributors(reviewProject).Should().BeEquivalentTo(new[] { ("SemanticMatch", 0.6, "semantic-match") });
    }

    [Theory]
    [InlineData("RecordNameMatch", "record-name-match:sprk_matter:where=attachment:matched=number:name=\"x\":number=\"y\":reason=\"z\"", "record-name-match:where=attachment:matched=number")]
    [InlineData("RecordNameMatch", "record-name-match:sprk_matter:where=elsewhere:matched=title:name=\"x\"", "record-name-match")]
    [InlineData("SemanticMatch", "semantic:sprk_project:score=0.62:where=subject in the title", "semantic-match")]
    [InlineData("ParticipantCorrelation", "participant:to:someone@contoso.example→member:sprk_matter", "participant-correlation")]
    [InlineData("Not A Rung", "anything", "unknown")]
    public void StoredReasonCode_IsBuiltFromClosedSetsOnly(string rung, string provenance, string expected)
        => AssociationProvenance.StoredReasonCode(rung, provenance).Should().Be(expected);

    [Fact]
    public async Task CallerScopedSuggestions_CarryReasonCodesOnly_NeverRecordTextOrAnotherRecordsId()
    {
        // The attachment-document rung names a matched DOCUMENT (id + name) on a contributor whose candidate is the
        // document's linked record; the caller's reads authorize the candidate, not the document.
        var documentId = Guid.NewGuid();
        const string DocumentName = "Matched Document Record Title.pdf";
        var matches = Matches().Append(new RungMatch
        {
            RegardingFieldName = "sprk_regardingmatter",
            Target = new EntityReference("sprk_matter", _candidateMatter),
            Confidence = 0.65,
            Provenance = $"attachment-document:document={documentId:D}:name=\"{DocumentName}\":link=sprk_matter:target=sprk_matter",
            Rung = RungKind.DocumentAssociation,
        }).ToList();
        var userClient = new Mock<IDataverseUserClient>();
        userClient
            .Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonDocument.Parse("{}").RootElement));

        var scoped = await SuggestionCandidateAccess.EvaluateForCallerAsync(
            CommunicationId, Envelope(), new AssociationContext(), Resolver(matches), userClient.Object,
            NullLogger.Instance, CancellationToken.None);

        var contributors = scoped.Suggestions.Candidates.SelectMany(c => c.Contributors).ToList();
        contributors.Should().NotBeEmpty();
        contributors.Select(k => k.Provenance).Should().OnlyContain(
            p => Regex.IsMatch(p, "^[a-z0-9-]+(:where=(subject|body|attachment))?(:matched=(name|number))?$"),
            "a suggestion response carries the stored form's reason codes only");
        var json = JsonSerializer.Serialize(scoped.Suggestions);
        foreach (var text in RecordText.Concat([DocumentName, documentId.ToString("D"), documentId.ToString("N")]))
        {
            json.Should().NotContain(text);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<string> StoredByInboundJobAsync()
    {
        var written = new List<Dictionary<string, object>>();
        _dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", CommunicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written.Add(new(f)))
            .Returns(Task.CompletedTask);

        await Resolver().ResolveAsync(CommunicationId, Envelope(), new AssociationContext(), CancellationToken.None);

        written.Should().ContainSingle();
        return (string)written[0]["sprk_associationprovenance"];
    }

    private IncomingAssociationResolver Resolver(IReadOnlyList<RungMatch>? matches = null) => new(
        new IAssociationRung[] { new FixedMatchesRung(matches ?? Matches()) },
        _dataverse.Object,
        _dataverse.Object,
        AssociationTestSupport.Mapper(),
        Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
        new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
        Mock.Of<ILogger<IncomingAssociationResolver>>());

    /// <summary>
    /// One matter the engine files (an explicit reference plus a name match on it), a second matter on the same field
    /// below the auto-file bar (a number match — a review candidate), and a project from the semantic rung (a review
    /// candidate). Provenance strings are in the rungs' own formats.
    /// </summary>
    private IReadOnlyList<RungMatch> Matches() =>
    [
        new RungMatch
        {
            RegardingFieldName = "sprk_regardingmatter",
            Target = new EntityReference("sprk_matter", _writtenMatter),
            Confidence = 1.0,
            Provenance = $"explicit:subject-token→matter:{ReferenceToken}",
            Rung = RungKind.ExplicitReference,
        },
        new RungMatch
        {
            RegardingFieldName = "sprk_regardingmatter",
            Target = new EntityReference("sprk_matter", _writtenMatter),
            Confidence = 0.9,
            Provenance = $"record-name-match:sprk_matter:where=subject:matched=name:name=\"{WrittenMatterName}\":number=\"\":reason=\"name in subject\"",
            Rung = RungKind.RecordNameMatch,
        },
        new RungMatch
        {
            RegardingFieldName = "sprk_regardingmatter",
            Target = new EntityReference("sprk_matter", _candidateMatter),
            Confidence = 0.7,
            Provenance = $"record-name-match:sprk_matter:where=body:matched=number:name=\"{CandidateMatterName}\":number=\"{CandidateMatterNumber}\":reason=\"reference number in body\"",
            Rung = RungKind.RecordNameMatch,
        },
        new RungMatch
        {
            RegardingFieldName = "sprk_regardingproject",
            Target = new EntityReference("sprk_project", _candidateProject),
            Confidence = 0.6,
            Provenance = $"semantic:sprk_project:score=0.60:name: {ProjectName}; description: {ProjectDescription}",
            Rung = RungKind.SemanticMatch,
        },
    ];

    private static NormalizedMessage Envelope() => new()
    {
        Direction = CommunicationDirection.Incoming,
        Subject = "Re: update",
        From = "sender@external.example",
    };

    private static JsonElement Candidate(List<JsonElement> candidates, Guid id)
        => candidates.Single(c => c.GetProperty("targetId").GetString() == id.ToString("D"));

    private static IEnumerable<(string Rung, double Confidence, string Provenance)> Contributors(JsonElement candidate)
        => candidate.GetProperty("contributors").EnumerateArray()
            .Select(k => (k.GetProperty("rung").GetString()!, k.GetProperty("confidence").GetDouble(), k.GetProperty("provenance").GetString()!))
            .ToList();

    /// <summary>A deterministic rung that returns the given matches (each match names its own rung kind).</summary>
    private sealed class FixedMatchesRung(IReadOnlyList<RungMatch> matches) : IAssociationRung
    {
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;

        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(NormalizedMessage message, AssociationContext context, CancellationToken ct)
            => Task.FromResult(matches);
    }
}
