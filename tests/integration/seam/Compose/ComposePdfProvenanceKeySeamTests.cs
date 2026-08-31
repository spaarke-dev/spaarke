// Task 070 cluster 4 — the PDF provenance KEY, exercised against a real distributed cache.
//
// WHY THIS FILE EXISTS. The cluster-4 mutation pass shortened the derived-document cache key from
// {driveId}:{speId} to {speId} alone, and all 1,798 Compose tests stayed green. Every existing PDF test
// uses a single drive, so nothing had ever asked whether the drive half of the key does anything.
//
// It does. Drive-item ids are unique per DRIVE, not globally — two containers can hold items with the
// same id. With the drive dropped, opening PDF X in container A would resolve to the Word document that
// PDF X in container B became, and the user would be handed someone else's document to edit. That is a
// cross-container data-exposure shape, not a cache-efficiency detail, which is why it is worth a test
// that does not depend on the rest of the load/save pipeline to detect it.
//
// SHAPE. The coordinator is exercised directly against a REAL MemoryDistributedCache — no mock of the
// class under test, and the one collaborator it consults on this path (the SPE reachability probe) is
// mocked at its module boundary, per ADR-038. The renderer/projector are real instances the path never
// reaches.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposePdfProvenanceKeySeamTests
{
    private const string SpeId = "01ABCDEF-SAME-ITEM-ID";
    private const string DriveA = "b!drive-container-alpha";
    private const string DriveB = "b!drive-container-bravo";

    [Fact]
    public async Task ResolvePdfDerivedDocument_SameItemIdOnADifferentDrive_DoesNotResolveTheOtherDrivesDocument()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        // The probe would say "reachable" for anything — so if a mapping is returned at all, it is
        // because the KEY matched, which is precisely what this test is measuring.
        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: "derived", Name: "derived.docx", ParentId: null, Size: 1024,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1\"", IsFolder: false, WebUrl: null, DriveId: DriveA));

        var sut = new ComposePdfIntakeCoordinator(
            pdfIntakeSource: null,
            pdfModelProjector: new ComposePdfModelProjector(),
            documentRenderer: new ComposeDocumentRenderer(),
            spe: spe.Object,
            cache: cache,
            logger: NullLogger.Instance);

        var httpContext = new DefaultHttpContext();

        // Drive A's PDF became a Word document.
        await sut.SetPdfDerivedDocumentAsync(
            source: new ComposePdfIntakeCoordinator.ComposePdfSourceMarker(DriveA, SpeId),
            derivedDriveId: DriveA,
            derivedSpeId: "01DERIVED-IN-ALPHA",
            derivedRecordId: Guid.NewGuid(),
            derivedAtUtc: DateTimeOffset.UtcNow,
            ct: CancellationToken.None);

        // Sanity: drive A resolves its own mapping. Without this the negative below could pass because
        // nothing was ever stored.
        var fromDriveA = await sut.ResolvePdfDerivedDocumentAsync(DriveA, SpeId, httpContext, CancellationToken.None);
        fromDriveA.Should().NotBeNull("drive A stored this mapping and must get it back");
        fromDriveA!.SpeId.Should().Be("01DERIVED-IN-ALPHA");

        // The measurement: a DIFFERENT drive, same item id, must not see it.
        var fromDriveB = await sut.ResolvePdfDerivedDocumentAsync(DriveB, SpeId, httpContext, CancellationToken.None);
        fromDriveB.Should().BeNull(
            "drive-item ids are unique per DRIVE, not globally. If the derived-document key ignored the " +
            "drive, opening this PDF in container B would hand the user the Word document that a " +
            "DIFFERENT PDF in container A became — someone else's document, silently, with no error.");
    }
}
