using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// FR-C4 (task 025): proves the cross-path reconciliation contract of <see cref="CrossPathLink"/> — a captured
/// <c>sprk_communication</c> and a user-saved <c>sprk_document</c> archive of the SAME email are LINKED (not
/// duplicated) via the document's existing <c>sprk_relatedcommunication</c> lookup. The link is idempotent (single-valued lookup;
/// re-processing does not re-write) and non-fatal (NFR-04 — a failure degrades, never throws out of capture/upload).
/// The generic-seam boundary (<see cref="IGenericEntityService"/>) is mocked.
/// </summary>
public class CrossPathLinkTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    // LinkDocumentToCommunicationAsync's four tests were removed with the method (task 130, ADR-038 A3: subject deleted).
    // Its replacement, the link written in the document create, is covered by
    // tests/integration/data-mutation/OfficeVersionSave/OfficeEmailDocumentCommunicationLinkTests.cs.

    // ── FindAndLinkArchiveDocumentsAsync (capture side: upload-then-capture) ───────────────────

    [Fact]
    public async Task FindAndLinkArchiveDocumentsAsync_WhenArchiveWasUploadedFirst_LinksItToTheCommunication()
    {
        const string messageId = "<msg-fr-c4-001@partner.com>";
        var communicationId = Guid.NewGuid();
        var archiveDocId = Guid.NewGuid();

        var generic = new Mock<IGenericEntityService>();
        QueryExpression? captured = null;
        generic.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_document", archiveDocId) }));
        Guid? updatedDoc = null;
        Dictionary<string, object>? written = null;
        generic.Setup(g => g.UpdateAsync("sprk_document", archiveDocId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, f, _) => { updatedDoc = id; written = f; })
            .Returns(Task.CompletedTask);

        var linked = await CrossPathLink.FindAndLinkArchiveDocumentsAsync(
            generic.Object, messageId, communicationId, Logger, CancellationToken.None);

        linked.Should().Be(1, "the pre-existing .eml archive document links to the newly-captured communication");
        updatedDoc.Should().Be(archiveDocId);
        written!.Should().ContainKey(CrossPathLink.LinkedCommunicationAttribute);
        ((EntityReference)written![CrossPathLink.LinkedCommunicationAttribute]).Id.Should().Be(communicationId);
        // The query keys on the shared internet-message-id AND the email-archive flag.
        captured!.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == CrossPathLink.EmailMessageIdAttribute && c.Values.Contains(messageId));
        captured.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == CrossPathLink.IsEmailArchiveAttribute);
    }

    [Fact]
    public async Task FindAndLinkArchiveDocumentsAsync_WhenNoArchiveExists_ReturnsZeroAndWritesNothing()
    {
        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var linked = await CrossPathLink.FindAndLinkArchiveDocumentsAsync(
            generic.Object, "<msg-none@x.com>", Guid.NewGuid(), Logger, CancellationToken.None);

        linked.Should().Be(0, "capture-then-upload order — no archive exists yet; the office side links later");
        generic.Verify(g => g.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FindAndLinkArchiveDocumentsAsync_WhenArchiveAlreadyLinked_SkipsTheWrite()
    {
        var communicationId = Guid.NewGuid();
        var archiveDocId = Guid.NewGuid();
        var alreadyLinked = new Entity("sprk_document", archiveDocId);
        alreadyLinked[CrossPathLink.LinkedCommunicationAttribute] = new EntityReference("sprk_communication", communicationId);

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { alreadyLinked }));

        var linked = await CrossPathLink.FindAndLinkArchiveDocumentsAsync(
            generic.Object, "<msg-dup@x.com>", communicationId, Logger, CancellationToken.None);

        linked.Should().Be(0, "an archive already linked to this communication is idempotently skipped");
        generic.Verify(g => g.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FindAndLinkArchiveDocumentsAsync_WhenQueryThrows_IsNonFatalAndReturnsZero()
    {
        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        var linked = await CrossPathLink.FindAndLinkArchiveDocumentsAsync(
            generic.Object, "<msg-boom@x.com>", Guid.NewGuid(), Logger, CancellationToken.None);

        linked.Should().Be(0, "a lookup failure degrades, never throws out of capture (NFR-04)");
    }

    [Fact]
    public async Task FindAndLinkArchiveDocumentsAsync_WhenBlankMessageId_IsNoOpWithoutQuerying()
    {
        var generic = new Mock<IGenericEntityService>(MockBehavior.Strict); // strict → any query fails the test

        var linked = await CrossPathLink.FindAndLinkArchiveDocumentsAsync(
            generic.Object, "  ", Guid.NewGuid(), Logger, CancellationToken.None);

        linked.Should().Be(0);
    }
}
