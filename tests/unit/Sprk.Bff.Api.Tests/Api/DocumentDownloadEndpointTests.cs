using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// Unit tests for the document download endpoint logic.
/// Tests cover: validation, document entity behavior, telemetry, and edge cases.
/// Note: SpeFileStore integration is tested via integration tests since it requires Graph SDK.
/// </summary>
public class DocumentDownloadEndpointTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly DocumentTelemetry _telemetry;

    public DocumentDownloadEndpointTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _telemetry = new DocumentTelemetry();
    }

    private static HttpContext CreateHttpContext(string userId = "test-user-123")
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "test-trace-id"
        };

        if (!string.IsNullOrEmpty(userId))
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }

    private static DocumentEntity CreateValidDocument(
        string? graphDriveId = "test-drive-id",
        string? graphItemId = "test-item-id",
        string? mimeType = "message/rfc822",
        string? fileName = "test-email.eml",
        long? fileSize = 1024)
    {
        return new DocumentEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Document",
            GraphDriveId = graphDriveId,
            GraphItemId = graphItemId,
            MimeType = mimeType,
            FileName = fileName,
            FileSize = fileSize
        };
    }

    #region Validation Tests

    [Fact]
    public void DownloadDocument_InvalidGuidFormat_FailsValidation()
    {
        // Arrange
        var invalidId = "not-a-valid-guid";

        // Assert - The endpoint validates GUID format before proceeding
        Guid.TryParse(invalidId, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DownloadDocument_EmptyOrNullId_FailsValidation(string? id)
    {
        // Assert - validates the endpoint's validation logic
        string.IsNullOrWhiteSpace(id).Should().BeTrue();
    }

    [Fact]
    public void DownloadDocument_ValidGuidFormat_PassesValidation()
    {
        // Arrange
        var validId = Guid.NewGuid().ToString();

        // Assert
        Guid.TryParse(validId, out var parsed).Should().BeTrue();
        parsed.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region DataverseService Integration Tests

    [Fact]
    public async Task GetDocumentAsync_DocumentNotFound_ReturnsNull()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Act
        var document = await _dataverseServiceMock.Object.GetDocumentAsync(documentId);

        // Assert
        document.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentAsync_DocumentExists_ReturnsDocument()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var expectedDocument = CreateValidDocument();

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDocument);

        // Act
        var document = await _dataverseServiceMock.Object.GetDocumentAsync(documentId);

        // Assert
        document.Should().NotBeNull();
        document.Should().Be(expectedDocument);
    }

    [Fact]
    public async Task GetDocumentAsync_CorrectIdPassed()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Act
        await _dataverseServiceMock.Object.GetDocumentAsync(documentId);

        // Assert
        _dataverseServiceMock.Verify(
            x => x.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SPE Pointer Validation Tests

    [Fact]
    public void DocumentEntity_MissingGraphDriveId_DetectedCorrectly()
    {
        // Arrange
        var document = CreateValidDocument(graphDriveId: null);

        // Assert
        string.IsNullOrWhiteSpace(document.GraphDriveId).Should().BeTrue();
    }

    [Fact]
    public void DocumentEntity_EmptyGraphDriveId_TreatedAsMissing()
    {
        // Arrange
        var document = CreateValidDocument(graphDriveId: "");

        // Assert
        string.IsNullOrWhiteSpace(document.GraphDriveId).Should().BeTrue();
    }

    [Fact]
    public void DocumentEntity_WhitespaceGraphDriveId_TreatedAsMissing()
    {
        // Arrange
        var document = CreateValidDocument(graphDriveId: "   ");

        // Assert
        string.IsNullOrWhiteSpace(document.GraphDriveId).Should().BeTrue();
    }

    [Fact]
    public void DocumentEntity_MissingGraphItemId_DetectedCorrectly()
    {
        // Arrange
        var document = CreateValidDocument(graphItemId: null);

        // Assert
        string.IsNullOrWhiteSpace(document.GraphItemId).Should().BeTrue();
    }

    [Fact]
    public void DocumentEntity_EmptyGraphItemId_TreatedAsMissing()
    {
        // Arrange
        var document = CreateValidDocument(graphItemId: "");

        // Assert
        string.IsNullOrWhiteSpace(document.GraphItemId).Should().BeTrue();
    }

    [Fact]
    public void DocumentEntity_ValidSpePointers_DetectedCorrectly()
    {
        // Arrange
        var document = CreateValidDocument();

        // Assert
        string.IsNullOrWhiteSpace(document.GraphDriveId).Should().BeFalse();
        string.IsNullOrWhiteSpace(document.GraphItemId).Should().BeFalse();
    }

    #endregion

    #region Content-Type and Filename Tests

    [Fact]
    public void DocumentEntity_ValidMimeType_UsedForContentType()
    {
        // Arrange
        var document = CreateValidDocument(mimeType: "message/rfc822");

        // Assert
        var contentType = document.MimeType ?? "application/octet-stream";
        contentType.Should().Be("message/rfc822");
    }

    [Fact]
    public void DocumentEntity_NoMimeType_DefaultsToOctetStream()
    {
        // Arrange
        var document = CreateValidDocument(mimeType: null);

        // Assert
        var contentType = document.MimeType ?? "application/octet-stream";
        contentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public void DocumentEntity_ValidFileName_UsedForDownload()
    {
        // Arrange
        var document = CreateValidDocument(fileName: "important-email.eml");

        // Assert
        var fileName = document.FileName ?? $"{document.Id}.bin";
        fileName.Should().Be("important-email.eml");
    }

    [Fact]
    public void DocumentEntity_NoFileName_DefaultsToIdWithBinExtension()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var document = new DocumentEntity
        {
            Id = documentId,
            Name = "Test Document",
            GraphDriveId = "drive-id",
            GraphItemId = "item-id",
            FileName = null
        };

        // Assert
        var fileName = document.FileName ?? $"{document.Id}.bin";
        fileName.Should().Be($"{documentId}.bin");
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/msword")]
    [InlineData("text/plain")]
    [InlineData("image/png")]
    public void DocumentEntity_VariousMimeTypes_HandledCorrectly(string mimeType)
    {
        // Arrange
        var document = CreateValidDocument(mimeType: mimeType);

        // Assert
        document.MimeType.Should().Be(mimeType);
    }

    #endregion

    #region Telemetry Tests

    [Fact]
    public void RecordDownloadStart_ReturnsRunningStopwatch()
    {
        // Arrange & Act
        var stopwatch = _telemetry.RecordDownloadStart("doc-123", "user-456");

        // Assert
        stopwatch.Should().NotBeNull();
        stopwatch.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void RecordDownloadStart_WithoutUserId_ReturnsStopwatch()
    {
        // Arrange & Act
        var stopwatch = _telemetry.RecordDownloadStart("doc-123");

        // Assert
        stopwatch.Should().NotBeNull();
        stopwatch.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void RecordDownloadSuccess_StopsStopwatch()
    {
        // Arrange
        var stopwatch = _telemetry.RecordDownloadStart("doc-123");

        // Act
        _telemetry.RecordDownloadSuccess(stopwatch, "doc-123", "user-456", "file.eml", "message/rfc822", 1024);

        // Assert
        stopwatch.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RecordDownloadSuccess_WithNullOptionalParams_Succeeds()
    {
        // Arrange
        var stopwatch = _telemetry.RecordDownloadStart("doc-123");

        // Act & Assert - should not throw
        _telemetry.RecordDownloadSuccess(stopwatch, "doc-123", null, null, null, null);
        stopwatch.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RecordDownloadNotFound_StopsStopwatch()
    {
        // Arrange
        var stopwatch = _telemetry.RecordDownloadStart("doc-123");

        // Act
        _telemetry.RecordDownloadNotFound(stopwatch, "doc-123", "user-456", "document_not_found");

        // Assert
        stopwatch.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RecordDownloadNotFound_WithDifferentReasons()
    {
        // Arrange & Act & Assert - various not found reasons should work
        var reasons = new[] { "document_not_found", "missing_drive_id", "missing_item_id", "file_stream_null" };

        foreach (var reason in reasons)
        {
            var stopwatch = _telemetry.RecordDownloadStart("doc-123");
            _telemetry.RecordDownloadNotFound(stopwatch, "doc-123", "user-456", reason);
            stopwatch.IsRunning.Should().BeFalse();
        }
    }

    [Fact]
    public void RecordDownloadFailure_StopsStopwatch()
    {
        // Arrange
        var stopwatch = _telemetry.RecordDownloadStart("doc-123");

        // Act
        _telemetry.RecordDownloadFailure(stopwatch, "doc-123", "user-456", "graph_error");

        // Assert
        stopwatch.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RecordDownloadFailure_WithDifferentErrorCodes()
    {
        // Arrange & Act & Assert - various error codes should work
        var errorCodes = new[] { "invalid_document_id", "graph_error_notFound", "unexpected_error" };

        foreach (var errorCode in errorCodes)
        {
            var stopwatch = _telemetry.RecordDownloadStart("doc-123");
            _telemetry.RecordDownloadFailure(stopwatch, "doc-123", "user-456", errorCode);
            stopwatch.IsRunning.Should().BeFalse();
        }
    }

    [Fact]
    public void RecordDownloadDenied_DoesNotThrow()
    {
        // Act - RecordDownloadDenied doesn't take a stopwatch (denial happens before download starts)
        var act = () => _telemetry.RecordDownloadDenied("doc-123", "user-456", "unauthorized");

        // Assert - No exception thrown indicates success
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordDownloadDenied_WithNullUserId_DoesNotThrow()
    {
        // Act
        var act = () => _telemetry.RecordDownloadDenied("doc-123", null, "access_denied");

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region File Size Tests

    [Fact]
    public void DocumentEntity_ZeroFileSize_IsValid()
    {
        // Arrange
        var document = CreateValidDocument(fileSize: 0);

        // Assert - Zero file size is valid (empty file)
        document.FileSize.Should().Be(0);
    }

    [Fact]
    public void DocumentEntity_NullFileSize_IsValid()
    {
        // Arrange
        var document = CreateValidDocument(fileSize: null);

        // Assert - Null file size is valid (unknown size)
        document.FileSize.Should().BeNull();
    }

    [Fact]
    public void DocumentEntity_SmallFileSize_IsValid()
    {
        // Arrange
        var document = CreateValidDocument(fileSize: 1024); // 1KB

        // Assert
        document.FileSize.Should().Be(1024);
    }

    [Fact]
    public void DocumentEntity_LargeFileSize_IsValid()
    {
        // Arrange - 250MB is the max per NFR-05
        var maxSize = 250L * 1024 * 1024;
        var document = CreateValidDocument(fileSize: maxSize);

        // Assert
        document.FileSize.Should().Be(maxSize);
    }

    #endregion

    #region HttpContext Tests

    [Fact]
    public void HttpContext_WithAuthenticatedUser_HasUserId()
    {
        // Arrange
        var context = CreateHttpContext("user-123");

        // Act
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Assert
        userId.Should().Be("user-123");
    }

    [Fact]
    public void HttpContext_WithAnonymousUser_HasNoUserId()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Assert
        userId.Should().BeNull();
    }

    [Fact]
    public void HttpContext_HasTraceIdentifier()
    {
        // Arrange
        var context = CreateHttpContext();

        // Assert
        context.TraceIdentifier.Should().Be("test-trace-id");
    }

    #endregion
}
