using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Testing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Security tests for session cleanup (R2-066).
///
/// Verifies NFR-06: Session cleanup MUST delete ALL session-scoped
/// uploaded documents from temporary storage — not just mark as deleted,
/// but actually remove from storage.
///
/// Also verifies ADR-014: cache keys are tenant-scoped.
/// </summary>
public class SessionCleanupSecurityTests
{
    private readonly Mock<BlobServiceClient> _blobServiceMock;
    private readonly Mock<ILogger<TempBlobStorageService>> _loggerMock;

    public SessionCleanupSecurityTests()
    {
        _blobServiceMock = new Mock<BlobServiceClient>();
        _loggerMock = new Mock<ILogger<TempBlobStorageService>>();
    }

    // =========================================================================
    // DeleteSessionDocumentsAsync
    // =========================================================================

    [Fact]
    public async Task DeleteSessionDocumentsAsync_CallsDeleteOnAllSessionBlobs()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var containerMock = new Mock<BlobContainerClient>();

        _blobServiceMock
            .Setup(b => b.GetBlobContainerClient("test-documents"))
            .Returns(containerMock.Object);

        // Simulate 3 blobs in the session prefix
        var blobItems = new List<BlobItem>
        {
            BlobsModelFactory.BlobItem($"{sessionId}/20260317-120000_doc1.pdf"),
            BlobsModelFactory.BlobItem($"{sessionId}/20260317-120001_doc2.docx"),
            BlobsModelFactory.BlobItem($"{sessionId}/20260317-120002_doc3.txt"),
        };

        var pages = AsyncPageable<BlobItem>.FromPages(new[]
        {
            Azure.Page<BlobItem>.FromValues(blobItems, null, Mock.Of<Azure.Response>())
        });

        containerMock
            .Setup(c => c.GetBlobsAsync(
                BlobTraits.None,
                BlobStates.None,
                $"{sessionId}/",
                It.IsAny<CancellationToken>()))
            .Returns(pages);

        // Set up individual blob deletion mocks
        var deletedBlobs = new List<string>();
        containerMock
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns<string>(name =>
            {
                var blobMock = new Mock<BlobClient>();
                blobMock
                    .Setup(b => b.DeleteIfExistsAsync(
                        It.IsAny<Azure.Storage.Blobs.Models.DeleteSnapshotsOption>(),
                        It.IsAny<BlobRequestConditions>(),
                        It.IsAny<CancellationToken>()))
                    .Callback(() => deletedBlobs.Add(name))
                    .ReturnsAsync(Azure.Response.FromValue(true, Mock.Of<Azure.Response>()));
                return blobMock.Object;
            });

        var sut = new TempBlobStorageService(_blobServiceMock.Object, _loggerMock.Object);

        // Act
        await sut.DeleteSessionDocumentsAsync(sessionId, CancellationToken.None);

        // Assert — all 3 blobs must be physically deleted
        deletedBlobs.Should().HaveCount(3, "all session-scoped uploads must be deleted (NFR-06)");
        deletedBlobs.Should().AllSatisfy(name =>
            name.Should().StartWith($"{sessionId}/", "only session-scoped blobs should be deleted"));
    }

    [Fact]
    public async Task DeleteSessionDocumentsAsync_DeletesZeroBlobs_WhenSessionHasNoUploads()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var containerMock = new Mock<BlobContainerClient>();

        _blobServiceMock
            .Setup(b => b.GetBlobContainerClient("test-documents"))
            .Returns(containerMock.Object);

        var emptyPages = AsyncPageable<BlobItem>.FromPages(new[]
        {
            Azure.Page<BlobItem>.FromValues(new List<BlobItem>(), null, Mock.Of<Azure.Response>())
        });

        containerMock
            .Setup(c => c.GetBlobsAsync(
                BlobTraits.None,
                BlobStates.None,
                $"{sessionId}/",
                It.IsAny<CancellationToken>()))
            .Returns(emptyPages);

        var sut = new TempBlobStorageService(_blobServiceMock.Object, _loggerMock.Object);

        // Act — should not throw
        await sut.DeleteSessionDocumentsAsync(sessionId, CancellationToken.None);

        // Assert — no blob clients should be created for deletion
        containerMock.Verify(
            c => c.GetBlobClient(It.IsAny<string>()),
            Times.Never,
            "no blobs should be targeted for deletion when session has no uploads");
    }

    // =========================================================================
    // File size validation
    // =========================================================================

    [Fact]
    public async Task ValidateFileSizeAsync_ReturnsFalse_WhenExceeds50MB()
    {
        // Arrange
        var sut = new TempBlobStorageService(_blobServiceMock.Object, _loggerMock.Object);
        var oversizedStream = new MemoryStream(new byte[51 * 1024 * 1024]); // 51MB

        // Act
        var result = await sut.ValidateFileSizeAsync(oversizedStream, CancellationToken.None);

        // Assert
        result.Should().BeFalse("files exceeding 50MB must be rejected");
    }

    [Fact]
    public async Task ValidateFileSizeAsync_ReturnsTrue_WhenWithin50MB()
    {
        // Arrange
        var sut = new TempBlobStorageService(_blobServiceMock.Object, _loggerMock.Object);
        var validStream = new MemoryStream(new byte[1024]); // 1KB

        // Act
        var result = await sut.ValidateFileSizeAsync(validStream, CancellationToken.None);

        // Assert
        result.Should().BeTrue("files within 50MB limit should be accepted");
    }

    // =========================================================================
    // ADR-014: Cache key tenant scoping
    // =========================================================================

    [Fact]
    public void CacheKey_IncludesTenantId_ForMultiTenantIsolation()
    {
        // Arrange & Act
        var key1 = ChatSessionManager.BuildCacheKey("tenant-a", "session-1");
        var key2 = ChatSessionManager.BuildCacheKey("tenant-b", "session-1");

        // Assert
        key1.Should().Contain("tenant-a", "cache key must include tenant ID (ADR-014)");
        key2.Should().Contain("tenant-b", "cache key must include tenant ID (ADR-014)");
        key1.Should().NotBe(key2, "same session ID in different tenants must produce different keys");
    }

    [Fact]
    public void CacheKey_FollowsExpectedPattern()
    {
        // Arrange & Act
        var key = ChatSessionManager.BuildCacheKey("tenant-abc", "session-xyz");

        // Assert
        key.Should().Be("chat:session:tenant-abc:session-xyz",
            "cache key must follow pattern chat:session:{tenantId}:{sessionId} (ADR-014)");
    }
}
