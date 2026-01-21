namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for the POST /office/share/attach endpoint.
/// Contains packaged attachments ready for Outlook compose API.
/// </summary>
/// <remarks>
/// <para>
/// The response includes an array of AttachmentPackage objects, one for each
/// requested document that was successfully retrieved and validated.
/// </para>
/// <para>
/// Documents that failed validation (not found, access denied, size exceeded)
/// are reported in the Errors array rather than causing the entire request to fail.
/// </para>
/// </remarks>
public record ShareAttachResponse
{
    /// <summary>
    /// Array of successfully packaged attachments.
    /// Each package contains either a download URL or base64 content
    /// depending on the requested delivery mode.
    /// </summary>
    public required AttachmentPackage[] Attachments { get; init; }

    /// <summary>
    /// Array of errors for documents that could not be packaged.
    /// Partial success is allowed - some documents may succeed while others fail.
    /// </summary>
    public AttachmentError[]? Errors { get; init; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Total size in bytes of all successfully packaged attachments.
    /// </summary>
    public long TotalSize { get; init; }

    /// <summary>
    /// Indicates whether all requested documents were successfully packaged.
    /// False if any document failed (check Errors array for details).
    /// </summary>
    public bool AllSucceeded => Errors == null || Errors.Length == 0;
}

/// <summary>
/// Represents a packaged attachment ready for Outlook compose.
/// </summary>
/// <remarks>
/// <para>
/// Per spec: Attachments are delivered via URL (primary) or base64 (fallback).
/// The URL method uses pre-signed URLs with short TTL for security.
/// </para>
/// </remarks>
public record AttachmentPackage
{
    /// <summary>
    /// Document ID (sprk_document.sprk_documentid).
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Original filename with extension.
    /// </summary>
    /// <example>Contract.docx</example>
    public required string FileName { get; init; }

    /// <summary>
    /// MIME content type of the file.
    /// </summary>
    /// <example>application/vnd.openxmlformats-officedocument.wordprocessingml.document</example>
    public required string ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    /// <example>245678</example>
    public required long Size { get; init; }

    /// <summary>
    /// Pre-signed download URL with short TTL (primary delivery method).
    /// Use this with Office.context.mailbox.item.addFileAttachmentAsync().
    /// </summary>
    /// <remarks>
    /// URL expires after 5 minutes. Contains cryptographic token bound to requesting user.
    /// </remarks>
    /// <example>https://api.spaarke.com/office/share/attach/token123456</example>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// When the download URL expires.
    /// </summary>
    public required DateTimeOffset UrlExpiry { get; init; }

    /// <summary>
    /// Base64-encoded content (fallback for small files < 1MB if URL fails).
    /// Only populated if URL-based attachment fails and file is small enough.
    /// </summary>
    public string? ContentBase64 { get; init; }
}

/// <summary>
/// Represents an error for a document that could not be packaged.
/// </summary>
public record AttachmentError
{
    /// <summary>
    /// The document ID that failed.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Error code following the OFFICE_XXX convention.
    /// </summary>
    /// <remarks>
    /// Common error codes:
    /// <list type="bullet">
    /// <item><term>OFFICE_004</term><description>Attachment too large (single file > 25MB)</description></item>
    /// <item><term>OFFICE_007</term><description>Document not found</description></item>
    /// <item><term>OFFICE_009</term><description>Access denied (user lacks share permission)</description></item>
    /// <item><term>OFFICE_012</term><description>SPE retrieval failed</description></item>
    /// </list>
    /// </remarks>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }
}
