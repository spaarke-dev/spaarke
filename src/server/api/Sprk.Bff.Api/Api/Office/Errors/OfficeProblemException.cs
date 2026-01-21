namespace Sprk.Bff.Api.Api.Office.Errors;

/// <summary>
/// Exception that maps to RFC 7807 ProblemDetails with Office-specific error codes (OFFICE_001-015).
/// </summary>
/// <remarks>
/// <para>
/// Throw this exception from Office-related services to return structured error responses.
/// The exception handler will convert it to a ProblemDetails response with the appropriate
/// error code, correlation ID, and user-friendly message.
/// </para>
/// <para>
/// Error codes follow the spec.md Error Code Catalog:
/// - OFFICE_001-006: Validation errors (400)
/// - OFFICE_007-008: Not found errors (404)
/// - OFFICE_009-010: Authorization errors (403)
/// - OFFICE_011: Conflict errors (409)
/// - OFFICE_012-014: Service errors (502)
/// - OFFICE_015: Service unavailable (503)
/// </para>
/// </remarks>
public sealed class OfficeProblemException : Exception
{
    /// <summary>
    /// The Office error code (OFFICE_001-015).
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Detailed error message for the user.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// The request path where the error occurred.
    /// </summary>
    public string Instance { get; }

    /// <summary>
    /// Additional properties to include in the error response.
    /// </summary>
    public Dictionary<string, object?>? Extensions { get; }

    /// <summary>
    /// Creates a new OfficeProblemException.
    /// </summary>
    /// <param name="errorCode">The Office error code (OFFICE_001-015).</param>
    /// <param name="detail">Detailed error message for the user.</param>
    /// <param name="instance">The request path where the error occurred.</param>
    /// <param name="extensions">Additional properties to include in the error response.</param>
    public OfficeProblemException(
        string errorCode,
        string detail,
        string instance = "/office",
        Dictionary<string, object?>? extensions = null)
        : base($"{errorCode}: {detail}")
    {
        ErrorCode = errorCode;
        Detail = detail;
        Instance = instance;
        Extensions = extensions;
    }

    /// <summary>
    /// Gets the HTTP status code for this error.
    /// </summary>
    public int StatusCode => OfficeErrorCodes.GetStatusCode(ErrorCode);

    /// <summary>
    /// Gets the error title for this error.
    /// </summary>
    public string Title => OfficeErrorCodes.GetTitle(ErrorCode);

    /// <summary>
    /// Gets the type URI for this error.
    /// </summary>
    public string Type => OfficeErrorCodes.GetTypeUri(ErrorCode);

    #region Factory Methods

