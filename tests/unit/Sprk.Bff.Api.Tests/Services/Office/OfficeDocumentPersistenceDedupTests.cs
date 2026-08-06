using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Proves the FR-C3 content-dedup wiring on the email-attachment / Office save path
/// (<see cref="OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync"/>): a byte-identical upload
/// creates NO second canonical <c>sprk_document</c> (it returns the existing canonical id), and a first upload
/// creates the document AND stamps its content hash so later uploads dedup against it. The detector is mocked
/// at its <c>virtual</c> <c>ReconcileAsync</c> seam; the Dataverse writes go through the mocked
/// <see cref="IDocumentDataverseService"/> boundary.
/// </summary>
public class OfficeDocumentPersistenceDedupTests
{
    private static Mock<ContentDedupDetector> DetectorReturning(DedupDecision decision)
    {
        var mock = new Mock<ContentDedupDetector>(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);
        mock.Setup(d => d.ReconcileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decision);
        return mock;
    }

    private static SaveRequest AttachmentSave() => new() { ContentType = SaveContentType.Attachment };

    [Fact]
    public async Task CreateDocumentWithSpePointers_WhenDuplicate_CreatesNoSecondDocumentAndReturnsCanonical()
    {
        var canonical = Guid.NewGuid();
        var detector = DetectorReturning(new DedupDecision("hash1", IsDuplicate: true, CanonicalDocumentId: canonical));
        var docSvc = new Mock<IDocumentDataverseService>(MockBehavior.Strict); // strict → fails if ANY create/update happens
        var sut = new OfficeDocumentPersistence(docSvc.Object, Mock.Of<IProcessingJobService>(), detector.Object, NullLogger<OfficeDocumentPersistence>.Instance);

        var result = await sut.CreateDocumentWithSpePointersAsync(
            AttachmentSave(), "drive1", "item2", "https://spe/web", "invoice.pdf", 1024, "owner-oid", CancellationToken.None);

        result.DocumentId.Should().Be(canonical, "a byte-identical upload resolves to the existing canonical document");
        result.WasContentDuplicate.Should().BeTrue("the caller must skip finalization + clean up the transient blob (R-3)");
        docSvc.Verify(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "no second canonical sprk_document may be created for duplicate content (FR-C3)");
    }

    [Fact]
    public async Task CreateDocumentWithSpePointers_WhenNotDuplicate_CreatesDocumentAndStampsCanonicalHash()
    {
        var newDocId = Guid.NewGuid();
        var detector = DetectorReturning(new DedupDecision("hash-xyz", IsDuplicate: false, CanonicalDocumentId: null));
        var docSvc = new Mock<IDocumentDataverseService>();
        docSvc.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocId.ToString());
        UpdateDocumentRequest? update = null;
        docSvc.Setup(d => d.UpdateDocumentAsync(newDocId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, u, _) => update = u)
            .Returns(Task.CompletedTask);

        var sut = new OfficeDocumentPersistence(docSvc.Object, Mock.Of<IProcessingJobService>(), detector.Object, NullLogger<OfficeDocumentPersistence>.Instance);

        var result = await sut.CreateDocumentWithSpePointersAsync(
            AttachmentSave(), "drive1", "item2", "https://spe/web", "invoice.pdf", 1024, "owner-oid", CancellationToken.None);

        result.DocumentId.Should().Be(newDocId);
        result.WasContentDuplicate.Should().BeFalse("a first upload is not a duplicate — finalization proceeds normally");
        docSvc.Verify(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        update.Should().NotBeNull();
        update!.CanonicalHash.Should().Be("hash-xyz", "the first writer stamps the content hash so future uploads dedup against it");
    }

