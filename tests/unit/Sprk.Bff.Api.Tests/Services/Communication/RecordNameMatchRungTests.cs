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
    public async Task Evaluate_WhenRecordNameAppearsInSubject_EmitsMatchAtSubjectConfidence()
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
        match.Confidence.Should().Be(0.95);                     // SubjectNameConfidence (strongest)
        match.Rung.Should().Be(RungKind.RecordNameMatch);
        match.Provenance.Should().Contain("where=subject");
        match.Provenance.Should().Contain("reason=\"name in subject\"");
        match.Provenance.Should().Contain("Smith v Smith");
    }

    [Fact]
    public async Task Evaluate_WhenNameAppearsInBody_EmitsMatchAtBodyConfidence()
    {
        SetupIndex(RungTestSupport.Hit(RecordEntityType.Project, Guid.NewGuid(), 0.6, "Smith v Smith"));

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "Please action", bodyText: "This is about the matter titled Smith v Smith, thanks."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingproject");
        match.Confidence.Should().Be(0.88);                     // BodyNameConfidence
        match.Provenance.Should().Contain("where=body");
    }

    [Fact]
    public async Task Evaluate_SubjectMatchOutranksAttachmentMatch()
    {
        // The core email-r4 UAT fix: a name in the SUBJECT must outrank a name that appears only in a (noisy)
        // attachment, so the exact match surfaces first and attachment noise cannot crowd it out.
        var subjectMatterId = Guid.NewGuid();
        var attachmentMatterId = Guid.NewGuid();
        SetupIndex(
            RungTestSupport.Hit(RecordEntityType.Matter, subjectMatterId, 0.7, "Smith v Smith"),
            RungTestSupport.Hit(RecordEntityType.Project, attachmentMatterId, 0.7, "Test New Matter via Workspace"));

        var envelope = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            Subject = "Fw: Smith v Smith",
            BodyText = "See attached.",
            AttachmentText = "This engagement concerns Test New Matter via Workspace and related items.",
        };

        var matches = await Build().EvaluateAsync(envelope, new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        var top = matches[0];
        top.Target!.Id.Should().Be(subjectMatterId);            // subject match ranks first
        top.Confidence.Should().Be(0.95);
        matches[1].Confidence.Should().Be(0.75);                // attachment match demoted
    }

    [Fact]
    public async Task Evaluate_WhenMatterAndProjectBothNamedInSubject_SurfacesBoth()
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
        matches.Should().OnlyContain(m => m.Confidence == 0.95);   // both in subject
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
        match.Confidence.Should().Be(0.97);                     // NumberConfidence (most discriminating)
        match.Provenance.Should().Contain("REAL-2026-123456.02");
        match.Provenance.Should().Contain("matched=number");
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

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.75);                     // AttachmentNameConfidence (demoted)
        match.Provenance.Should().Contain("where=attachment");
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

    // ── email-r4 UAT #7 regression: body/attachment reference numbers must resolve regardless of ranking ──
    // The defect: rung 3.5 was retrieve-then-verify over ONE noisy whole-text query, so a reference number
    // buried in the body never ranked into the top-N retrieval window and was never verified. The fix adds an
    // EXACT `referenceNumbers` server-side filter. These tests model that split: the whole-text call (no
    // Filters) MISSES the record; the exact-filter call (Filters.ReferenceNumbers set) FINDS it.

    private static bool IsExactRefFilter(RecordSearchRequest r) =>
        r.Filters?.ReferenceNumbers is { Count: > 0 };

    private void SetupSplitIndex(RecordSearchResult[] wholeText, RecordSearchResult[] exactFilter)
    {
        _matcher.Setup(m => m.SearchAsync(It.Is<RecordSearchRequest>(r => !IsExactRefFilter(r)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(wholeText));
        _matcher.Setup(m => m.SearchAsync(It.Is<RecordSearchRequest>(r => IsExactRefFilter(r)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RungTestSupport.SearchResponse(exactFilter));
    }

    private static RecordSearchResult RefHit(string recordType, Guid id, string name, string referenceNumber) =>
        new()
        {
            RecordId = id.ToString(),
            RecordType = recordType,
            RecordName = name,
            ReferenceNumbers = new[] { referenceNumber },
            ConfidenceScore = 0.5,
        };

    [Fact]
    public async Task Evaluate_WhenReferenceNumberOnlyInBody_AndWholeTextRetrievalMisses_ExactFilterResolvesIt()
    {
        // The core #7 scenario: the reference number is in the BODY only, and the whole-text keyword retrieval
        // does NOT surface the record (ranking miss). The exact referenceNumbers filter must recover it.
        var matterId = Guid.NewGuid();
        SetupSplitIndex(
            wholeText: Array.Empty<RecordSearchResult>(),                                  // ranking miss
            exactFilter: new[] { RefHit(RecordEntityType.Matter, matterId, "Smith v Smith", "REAL-2026-123456.02") });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(
                subject: "Fwd: paperwork",
                bodyText: "It also involves real estate matter Smith & Smith REAL-2026-123456.02, thanks."),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.97);                     // NumberConfidence (flat across locations)
        match.Rung.Should().Be(RungKind.RecordNameMatch);       // Suggest-only — never auto-file-eligible
        match.Provenance.Should().Contain("where=body");
        match.Provenance.Should().Contain("REAL-2026-123456.02");
    }

    [Fact]
    public async Task Evaluate_WhenSubjectRefAndDifferentBodyRef_EmitsBothMatterCandidates()
    {
        // The email-1 UAT scenario: matter #A in the subject, a DIFFERENT matter #B in the body. Both must
        // surface as sprk_regardingmatter candidates each ≥ 0.85 so the mapper flags Ambiguous (user picks).
        var patentId = Guid.NewGuid();
        var smithId = Guid.NewGuid();
        SetupSplitIndex(
            wholeText: new[] { RefHit(RecordEntityType.Matter, patentId, "Patent Application 19183531 - Elisa Liardo", "PAT-942665") },
            exactFilter: new[] { RefHit(RecordEntityType.Matter, smithId, "Smith v Smith", "REAL-2026-123456.02") });

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(
                subject: "PAT-942665 Patent Application 19183531 - Elisa Liardo",
                bodyText: "It also involves real estate matter Smith & Smith REAL-2026-123456.02"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.RegardingFieldName == "sprk_regardingmatter");
        matches.Should().OnlyContain(m => m.Confidence == 0.97);            // both reference numbers ≥ threshold
        matches.Select(m => m.Target!.Id).Should().BeEquivalentTo(new[] { patentId, smithId });
        matches.Should().Contain(m => m.Provenance.Contains("where=subject") && m.Provenance.Contains("PAT-942665"));
        matches.Should().Contain(m => m.Provenance.Contains("where=body") && m.Provenance.Contains("REAL-2026-123456.02"));
    }

    [Fact]
    public async Task Evaluate_WhenExtractedTokenMatchesNoRecord_EmitsNothing()
    {
        // Precision gate: a reference-shaped token that resolves to NO record (the exact filter returns empty)
        // emits nothing — the permissive extraction regex never produces a false positive on its own.
        SetupSplitIndex(
            wholeText: Array.Empty<RecordSearchResult>(),
            exactFilter: Array.Empty<RecordSearchResult>());               // "ABC-9999" matches no record

        var matches = await Build().EvaluateAsync(
            RungTestSupport.Envelope(
                subject: "Order update",
                bodyText: "Call me at 555-1234 about order ABC-9999 by 7/19/2026."),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WhenBodyHasReference_QueriesExactReferenceNumberFilter()
    {
        // Proves the exact-filter search is actually issued with the extracted number (the ranking-independent
        // recall path), not just the whole-text query.
        var captured = new List<RecordSearchRequest>();
        _matcher.Setup(m => m.SearchAsync(It.IsAny<RecordSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordSearchRequest, CancellationToken>((req, _) => captured.Add(req))
            .ReturnsAsync(RungTestSupport.SearchResponse());

        await Build().EvaluateAsync(
            RungTestSupport.Envelope(subject: "hi", bodyText: "re: REAL-2026-123456.02 please action"),
            new AssociationContext(), CancellationToken.None);

        captured.Should().Contain(r => IsExactRefFilter(r)
            && r.Filters!.ReferenceNumbers!.Any(n => n.Equals("REAL-2026-123456.02", StringComparison.OrdinalIgnoreCase)));
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
