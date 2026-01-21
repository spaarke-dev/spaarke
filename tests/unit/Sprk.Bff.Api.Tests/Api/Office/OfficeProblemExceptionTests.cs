using FluentAssertions;
using Sprk.Bff.Api.Api.Office.Errors;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Tests for OfficeProblemException factory methods.
/// Verifies each error code produces correct exception properties.
/// </summary>
public class OfficeProblemExceptionTests
{
    #region Validation Exceptions (OFFICE_001-006)

    [Fact]
    public void InvalidSourceType_CreatesException_WithCorrectCode()
    {
        // Arrange
        var sourceType = "InvalidType";

        // Act
        var ex = OfficeProblemException.InvalidSourceType(sourceType);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_001");
        ex.StatusCode.Should().Be(400);
        ex.Title.Should().Be("Invalid Source Type");
        ex.Detail.Should().Contain(sourceType);
        ex.Instance.Should().Be("/office/save");
    }

    [Fact]
    public void InvalidAssociationType_CreatesException_WithCorrectCode()
    {
        // Arrange
        var associationType = "BadType";

        // Act
        var ex = OfficeProblemException.InvalidAssociationType(associationType);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_002");
        ex.StatusCode.Should().Be(400);
        ex.Title.Should().Be("Invalid Association Type");
        ex.Detail.Should().Contain(associationType);
    }

    [Fact]
    public void AssociationRequired_CreatesException_WithCorrectCode()
    {
        // Act
        var ex = OfficeProblemException.AssociationRequired();

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_003");
        ex.StatusCode.Should().Be(400);
        ex.Title.Should().Be("Association Required");
    }

    [Fact]
    public void AttachmentTooLarge_CreatesException_WithFileInfo()
    {
        // Arrange
        var fileName = "huge.pdf";
        long fileSize = 50 * 1024 * 1024;
        long maxSize = 25 * 1024 * 1024;

        // Act
        var ex = OfficeProblemException.AttachmentTooLarge(fileName, fileSize, maxSize);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_004");
        ex.StatusCode.Should().Be(400);
        ex.Extensions.Should().ContainKey("fileName").WhoseValue.Should().Be(fileName);
        ex.Extensions.Should().ContainKey("fileSize").WhoseValue.Should().Be(fileSize);
        ex.Extensions.Should().ContainKey("maxSize").WhoseValue.Should().Be(maxSize);
    }

    [Fact]
    public void TotalSizeExceeded_CreatesException_WithSizeInfo()
    {
        // Arrange
        long totalSize = 150 * 1024 * 1024;
        long maxTotal = 100 * 1024 * 1024;

        // Act
        var ex = OfficeProblemException.TotalSizeExceeded(totalSize, maxTotal);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_005");
        ex.StatusCode.Should().Be(400);
        ex.Extensions.Should().ContainKey("totalSize");
        ex.Extensions.Should().ContainKey("maxTotalSize");
    }

    [Fact]
    public void BlockedFileType_CreatesException_WithFileInfo()
    {
        // Arrange
        var fileName = "virus.exe";
        var extension = ".exe";

        // Act
        var ex = OfficeProblemException.BlockedFileType(fileName, extension);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_006");
        ex.StatusCode.Should().Be(400);
        ex.Extensions.Should().ContainKey("fileName").WhoseValue.Should().Be(fileName);
        ex.Extensions.Should().ContainKey("extension").WhoseValue.Should().Be(extension);
    }

    #endregion

    #region Not Found Exceptions (OFFICE_007-008)

    [Fact]
    public void AssociationNotFound_CreatesException_WithEntityInfo()
    {
        // Arrange
        var entityType = "Matter";
        var entityId = Guid.NewGuid().ToString();

        // Act
        var ex = OfficeProblemException.AssociationNotFound(entityType, entityId);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_007");
        ex.StatusCode.Should().Be(404);
        ex.Title.Should().Be("Association Target Not Found");
        ex.Extensions.Should().ContainKey("associationType").WhoseValue.Should().Be(entityType);
        ex.Extensions.Should().ContainKey("associationId").WhoseValue.Should().Be(entityId);
    }

    [Fact]
    public void JobNotFound_CreatesException_WithJobId()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();

