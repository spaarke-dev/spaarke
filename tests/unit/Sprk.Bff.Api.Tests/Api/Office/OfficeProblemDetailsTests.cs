using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Api.Office.Errors;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Tests for Office integration ProblemDetails error handling.
/// Verifies error codes OFFICE_001-015 per spec.md and ADR-019.
/// </summary>
public class OfficeProblemDetailsTests
{
    private readonly string _correlationId = Guid.NewGuid().ToString();

    private static async Task<(int status, string body)> ExecuteResultAsync(IResult result)
    {
        var ctx = new DefaultHttpContext();
        var ms = new MemoryStream();
        ctx.Response.Body = ms;
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        return (ctx.Response.StatusCode, json);
    }

    private static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);

    #region Validation Error Tests (OFFICE_001-006)

    [Fact]
    public async Task InvalidSourceType_Returns400_WithOFFICE_001()
    {
        // Arrange
        var invalidType = "InvalidType";

        // Act
        var result = OfficeProblemDetailsExtensions.InvalidSourceType(invalidType, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_001");
        doc.RootElement.GetProperty("correlationId").GetString().Should().Be(_correlationId);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Invalid Source Type");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("validation-error");
    }

    [Fact]
    public async Task InvalidAssociationType_Returns400_WithOFFICE_002()
    {
        // Arrange
        var invalidType = "InvalidEntity";

        // Act
        var result = OfficeProblemDetailsExtensions.InvalidAssociationType(invalidType, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_002");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("InvalidEntity");
    }

    [Fact]
    public async Task AssociationRequired_Returns400_WithOFFICE_003()
    {
        // Act
        var result = OfficeProblemDetailsExtensions.AssociationRequired(_correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_003");
        doc.RootElement.GetProperty("title").GetString().Should().Be("Association Required");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("Matter, Project, Invoice, Account, or Contact");
    }

    [Fact]
    public async Task AttachmentTooLarge_Returns400_WithOFFICE_004()
    {
        // Arrange
        var fileName = "large-file.pdf";
        long fileSize = 30 * 1024 * 1024; // 30MB
        long maxSize = 25 * 1024 * 1024; // 25MB

        // Act
        var result = OfficeProblemDetailsExtensions.AttachmentTooLarge(fileName, fileSize, maxSize, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_004");
        doc.RootElement.GetProperty("fileName").GetString().Should().Be(fileName);
        doc.RootElement.GetProperty("fileSize").GetInt64().Should().Be(fileSize);
        doc.RootElement.GetProperty("maxSize").GetInt64().Should().Be(maxSize);
    }

    [Fact]
    public async Task TotalSizeExceeded_Returns400_WithOFFICE_005()
    {
        // Arrange
        long totalSize = 120 * 1024 * 1024; // 120MB
        long maxTotalSize = 100 * 1024 * 1024; // 100MB

        // Act
        var result = OfficeProblemDetailsExtensions.TotalSizeExceeded(totalSize, maxTotalSize, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_005");
        doc.RootElement.GetProperty("totalSize").GetInt64().Should().Be(totalSize);
        doc.RootElement.GetProperty("maxTotalSize").GetInt64().Should().Be(maxTotalSize);
    }

    [Fact]
    public async Task BlockedFileType_Returns400_WithOFFICE_006()
    {
        // Arrange
        var fileName = "script.exe";
        var extension = ".exe";

        // Act
        var result = OfficeProblemDetailsExtensions.BlockedFileType(fileName, extension, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(400);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_006");
        doc.RootElement.GetProperty("fileName").GetString().Should().Be(fileName);
        doc.RootElement.GetProperty("extension").GetString().Should().Be(extension);
    }

    #endregion

    #region Not Found Error Tests (OFFICE_007-008)

    [Fact]
    public async Task AssociationNotFound_Returns404_WithOFFICE_007()
    {
        // Arrange
        var entityType = "Matter";
        var entityId = Guid.NewGuid().ToString();

        // Act
        var result = OfficeProblemDetailsExtensions.AssociationNotFound(entityType, entityId, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(404);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_007");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("not-found");
        doc.RootElement.GetProperty("associationType").GetString().Should().Be(entityType);
        doc.RootElement.GetProperty("associationId").GetString().Should().Be(entityId);
    }

    [Fact]
    public async Task JobNotFound_Returns404_WithOFFICE_008()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();

        // Act
        var result = OfficeProblemDetailsExtensions.JobNotFound(jobId, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(404);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_008");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(jobId);
    }

    #endregion

    #region Authorization Error Tests (OFFICE_009-010)

    [Fact]
    public async Task AccessDenied_Returns403_WithOFFICE_009()
    {
        // Arrange
        var resource = "Document";

        // Act
        var result = OfficeProblemDetailsExtensions.AccessDenied(resource, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(403);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("forbidden");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(resource);
    }

    [Fact]
    public async Task AccessDenied_WithReason_IncludesReasonInDetail()
    {
        // Arrange
        var resource = "Matter";
        var reason = "User is not assigned to this team.";

        // Act
        var result = OfficeProblemDetailsExtensions.AccessDenied(resource, _correlationId, reason);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(403);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(reason);
    }

    [Fact]
    public async Task CannotCreateEntity_Returns403_WithOFFICE_010()
    {
        // Arrange
        var entityType = "Matter";

        // Act
        var result = OfficeProblemDetailsExtensions.CannotCreateEntity(entityType, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(403);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_010");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(entityType);
    }

    #endregion

    #region Conflict Error Tests (OFFICE_011)

    [Fact]
    public async Task DocumentAlreadyExists_Returns409_WithOFFICE_011()
    {
        // Arrange
        var documentId = Guid.NewGuid().ToString();
        var documentName = "Contract-2026.pdf";

        // Act
        var result = OfficeProblemDetailsExtensions.DocumentAlreadyExists(documentId, documentName, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(409);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_011");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("conflict");
        doc.RootElement.GetProperty("existingDocumentId").GetString().Should().Be(documentId);
        doc.RootElement.GetProperty("documentName").GetString().Should().Be(documentName);
    }

    #endregion

    #region Service Error Tests (OFFICE_012-014)

    [Fact]
    public async Task SpeUploadFailed_Returns502_WithOFFICE_012()
    {
        // Act
        var result = OfficeProblemDetailsExtensions.SpeUploadFailed(_correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(502);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_012");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("service-error");
    }

    [Fact]
    public async Task SpeUploadFailed_WithReason_IncludesReasonInDetail()
    {
        // Arrange
        var reason = "Container quota exceeded.";

        // Act
        var result = OfficeProblemDetailsExtensions.SpeUploadFailed(_correlationId, reason);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(502);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(reason);
    }

    [Fact]
    public async Task GraphApiError_Returns502_WithOFFICE_013()
    {
        // Act
        var result = OfficeProblemDetailsExtensions.GraphApiError(
            _correlationId,
            graphRequestId: "graph-req-123",
            graphErrorCode: "accessDenied");
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(502);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_013");
        doc.RootElement.GetProperty("graphRequestId").GetString().Should().Be("graph-req-123");
        doc.RootElement.GetProperty("graphErrorCode").GetString().Should().Be("accessDenied");
    }

    [Fact]
    public async Task FromGraphException_MapsAuthErrorsToOFFICE_009()
    {
        // Arrange
        var ex = new ODataError
        {
            Error = new MainError
            {
                Code = "accessDenied",
                Message = "Insufficient privileges"
            },
            ResponseStatusCode = 403
        };

        // Act
        var result = OfficeProblemDetailsExtensions.FromGraphException(ex, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert - Graph auth errors should map to OFFICE_009 (Access Denied)
        status.Should().Be(403);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
    }

    [Fact]
    public async Task FromGraphException_MapsOtherErrorsToOFFICE_013()
    {
        // Arrange
        var ex = new ODataError
        {
            Error = new MainError
            {
                Code = "itemNotFound",
                Message = "Resource not found"
            },
            ResponseStatusCode = 404
        };

        // Act
        var result = OfficeProblemDetailsExtensions.FromGraphException(ex, _correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert - Non-auth Graph errors should map to OFFICE_013 (Graph API Error)
        status.Should().Be(502);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_013");
    }

    [Fact]
    public async Task DataverseError_Returns502_WithOFFICE_014()
    {
        // Arrange
        var reason = "Connection timeout";

        // Act
        var result = OfficeProblemDetailsExtensions.DataverseError(_correlationId, reason);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(502);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_014");
        doc.RootElement.GetProperty("detail").GetString().Should().Contain(reason);
    }

    #endregion

    #region Service Unavailable Tests (OFFICE_015)

    [Fact]
    public async Task ProcessingUnavailable_Returns503_WithOFFICE_015()
    {
        // Act
        var result = OfficeProblemDetailsExtensions.ProcessingUnavailable(_correlationId);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(503);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_015");
        doc.RootElement.GetProperty("type").GetString().Should().Contain("unavailable");
    }

    [Fact]
    public async Task ProcessingUnavailable_WithRetryAfter_IncludesRetrySeconds()
    {
        // Arrange
        var retryAfterSeconds = 30;

        // Act
        var result = OfficeProblemDetailsExtensions.ProcessingUnavailable(_correlationId, retryAfterSeconds);
        var (status, json) = await ExecuteResultAsync(result);

        // Assert
        status.Should().Be(503);

        using var doc = ParseJson(json);
        doc.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(retryAfterSeconds);
    }

    #endregion

    #region Correlation ID Tests

    [Fact]
    public async Task AllErrors_IncludeCorrelationId()
    {
        // Test a representative sample of error types
        var results = new[]
        {
            OfficeProblemDetailsExtensions.InvalidSourceType("test", _correlationId),
            OfficeProblemDetailsExtensions.AssociationRequired(_correlationId),
            OfficeProblemDetailsExtensions.AssociationNotFound("Matter", "123", _correlationId),
            OfficeProblemDetailsExtensions.AccessDenied("test", _correlationId),
            OfficeProblemDetailsExtensions.SpeUploadFailed(_correlationId),
            OfficeProblemDetailsExtensions.ProcessingUnavailable(_correlationId)
        };

        foreach (var result in results)
        {
            var (_, json) = await ExecuteResultAsync(result);
            using var doc = ParseJson(json);
            doc.RootElement.GetProperty("correlationId").GetString().Should().Be(_correlationId);
        }
    }

    #endregion
}
