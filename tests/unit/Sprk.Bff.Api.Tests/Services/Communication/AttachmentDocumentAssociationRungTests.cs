using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 3.7 (attachment→document association, 061 UAT / F1) tests. The rung matches an incoming attachment to
/// an existing <c>sprk_document</c> (via the mocked <see cref="IGenericEntityService"/> boundary) and surfaces
/// that document's OWN matter/project/invoice links as SUGGESTED association candidates — never auto-file
/// (RungKind.DocumentAssociation is not in the mapper's auto-file-eligible set). Closes the UAT gap where an
/// attached file already filed to a matter contributed nothing to the email's association.
/// </summary>
public class AttachmentDocumentAssociationRungTests
{
    private readonly Mock<IGenericEntityService> _svc = new();

    private AttachmentDocumentAssociationRung Rung() =>
        new(_svc.Object, NullLogger<AttachmentDocumentAssociationRung>.Instance);

    private void SetupDocuments(params DataverseEntity[] docs) =>
        _svc.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(docs.ToList()));

    private static DataverseEntity Document(Guid id, string fileName, params (string field, string entity, Guid id)[] links)
    {
        var e = new DataverseEntity("sprk_document", id) { ["sprk_filename"] = fileName };
        foreach (var (field, entity, linkId) in links)
            e[field] = new EntityReference(entity, linkId);
        return e;
    }

    private static NormalizedMessage MessageWithAttachment(string fileName) => new()
    {
        Direction = CommunicationDirection.Incoming,
        Subject = "FYI - documents enclosed",
        Attachments = new[] { new NormalizedAttachment { Name = fileName } },
    };

    // ── 1. attachment filename matches a document filed to a matter → Suggested-band candidate ──

    [Fact]
    public async Task Evaluate_AttachmentMatchesDocumentFiledToMatter_SurfacesMatterAsSuggestedNeverAutoFile()
    {
        var matterId = Guid.NewGuid();
        SetupDocuments(Document(Guid.NewGuid(), "PAT 109270W-1 - Letter to Office.pdf",
            ("sprk_matter", "sprk_matter", matterId)));

        var matches = await Rung().EvaluateAsync(
            MessageWithAttachment("PAT 109270W-1 - Letter to Office.pdf"), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Target!.Id.Should().Be(matterId);
        match.Rung.Should().Be(RungKind.DocumentAssociation);
        match.Confidence.Should().Be(0.65);
        match.Confidence.Should().BeLessThan(0.85, "a document's matter is INDIRECT evidence — surface for review, never auto-file");
        match.Provenance.Should().Contain("attachment-document");
    }

    [Fact]
    public async Task Evaluate_DocumentMatch_MapperSuggestsButDoesNotAutoFileNorWrite()
    {
        // End-to-end through the real mapper: a lone document-association match must (a) NOT auto-file (its kind
        // is excluded from the auto-file-eligible set) and (b) NOT be WRITTEN as a filed association — it is a
        // surface-only SUGGESTION the reviewer confirms (061 UAT round-2). It is still surfaced as a candidate.
        var matterId = Guid.NewGuid();
        SetupDocuments(Document(Guid.NewGuid(), "Engagement Letter Smith v Smith.pdf",
            ("sprk_matter", "sprk_matter", matterId)));

        var matches = await Rung().EvaluateAsync(
            MessageWithAttachment("Engagement Letter Smith v Smith.pdf"), new AssociationContext(), CancellationToken.None);

        var decision = AssociationTestSupport.Mapper().Decide(matches, CommunicationDirection.Incoming, null);
        decision.AutoFiled.Should().BeFalse("a document-association match can never auto-file");
        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.RegardingWrites.Should().BeEmpty("F1 matches are surface-only candidates — never written as a filed association");
        decision.Provenance.Candidates.Should().Contain(c => c.TargetId == matterId.ToString("D"),
            "the document's matter is still surfaced as a review candidate");
    }

    // ── 2. related-matter link maps to the regarding-matter field ──

    [Fact]
    public async Task Evaluate_DocumentRelatedMatterLink_MapsToRegardingMatter()
    {
        var matterId = Guid.NewGuid();
        SetupDocuments(Document(Guid.NewGuid(), "Invoice-10044725 backup.pdf",
            ("sprk_relatedmatter", "sprk_matter", matterId)));

        var matches = await Rung().EvaluateAsync(
            MessageWithAttachment("Invoice-10044725 backup.pdf"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.RegardingFieldName.Should().Be("sprk_regardingmatter");
        matches[0].Target!.Id.Should().Be(matterId);
    }

    // ── 2b. type-agnostic: F1 follows EVERY record link the document carries (not hard-coded to a type) ──

    [Fact]
    public async Task Evaluate_TypeAgnostic_SurfacesEveryRecordLinkTheDocumentCarries()
    {
        // 061 UAT round-2 (owner: don't hard-code F1 to a single record type). F1 follows all links; relevance
        // is decided by the smart layer (suggest-band confidence + surface-only-not-written in the mapper +
        // reviewer), NOT by hard-coding types out. An attached invoice's invoice IS surfaced — as a dismissible
        // SUGGESTION (the mapper leaves it unwritten; see AssociationStatusMapper surface-only tests).
        var matterId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        SetupDocuments(Document(Guid.NewGuid(), "Invoice-10044725.pdf",
            ("sprk_matter", "sprk_matter", matterId),
            ("sprk_invoice", "sprk_invoice", invoiceId)));

        var matches = await Rung().EvaluateAsync(
            MessageWithAttachment("Invoice-10044725.pdf"), new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardingmatter" && m.Target!.Id == matterId);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardinginvoice" && m.Target!.Id == invoiceId);
        matches.Should().OnlyContain(m => m.Rung == RungKind.DocumentAssociation && m.Confidence == 0.65);
    }

    // ── 3. cost gate: no attachments ⇒ no Dataverse query ──

    [Fact]
    public async Task Evaluate_NoAttachments_DoesNotQuery()
    {
        var message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Hello", BodyText = "no attachments here" };

        var matches = await Rung().EvaluateAsync(message, new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _svc.Verify(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never,
            "NFR-08: no attachment filenames or keywords ⇒ zero document queries");
    }

    [Fact]
    public async Task Evaluate_ShortGenericFileName_IsSkipped()
    {
        // "a.pdf" is below MinFileNameLength (8) — too weak a key; with no keywords either, no query runs.
        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            Subject = "hi",
            Attachments = new[] { new NormalizedAttachment { Name = "a.pdf" } },
        };

        var matches = await Rung().EvaluateAsync(message, new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _svc.Verify(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 4. dedup: two documents pointing at the SAME matter → one candidate ──

    [Fact]
    public async Task Evaluate_TwoDocumentsSameMatter_EmitsSingleDedupedCandidate()
    {
        var matterId = Guid.NewGuid();
        SetupDocuments(
            Document(Guid.NewGuid(), "AQ_SEC FORM D.pdf", ("sprk_matter", "sprk_matter", matterId)),
            Document(Guid.NewGuid(), "PAT 109270W-1 - Letter.pdf", ("sprk_relatedmatter", "sprk_matter", matterId)));

        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            Subject = "docs",
            Attachments = new[]
            {
                new NormalizedAttachment { Name = "AQ_SEC FORM D.pdf" },
                new NormalizedAttachment { Name = "PAT 109270W-1 - Letter.pdf" },
            },
        };

        var matches = await Rung().EvaluateAsync(message, new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle("the same matter reached via two documents dedups to one candidate")
            .Which.Target!.Id.Should().Be(matterId);
    }

    // ── 5. NFR-04: a document-query failure degrades to no-match ──

    [Fact]
    public async Task Evaluate_QueryThrows_DegradesToEmpty_NoPropagation()
    {
        _svc.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        var act = async () => await Rung().EvaluateAsync(
            MessageWithAttachment("Engagement Letter Smith v Smith.pdf"), new AssociationContext(), CancellationToken.None);

        var matches = await act.Should().NotThrowAsync();
        matches.Which.Should().BeEmpty();
    }
}
