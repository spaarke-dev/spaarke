using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// FR-C3 (R-3) transient-duplicate-blob cleanup: <see cref="OfficeStorageUploader.DeleteFromSpeAsync"/> deletes
/// the just-uploaded SPE drive item (via the <see cref="SpeFileStore"/> facade, ADR-007) when the office save
/// path suppressed a content duplicate, and is best-effort/non-fatal (a failed cleanup never fails the save).
/// The facade is mocked at its <c>virtual DeleteFileAsync</c> seam (the codebase idiom, cf. UploadSmallAsync).
/// </summary>
public class OfficeStorageUploaderDeleteTests
{
    private static Mock<SpeFileStore> BuildSpeMock()
    {
        var gcf = Mock.Of<IGraphClientFactory>();
        return new Mock<SpeFileStore>(MockBehavior.Loose,
            new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);
    }

    [Fact]
    public async Task DeleteFromSpeAsync_DeletesViaFacade_ReturnsTrue()
    {
        var spe = BuildSpeMock();
        spe.Setup(s => s.DeleteFileAsync("driveA", "itemB", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = new OfficeStorageUploader(spe.Object, NullLogger<OfficeStorageUploader>.Instance);

        var deleted = await sut.DeleteFromSpeAsync("driveA", "itemB", CancellationToken.None);

        deleted.Should().BeTrue();
        spe.Verify(s => s.DeleteFileAsync("driveA", "itemB", It.IsAny<CancellationToken>()), Times.Once,
            "ADR-007: the delete routes through the SpeFileStore facade");
    }

    [Fact]
    public async Task DeleteFromSpeAsync_WhenDeleteThrows_IsNonFatalReturnsFalse()
    {
        var spe = BuildSpeMock();
        spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("graph down"));
        var sut = new OfficeStorageUploader(spe.Object, NullLogger<OfficeStorageUploader>.Instance);

        var act = async () => await sut.DeleteFromSpeAsync("driveA", "itemB", CancellationToken.None);

        var deleted = await act.Should().NotThrowAsync("blob cleanup is best-effort — a failure must never fail the save");
        deleted.Subject.Should().BeFalse();
    }
}
