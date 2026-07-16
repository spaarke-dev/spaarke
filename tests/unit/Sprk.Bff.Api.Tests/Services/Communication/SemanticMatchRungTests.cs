using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 4 (semantic match, FR-14) unit tests. The rung resolves <see cref="IRecordMatchingAi"/> from a
/// per-eval <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/> scope; these tests
/// register a MOCK facade in a real service collection (boundary mock, ADR-038) and run the REAL rung logic
/// over it. The load-bearing invariant: rung 4 only ever proposes candidates in the Suggested band
/// (confidence ≤ ScoreCeiling &lt; the 0.85 auto-file threshold) — it can never auto-file.
/// </summary>
public class SemanticMatchRungTests
{
    private readonly Mock<IRecordMatchingAi> _matcher = new();

    private SemanticMatchRung Build(SemanticMatchOptions? opts = null) =>
        new(RungTestSupport.ScopeFactoryFor(_matcher.Object),
            Options.Create(opts ?? new SemanticMatchOptions()),
            NullLogger<SemanticMatchRung>.Instance);

    [Fact]
    public async Task Evaluate_WhenSearchReturnsHit_EmitsSuggestedBandMatchCappedAtScoreCeiling()
    {
        // A strong search hit (0.95) must be CAPPED at ScoreCeiling (0.80) so it lands firmly in the
        // Suggested band below the 0.85 auto-file threshold — rung 4 never produces an auto-file-eligible
        // confidence.
        var matterId = Guid.NewGuid();
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(
                RungTestSupport.Hit(RecordEntityType.Matter, matterId, 0.95, "Acme Matter")));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Target!.LogicalName.Should().Be("sprk_matter");
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.80);                          // capped at ScoreCeiling
        match.Confidence.Should().BeLessThan(0.85);                  // strictly below auto-file threshold
        match.Rung.Should().Be(RungKind.SemanticMatch);
    }

    [Theory]
    [InlineData(RecordEntityType.Matter, "sprk_regardingmatter")]
    [InlineData(RecordEntityType.Project, "sprk_regardingproject")]
    [InlineData(RecordEntityType.Invoice, "sprk_regardinginvoice")]
    public async Task Evaluate_MapsSearchableRecordTypeToItsRegardingField(string recordType, string expectedField)
    {
        var id = Guid.NewGuid();
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(
                RungTestSupport.Hit(recordType, id, 0.70)));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be(expectedField);
        match.Target!.LogicalName.Should().Be(recordType);
        match.Confidence.Should().Be(0.70);                          // below ceiling ⇒ preserved
    }

    [Fact]
    public async Task Evaluate_WritesMatchReasonsIntoProvenance()
    {
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(
                RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.72, "Acme",
                    "subject overlaps matter name", "sender is on the matter team")));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Provenance.Should().Contain("subject overlaps matter name");
        match.Provenance.Should().Contain("sender is on the matter team");
        match.Provenance.Should().Contain("semantic:");
    }

    [Fact]
    public async Task Evaluate_FiltersHitsBelowMinScore()
    {
        // MinScore default 0.50 — a 0.30 hit must be dropped, a 0.60 hit kept.
        var keptId = Guid.NewGuid();
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(
                RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.30, "Weak"),
                RungTestSupport.Hit(RecordEntityType.Project, keptId, 0.60, "Strong")));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(keptId);
    }

    [Fact]
    public async Task Evaluate_WhenDisabled_ReturnsEmpty_AndDoesNotCallSearch()
    {
        var matches = await Build(new SemanticMatchOptions { Enabled = false }).EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _matcher.Verify(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Evaluate_WhenQueryEmpty_ReturnsEmpty_AndDoesNotCallSearch()
    {
        var emptyEnvelope = RungTestSupport.Envelope(subject: "  ", bodyText: null);

        var matches = await Build().EvaluateAsync(emptyEnvelope, new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _matcher.Verify(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Evaluate_ComposesQueryFromEnvelopeSubjectAndBody_OverSearchableRecordTypesOnly()
    {
        RecordSearchRequest? captured = null;
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordSearchRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(RungTestSupport.SearchResponse());

        var envelope = RungTestSupport.Envelope(subject: "Contract for MAT-42", bodyText: "Please review the draft.");
        await Build().EvaluateAsync(envelope, new AssociationContext(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Query.Should().Contain("Contract for MAT-42");
        captured.Query.Should().Contain("Please review the draft.");
        // The records index only contains matter/project/invoice (§11 / FR-14) — org is not queried here.
        captured.RecordTypes.Should().BeEquivalentTo(RecordEntityType.ValidTypes);
        // SearchIndexName is left null so RecordSearchService resolves the records index (merge-clean w/ 075).
        captured.SearchIndexName.Should().BeNull();
    }
}