    /// <summary>
    /// Creates an exception for OFFICE_001: Invalid source type.
    /// </summary>
    public static OfficeProblemException InvalidSourceType(string sourceType)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.InvalidSourceType,
            $"Source type '{sourceType}' is not valid. Expected: OutlookEmail, OutlookAttachment, or WordDocument.",
            "/office/save");
    }

    /// <summary>
    /// Creates an exception for OFFICE_002: Invalid association type.
    /// </summary>
    public static OfficeProblemException InvalidAssociationType(string associationType)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.InvalidAssociationType,
            $"Association type '{associationType}' is not valid. Expected: Matter, Project, Invoice, Account, or Contact.",
            "/office/save");
    }

    /// <summary>
    /// Creates an exception for OFFICE_003: Association required.
    /// </summary>
    public static OfficeProblemException AssociationRequired()
    {
        return new OfficeProblemException(
            OfficeErrorCodes.AssociationRequired,
            "Association target is required. Select a Matter, Project, Invoice, Account, or Contact to associate the document with.",
            "/office/save");
    }

    /// <summary>
    /// Creates an exception for OFFICE_004: Attachment too large.
    /// </summary>
    public static OfficeProblemException AttachmentTooLarge(string fileName, long fileSize, long maxSize)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.AttachmentTooLarge,
            $"Attachment '{fileName}' ({FormatFileSize(fileSize)}) exceeds the maximum allowed size of {FormatFileSize(maxSize)}.",
            "/office/save",
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["fileSize"] = fileSize,
                ["maxSize"] = maxSize
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_005: Total size exceeded.
    /// </summary>
    public static OfficeProblemException TotalSizeExceeded(long totalSize, long maxTotalSize)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.TotalSizeExceeded,
            $"Total size ({FormatFileSize(totalSize)}) exceeds the maximum allowed total of {FormatFileSize(maxTotalSize)}.",
            "/office/save",
            new Dictionary<string, object?>
            {
                ["totalSize"] = totalSize,
                ["maxTotalSize"] = maxTotalSize
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_006: Blocked file type.
    /// </summary>
    public static OfficeProblemException BlockedFileType(string fileName, string extension)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.BlockedFileType,
            $"File type '{extension}' is not allowed for security reasons. The file '{fileName}' cannot be uploaded.",
            "/office/save",
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["extension"] = extension
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_007: Association target not found.
    /// </summary>
    public static OfficeProblemException AssociationNotFound(string associationType, string associationId)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.AssociationNotFound,
            $"The {associationType} with ID '{associationId}' was not found or you don't have access to it.",
            "/office/save",
            new Dictionary<string, object?>
            {
                ["associationType"] = associationType,
                ["associationId"] = associationId
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_008: Job not found.
    /// </summary>
    public static OfficeProblemException JobNotFound(string jobId)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.JobNotFound,
            $"Job '{jobId}' was not found. It may have expired or the ID is invalid.",
            $"/office/jobs/{jobId}");
    }

    /// <summary>
    /// Creates an exception for OFFICE_009: Access denied.
    /// </summary>
    public static OfficeProblemException AccessDenied(string resource, string? reason = null)
    {
        var detail = string.IsNullOrEmpty(reason)
            ? $"You do not have permission to access '{resource}'."
            : $"You do not have permission to access '{resource}'. {reason}";

        return new OfficeProblemException(
            OfficeErrorCodes.AccessDenied,
            detail,
            "/office",
            new Dictionary<string, object?>
            {
                ["resource"] = resource,
                ["reason"] = reason
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_010: Cannot create entity.
    /// </summary>
    public static OfficeProblemException CannotCreateEntity(string entityType)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.CannotCreateEntity,
            $"You do not have permission to create {entityType} records.",
            $"/office/quickcreate/{entityType.ToLowerInvariant()}");
    }

    /// <summary>
    /// Creates an exception for OFFICE_011: Document already exists.
    /// </summary>
    public static OfficeProblemException DocumentAlreadyExists(string documentId, string documentName)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.DocumentAlreadyExists,
            $"A document with the same content already exists: '{documentName}'.",
            "/office/save",
            new Dictionary<string, object?>
            {
                ["existingDocumentId"] = documentId,
                ["documentName"] = documentName
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_012: SPE upload failed.
    /// </summary>
    public static OfficeProblemException SpeUploadFailed(string? reason = null)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.SpeUploadFailed,
            string.IsNullOrEmpty(reason)
                ? "Failed to upload file to storage. Please try again later."
                : $"Failed to upload file to storage: {reason}",
            "/office/save");
    }

    /// <summary>
    /// Creates an exception for OFFICE_013: Graph API error.
    /// </summary>
    public static OfficeProblemException GraphApiError(string? graphRequestId = null, string? graphErrorCode = null, string? reason = null)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.GraphApiError,
            string.IsNullOrEmpty(reason)
                ? "A Microsoft Graph API error occurred. Please try again later."
                : $"A Microsoft Graph API error occurred: {reason}",
            "/office",
            new Dictionary<string, object?>
            {
                ["graphRequestId"] = graphRequestId,
                ["graphErrorCode"] = graphErrorCode
            });
    }

    /// <summary>
    /// Creates an exception for OFFICE_014: Dataverse error.
    /// </summary>
    public static OfficeProblemException DataverseError(string? reason = null)
    {
        return new OfficeProblemException(
            OfficeErrorCodes.DataverseError,
            string.IsNullOrEmpty(reason)
                ? "A Dataverse operation failed. Please try again later."
                : $"A Dataverse operation failed: {reason}",
            "/office");
    }

    /// <summary>
    /// Creates an exception for OFFICE_015: Processing unavailable.
    /// </summary>
    public static OfficeProblemException ProcessingUnavailable(int? retryAfterSeconds = null)
    {
        var extensions = retryAfterSeconds.HasValue
            ? new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds.Value }
            : null;

        return new OfficeProblemException(
            OfficeErrorCodes.ProcessingUnavailable,
            "Document processing service is temporarily unavailable. Please try again later.",
            "/office",
            extensions);
    }

    #endregion

    #region Helpers

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
