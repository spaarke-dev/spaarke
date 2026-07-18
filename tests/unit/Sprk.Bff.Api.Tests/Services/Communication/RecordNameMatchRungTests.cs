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
/// Rung 3.5 (deterministic record-name/number match, email-r4 UAT 2026-07-17) unit tests. The rung retrieves
/// candidates from the records index (via a mocked <see cref="IRecordMatchingAi"/> boundary) and then makes the
/// decision DETERMINISTICALLY: a candidate matches ONLY if its name (token subsequence) or a reference number
/// (alphanumeric) appears verbatim in the email. These tests run the REAL verification logic over the mocked
/// index boundary (ADR-038 boundary mock).
/// </summary>
public class RecordNameMatchRungTests
{
    private readonly Mock<IRecordMatchingAi> _matcher = new();

    private RecordNameMatchRung Build(RecordNameMatchOptions? opts = null) =>
        new(RungTestSupport.ScopeFactoryFor(_matcher.Object),
            Options.Create(opts ?? new RecordNameMatchOptions()),
            NullLogger<RecordNameMatchRung>.Instance);

    private void SetupIndex(params RecordSearchResult[] hits) =>
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(hits));

    [Fact]
    public async Task Evaluate_WhenRecordNameAppearsInSubject_EmitsMatchAtNameConfidence()
    {
        var matterId = Guid.NewGuid();
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Matter, matterId, 0.7, "Smith v Smith"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Fw: New Matter : Engagement Letter Smith v Smith", bodyText: null),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Target!.Id.Should().Be(matterId);
        match.Target!.Name.Should().Be("Smith v Smith");
        match.Confidence.Should().Be(0.90);                     // NameConfidence
        match.Rung.Should().Be(RungKind.RecordNameMatch);
        match.Provenance.Should().Contain("record-name-match");
        match.Provenance.Should().Contain("Smith v Smith");
    }

    [Fact]
    public async Task Evaluate_WhenNameAppearsInBody_EmitsMatch()
    {
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Project, Guid.NewGuid(), 0.6, "Smith v Smith"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Please action", bodyText: "This is about the matter titled Smith v Smith, thanks."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.RegardingFieldName.Should().Be("sprk_regardingproject");
    }

    [Fact]
    public async Task Evaluate_WhenMatterAndProjectBothNamedInEmail_SurfacesBoth()
    {
        // Owner spec: surface ALL deterministically-matching types; the user picks the primary.
        var matterId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupIndex(
            RungTestSupport.Hit(RecordEntityType.Matter, matterId, 0.7, "Smith v Smith"),
            RungTestSupport.Hit(RecordEntityType.Project, projectId, 0.65, "Smith v Smith"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "New Matter Smith v Smith", bodyText: null),
            new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Select(m => m.RegardingFieldName).Should()
            .BeEquivalentTo(new[] { "sprk_regardingmatter", "sprk_regardingproject" });
        matches.Should().OnlyContain(m => m.Confidence == 0.90);
    }

    [Fact]
    public async Task Evaluate_WhenCandidateNameNotInEmail_DoesNotMatch()
    {
        // The retrieve-then-verify contract: the index may return a fuzzy candidate, but without an exact name
        // appearance the deterministic rung emits nothing (this is what stops "Test New Matter via Workspace"
        // from beating the exact-name match).
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.66, "Test New Matter via Workspace"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Fw: New Matter Smith v Smith", bodyText: "About Smith v Smith."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WhenNameTooShort_DoesNotMatch()
    {
        // "AB" collapses to 2 chars < MinNameLength (5) — too weak to assert a deterministic match.
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.7, "A B"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Re: A B follow up", bodyText: null),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WhenNameIsSingleCommonToken_DoesNotMatch()
    {
        // "Agreement" is 1 token < MinNameTokens (2) — a single common word is too weak for a deterministic
        // match; the semantic rung still covers it fuzzily.
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Matter, Guid.NewGuid(), 0.7, "Agreement"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Please sign the Agreement", bodyText: null),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WhenReferenceNumberAppears_EmitsMatchAtNumberConfidence()
    {
        var matterId = Guid.NewGuid();
        // Name won't appear in the email; the reference number will (separator-insensitive).
        SetupIndex(new RecordSearchResult
        {
            RecordId = matterId.ToString(),
            RecordType = RecordEntityType.Matter,
            RecordName = "Confidential Redacted",
            ReferenceNumbers = new[] { "REAL-2026-123456.02" },
            ConfidenceScore = 0.5,
        });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Invoice", bodyText: "Regarding matter REAL-2026-123456.02 please proceed."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.95);                     // NumberConfidence
        match.Provenance.Should().Contain("REAL-2026-123456.02");
    }

    [Fact]
    public async Task Evaluate_WhenNameAppearsOnlyInAttachmentText_EmitsMatch()
    {
        // Phase 2: an email whose matter name appears ONLY in the attachment (not subject/body) must still
        // match — the inbound processor sets NormalizedMessage.AttachmentText and the rung includes it.
        var matterId = Guid.NewGuid();
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Matter, matterId, 0.7, "Smith v Smith"));

        var envelope = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            Subject = "Please see attached",
            BodyText = "Details are in the letter.",
            AttachmentText = "ENGAGEMENT LETTER\nRe: Smith v Smith\nDear client, this confirms our engagement.",
        };

        var matches = await Build().EvaluateAsync(envelope, new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }

    [Fact]
    public async Task Evaluate_WhenDisabled_ReturnsEmpty_AndDoesNotCallIndex()
    {
        var matches = await Build(new RecordNameMatchOptions { Enabled = false }).EvaluateAsync(
            RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _matcher.Verify(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Evaluate_RequestsKeywordRanking_OverSearchableTypes()
    {
        RecordSearchRequest? captured = null;
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordSearchRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(RungTestSupport.SearchResponse());

        await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Smith v Smith", bodyText: null),
            new AssociationContext(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Options!.PreferKeywordRanking.Should().BeTrue();       // no semantic reranker
        captured.RecordTypes.Should().BeEquivalentTo(RecordEntityType.ValidTypes);
    }
}