    [Fact]
    public async Task CreateDocumentWithSpePointers_EmailSave_MergesUploaderOntoCanonicalCommunication()
    {
        // FR-C2 (task 022) office half: an email save whose message was ALSO captured inbound (a canonical
        // sprk_communication exists for its internet-message-id) records THIS user as a saver on that canonical.
        const string messageId = "<msg-fr-c2-office@x.com>";
        var canonicalCommId = Guid.NewGuid();
        var detector = DetectorReturning(new DedupDecision("h", IsDuplicate: false, CanonicalDocumentId: null));

        var docSvc = new Mock<IDocumentDataverseService>();
        docSvc.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        docSvc.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var comm = new Mock<ICommunicationDataverseService>();
        var canonicalRow = new Entity("sprk_communication", canonicalCommId);
        comm.Setup(c => c.GetCommunicationByInternetMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canonicalRow);

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveAsync("sprk_communication", canonicalCommId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(canonicalRow);
        Dictionary<string, object>? written = null;
        generic.Setup(g => g.UpdateAsync("sprk_communication", canonicalCommId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);

        var sut = new OfficeDocumentPersistence(
            docSvc.Object, Mock.Of<IProcessingJobService>(), detector.Object,
            NullLogger<OfficeDocumentPersistence>.Instance, comm.Object, generic.Object);

        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata { Subject = "FR-C2 office test", SenderEmail = "sender@x.com", InternetMessageId = messageId },
        };

        await sut.CreateDocumentWithSpePointersAsync(
            request, "drive1", "item2", "https://spe/web", "email.eml", 100, "user-oid-42", CancellationToken.None);

        written.Should().NotBeNull("an email save whose message was captured inbound records the saver on the canonical communication (FR-C2)");
        written![DeliveryContextMerge.SavedByUsersAttribute].Should().Be("user-oid-42");
    }

    [Fact]
    public async Task CreateDocumentWithSpePointers_EmailSaveWithCapturedCommunication_LinksDocumentToCommunication()
    {
        // FR-C4 (task 025), capture-then-upload: an email save whose message was ALSO captured inbound (a canonical
        // sprk_communication exists) links the just-created archive DOCUMENT to that communication — so the review
        // surface shows ONE email, not the communication + this archive as two rows.
        const string messageId = "<msg-fr-c4-office@x.com>";
        var canonicalCommId = Guid.NewGuid();
        var newDocId = Guid.NewGuid();
        var detector = DetectorReturning(new DedupDecision("h", IsDuplicate: false, CanonicalDocumentId: null));

        var docSvc = new Mock<IDocumentDataverseService>();
        docSvc.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocId.ToString());
        docSvc.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.GetCommunicationByInternetMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", canonicalCommId));

        var generic = new Mock<IGenericEntityService>();
        // canonical-communication reads/writes (FR-C2) are unset → loose defaults; capture the FR-C4 document link.
        generic.Setup(g => g.RetrieveAsync("sprk_document", newDocId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", newDocId));
        Dictionary<string, object>? docWrite = null;
        generic.Setup(g => g.UpdateAsync("sprk_document", newDocId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => docWrite = f)
            .Returns(Task.CompletedTask);

        var sut = new OfficeDocumentPersistence(
            docSvc.Object, Mock.Of<IProcessingJobService>(), detector.Object,
            NullLogger<OfficeDocumentPersistence>.Instance, comm.Object, generic.Object);

        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata { Subject = "FR-C4 office test", SenderEmail = "sender@x.com", InternetMessageId = messageId },
        };

        await sut.CreateDocumentWithSpePointersAsync(
            request, "drive1", "item2", "https://spe/web", "email.eml", 100, "user-oid", CancellationToken.None);

        docWrite.Should().NotBeNull("the new archive document is cross-path-linked to its captured communication (FR-C4)");
        ((Microsoft.Xrm.Sdk.EntityReference)docWrite![CrossPathLink.LinkedCommunicationAttribute]).Id.Should().Be(canonicalCommId);
    }

    [Fact]
    public async Task CreateDocumentWithSpePointers_EmailSaveNeverCaptured_DoesNotAttemptCrossPathLink()
    {
        // FR-C4 (task 025), upload-then-capture: the email was NOT captured inbound (no canonical communication) →
        // the office side links nothing; the capture side links later when the communication is created.
        const string messageId = "<msg-fr-c4-uncaptured@x.com>";
        var newDocId = Guid.NewGuid();
        var detector = DetectorReturning(new DedupDecision("h", IsDuplicate: false, CanonicalDocumentId: null));

        var docSvc = new Mock<IDocumentDataverseService>();
        docSvc.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocId.ToString());
        docSvc.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.GetCommunicationByInternetMessageIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null); // never captured

        var generic = new Mock<IGenericEntityService>(MockBehavior.Strict); // strict → any sprk_document link call fails the test

        var sut = new OfficeDocumentPersistence(
            docSvc.Object, Mock.Of<IProcessingJobService>(), detector.Object,
            NullLogger<OfficeDocumentPersistence>.Instance, comm.Object, generic.Object);

        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata { Subject = "FR-C4 uncaptured", SenderEmail = "sender@x.com", InternetMessageId = messageId },
        };

        var act = async () => await sut.CreateDocumentWithSpePointersAsync(
            request, "drive1", "item2", "https://spe/web", "email.eml", 100, "user-oid", CancellationToken.None);

        await act.Should().NotThrowAsync("no canonical communication → no cross-path link attempted (and the save still succeeds)");
    }
}