        // Act
        var ex = OfficeProblemException.JobNotFound(jobId);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_008");
        ex.StatusCode.Should().Be(404);
        ex.Instance.Should().Contain(jobId);
    }

    #endregion

    #region Authorization Exceptions (OFFICE_009-010)

    [Fact]
    public void AccessDenied_CreatesException_WithResource()
    {
        // Arrange
        var resource = "Document";

        // Act
        var ex = OfficeProblemException.AccessDenied(resource);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_009");
        ex.StatusCode.Should().Be(403);
        ex.Title.Should().Be("Access Denied");
        ex.Extensions.Should().ContainKey("resource").WhoseValue.Should().Be(resource);
    }

    [Fact]
    public void AccessDenied_WithReason_IncludesReasonInExtensions()
    {
        // Arrange
        var resource = "Matter";
        var reason = "Not in team";

        // Act
        var ex = OfficeProblemException.AccessDenied(resource, reason);

        // Assert
        ex.Extensions.Should().ContainKey("reason").WhoseValue.Should().Be(reason);
        ex.Detail.Should().Contain(reason);
    }

    [Fact]
    public void CannotCreateEntity_CreatesException_WithEntityType()
    {
        // Arrange
        var entityType = "Invoice";

        // Act
        var ex = OfficeProblemException.CannotCreateEntity(entityType);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_010");
        ex.StatusCode.Should().Be(403);
        ex.Instance.Should().Contain("invoice");
    }

    #endregion

    #region Conflict Exceptions (OFFICE_011)

    [Fact]
    public void DocumentAlreadyExists_CreatesException_WithDocumentInfo()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var documentName = "Contract.pdf";

        // Act
        var ex = OfficeProblemException.DocumentAlreadyExists(documentId, documentName);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_011");
        ex.StatusCode.Should().Be(409);
        ex.Title.Should().Be("Document Already Exists");
        ex.Extensions.Should().ContainKey("existingDocumentId").WhoseValue.Should().Be(documentId);
        ex.Extensions.Should().ContainKey("documentName").WhoseValue.Should().Be(documentName);
    }

    #endregion

    #region Service Exceptions (OFFICE_012-014)

    [Fact]
    public void SpeUploadFailed_CreatesException_WithCorrectCode()
    {
        // Act
        var ex = OfficeProblemException.SpeUploadFailed();

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_012");
        ex.StatusCode.Should().Be(502);
        ex.Title.Should().Be("SPE Upload Failed");
    }

    [Fact]
    public void SpeUploadFailed_WithReason_IncludesReasonInDetail()
    {
        // Arrange
        var reason = "Quota exceeded";

        // Act
        var ex = OfficeProblemException.SpeUploadFailed(reason);

        // Assert
        ex.Detail.Should().Contain(reason);
    }

    [Fact]
    public void GraphApiError_CreatesException_WithGraphInfo()
    {
        // Arrange
        var requestId = "req-123";
        var errorCode = "itemNotFound";

        // Act
        var ex = OfficeProblemException.GraphApiError(requestId, errorCode);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_013");
        ex.StatusCode.Should().Be(502);
        ex.Extensions.Should().ContainKey("graphRequestId").WhoseValue.Should().Be(requestId);
        ex.Extensions.Should().ContainKey("graphErrorCode").WhoseValue.Should().Be(errorCode);
    }

    [Fact]
    public void DataverseError_CreatesException_WithCorrectCode()
    {
        // Arrange
        var reason = "Connection failed";

        // Act
        var ex = OfficeProblemException.DataverseError(reason);

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_014");
        ex.StatusCode.Should().Be(502);
        ex.Detail.Should().Contain(reason);
    }

    #endregion

    #region Service Unavailable Exceptions (OFFICE_015)

    [Fact]
    public void ProcessingUnavailable_CreatesException_WithCorrectCode()
    {
        // Act
        var ex = OfficeProblemException.ProcessingUnavailable();

        // Assert
        ex.ErrorCode.Should().Be("OFFICE_015");
        ex.StatusCode.Should().Be(503);
        ex.Title.Should().Be("Processing Unavailable");
    }

    [Fact]
    public void ProcessingUnavailable_WithRetryAfter_IncludesRetryInfo()
    {
        // Arrange
        var retryAfter = 60;

        // Act
        var ex = OfficeProblemException.ProcessingUnavailable(retryAfter);

        // Assert
        ex.Extensions.Should().ContainKey("retryAfterSeconds").WhoseValue.Should().Be(retryAfter);
    }

    #endregion

    #region Error Code Mapping Tests

    [Theory]
    [InlineData("OFFICE_001", 400)]
    [InlineData("OFFICE_002", 400)]
    [InlineData("OFFICE_003", 400)]
    [InlineData("OFFICE_004", 400)]
    [InlineData("OFFICE_005", 400)]
    [InlineData("OFFICE_006", 400)]
    [InlineData("OFFICE_007", 404)]
    [InlineData("OFFICE_008", 404)]
    [InlineData("OFFICE_009", 403)]
    [InlineData("OFFICE_010", 403)]
    [InlineData("OFFICE_011", 409)]
    [InlineData("OFFICE_012", 502)]
    [InlineData("OFFICE_013", 502)]
    [InlineData("OFFICE_014", 502)]
    [InlineData("OFFICE_015", 503)]
    public void OfficeErrorCodes_GetStatusCode_ReturnsCorrectStatus(string errorCode, int expectedStatus)
    {
        // Act
        var status = OfficeErrorCodes.GetStatusCode(errorCode);

        // Assert
        status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("OFFICE_001", "validation-error")]
    [InlineData("OFFICE_007", "not-found")]
    [InlineData("OFFICE_009", "forbidden")]
    [InlineData("OFFICE_011", "conflict")]
    [InlineData("OFFICE_012", "service-error")]
    [InlineData("OFFICE_015", "unavailable")]
    public void OfficeErrorCodes_GetTypeUri_ReturnsCorrectType(string errorCode, string expectedType)
    {
        // Act
        var typeUri = OfficeErrorCodes.GetTypeUri(errorCode);

        // Assert
        typeUri.Should().Contain(expectedType);
        typeUri.Should().StartWith("https://spaarke.com/errors/office/");
    }

    #endregion
}
