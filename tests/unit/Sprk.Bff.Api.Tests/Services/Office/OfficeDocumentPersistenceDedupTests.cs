using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
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

        result.Should().Be(canonical, "a byte-identical upload resolves to the existing canonical document");
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

        result.Should().Be(newDocId);
        docSvc.Verify(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        update.Should().NotBeNull();
        update!.CanonicalHash.Should().Be("hash-xyz", "the first writer stamps the content hash so future uploads dedup against it");
    }
}
