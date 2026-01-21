using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Bff.Api.Api.Office.Errors;

/// <summary>
/// ProblemDetails factory methods for Office integration endpoints.
/// Per ADR-019 all errors must return RFC 7807 ProblemDetails with correlation IDs.
/// </summary>
/// <remarks>
/// <para>
/// Each factory method creates a ProblemDetails response with:
/// - Stable error code (OFFICE_001-015) in the extensions
/// - Correlation ID for distributed tracing
/// - Type URI pointing to error documentation
/// - User-friendly title and detail messages
/// </para>
/// <para>
/// Usage: return OfficeProblemDetailsExtensions.ValidationError(...);
/// </para>
/// </remarks>
public static class OfficeProblemDetailsExtensions
{
    #region Validation Errors (400)

    /// <summary>
    /// OFFICE_001: Invalid source type - sourceType not recognized.
    /// </summary>
    public static IResult InvalidSourceType(
        string sourceType,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.InvalidSourceType,
            $"Source type '{sourceType}' is not valid. Expected: OutlookEmail, OutlookAttachment, or WordDocument.",
            correlationId,
            instance);
    }

    /// <summary>
    /// OFFICE_002: Invalid association type - associationType not recognized.
    /// </summary>
    public static IResult InvalidAssociationType(
        string associationType,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.InvalidAssociationType,
            $"Association type '{associationType}' is not valid. Expected: Matter, Project, Invoice, Account, or Contact.",
            correlationId,
            instance);
    }

    /// <summary>
    /// OFFICE_003: Association required - associationId missing.
    /// </summary>
    public static IResult AssociationRequired(
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.AssociationRequired,
            "Association target is required. Select a Matter, Project, Invoice, Account, or Contact to associate the document with.",
            correlationId,
            instance);
    }

    /// <summary>
    /// OFFICE_004: Attachment too large - Single file exceeds 25MB.
    /// </summary>
    public static IResult AttachmentTooLarge(
        string fileName,
        long fileSize,
        long maxSize,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.AttachmentTooLarge,
            $"Attachment '{fileName}' ({FormatFileSize(fileSize)}) exceeds the maximum allowed size of {FormatFileSize(maxSize)}.",
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["fileSize"] = fileSize,
                ["maxSize"] = maxSize
            });
    }

    /// <summary>
    /// OFFICE_005: Total size exceeded - Combined size exceeds 100MB.
    /// </summary>
    public static IResult TotalSizeExceeded(
        long totalSize,
        long maxTotalSize,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.TotalSizeExceeded,
            $"Total size ({FormatFileSize(totalSize)}) exceeds the maximum allowed total of {FormatFileSize(maxTotalSize)}.",
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["totalSize"] = totalSize,
                ["maxTotalSize"] = maxTotalSize
            });
    }

    /// <summary>
    /// OFFICE_006: Blocked file type - Dangerous extension.
    /// </summary>
    public static IResult BlockedFileType(
        string fileName,
        string extension,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.BlockedFileType,
            $"File type '{extension}' is not allowed for security reasons. The file '{fileName}' cannot be uploaded.",
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["extension"] = extension
            });
    }

    /// <summary>
    /// Creates a validation error with field-level errors (for FluentValidation-style responses).
    /// </summary>
    public static IResult ValidationErrors(
        Dictionary<string, string[]> errors,
        string correlationId,
        string instance = "/office/save")
    {
        return Results.ValidationProblem(
            errors,
            type: $"{OfficeErrorCodes.TypeBaseUri}validation-error",
            title: "Validation Error",
            instance: instance,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = OfficeErrorCodes.AssociationRequired,
                ["correlationId"] = correlationId
            });
    }

    #endregion

    #region Not Found Errors (404)

    /// <summary>
    /// OFFICE_007: Association target not found - Entity doesn't exist.
    /// </summary>
    public static IResult AssociationNotFound(
        string associationType,
        string associationId,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.AssociationNotFound,
            $"The {associationType} with ID '{associationId}' was not found or you don't have access to it.",
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["associationType"] = associationType,
                ["associationId"] = associationId
            });
    }

    /// <summary>
    /// OFFICE_008: Job not found - Job ID invalid or expired.
    /// </summary>
    public static IResult JobNotFound(
        string jobId,
        string correlationId,
        string instance = "/office/jobs")
    {
        return CreateProblemResult(
            OfficeErrorCodes.JobNotFound,
            $"Job '{jobId}' was not found. It may have expired or the ID is invalid.",
            correlationId,
            $"{instance}/{jobId}");
    }

    #endregion

    #region Authorization Errors (403)

    /// <summary>
    /// OFFICE_009: Access denied - User lacks permission.
    /// </summary>
    public static IResult AccessDenied(
        string resource,
        string correlationId,
        string? reason = null,
        string instance = "/office")
    {
        var detail = string.IsNullOrEmpty(reason)
            ? $"You do not have permission to access '{resource}'."
            : $"You do not have permission to access '{resource}'. {reason}";

        return CreateProblemResult(
            OfficeErrorCodes.AccessDenied,
            detail,
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["resource"] = resource,
                ["reason"] = reason
            });
    }

    /// <summary>
    /// OFFICE_010: Cannot create entity - User lacks create permission.
    /// </summary>
    public static IResult CannotCreateEntity(
        string entityType,
        string correlationId,
        string instance = "/office/quickcreate")
    {
        return CreateProblemResult(
            OfficeErrorCodes.CannotCreateEntity,
            $"You do not have permission to create {entityType} records.",
            correlationId,
            $"{instance}/{entityType.ToLowerInvariant()}");
    }

    #endregion

    #region Conflict Errors (409)

    /// <summary>
    /// OFFICE_011: Document already exists - Duplicate detected.
    /// </summary>
    public static IResult DocumentAlreadyExists(
        string documentId,
        string documentName,
        string correlationId,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.DocumentAlreadyExists,
            $"A document with the same content already exists: '{documentName}'.",
            correlationId,
            instance,
            new Dictionary<string, object?>
            {
                ["existingDocumentId"] = documentId,
                ["documentName"] = documentName
            });
    }

    #endregion

    #region Service Errors (502)

    /// <summary>
    /// OFFICE_012: SPE upload failed - SharePoint Embedded error.
    /// </summary>
    public static IResult SpeUploadFailed(
        string correlationId,
        string? reason = null,
        string instance = "/office/save")
    {
        return CreateProblemResult(
            OfficeErrorCodes.SpeUploadFailed,
            string.IsNullOrEmpty(reason)
                ? "Failed to upload file to storage. Please try again later."
                : $"Failed to upload file to storage: {reason}",
            correlationId,
            instance);
    }

    /// <summary>
    /// OFFICE_013: Graph API error - Microsoft Graph failure.
    /// </summary>
    public static IResult GraphApiError(
        string correlationId,
        string? graphRequestId = null,
        string? graphErrorCode = null,
        string? reason = null,
        string instance = "/office")
    {
        var extensions = new Dictionary<string, object?>
        {
            ["graphRequestId"] = graphRequestId,
            ["graphErrorCode"] = graphErrorCode
        };

        return CreateProblemResult(
            OfficeErrorCodes.GraphApiError,
            string.IsNullOrEmpty(reason)
                ? "A Microsoft Graph API error occurred. Please try again later."
                : $"A Microsoft Graph API error occurred: {reason}",
            correlationId,
            instance,
            extensions);
    }

    /// <summary>
    /// Creates a Graph API error from an ODataError exception.
    /// </summary>
    public static IResult FromGraphException(
        ODataError ex,
        string correlationId,
        string instance = "/office")
    {
        string? graphRequestId = null;
        try
        {
            graphRequestId = ex.ResponseHeaders?
                .Where(h => h.Key.Equals("request-id", StringComparison.OrdinalIgnoreCase) ||
                            h.Key.Equals("client-request-id", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .FirstOrDefault();
        }
        catch
        {
            // Ignore header access errors
        }

        // Determine if this is an auth error that should return 403
        var status = ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 502;
        var errorCode = ex.Error?.Code ?? "unknown";

        if (status is 401 or 403)
        {
            return AccessDenied(
                "Microsoft Graph API",
                correlationId,
                ex.Error?.Message,
                instance);
        }

        return GraphApiError(
            correlationId,
            graphRequestId,
            errorCode,
            ex.Error?.Message,
            instance);
    }

    /// <summary>
    /// OFFICE_014: Dataverse error - Dataverse operation failed.
    /// </summary>
    public static IResult DataverseError(
        string correlationId,
        string? reason = null,
        string instance = "/office")
    {
        return CreateProblemResult(
            OfficeErrorCodes.DataverseError,
            string.IsNullOrEmpty(reason)
                ? "A Dataverse operation failed. Please try again later."
                : $"A Dataverse operation failed: {reason}",
            correlationId,
            instance);
    }

    #endregion

    #region Service Unavailable (503)

    /// <summary>
    /// OFFICE_015: Processing unavailable - Workers offline.
    /// </summary>
    public static IResult ProcessingUnavailable(
        string correlationId,
        int? retryAfterSeconds = null,
        string instance = "/office")
    {
        var extensions = new Dictionary<string, object?>();
        if (retryAfterSeconds.HasValue)
        {
            extensions["retryAfterSeconds"] = retryAfterSeconds.Value;
        }

        return CreateProblemResult(
            OfficeErrorCodes.ProcessingUnavailable,
            "Document processing service is temporarily unavailable. Please try again later.",
            correlationId,
            instance,
            extensions);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a standard ProblemDetails result with Office-specific extensions.
    /// </summary>
    private static IResult CreateProblemResult(
        string errorCode,
        string detail,
        string correlationId,
        string instance,
        Dictionary<string, object?>? additionalExtensions = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        };

        if (additionalExtensions != null)
        {
            foreach (var kvp in additionalExtensions)
            {
                if (kvp.Value != null)
                {
                    extensions[kvp.Key] = kvp.Value;
                }
            }
        }

        return Results.Problem(
            type: OfficeErrorCodes.GetTypeUri(errorCode),
            title: OfficeErrorCodes.GetTitle(errorCode),
            detail: detail,
            statusCode: OfficeErrorCodes.GetStatusCode(errorCode),
            instance: instance,
            extensions: extensions);
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {suffixes[suffixIndex]}";
    }

    #endregion
}
