namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Service for processing email attachments and creating separate document records.
/// Filters signature images and small files, uploads to SPE, creates Dataverse records.
/// </summary>
public interface IEmailAttachmentProcessor
{
    /// <summary>
    /// Process attachments from an email and create separate document records.
    /// </summary>
    /// <param name="request">Request containing email details and attachment data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing created document records.</returns>
    Task<AttachmentProcessingResult> ProcessAttachmentsAsync(
        ProcessAttachmentsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file should be filtered out (signature image, too small, blocked extension).
    /// </summary>
    /// <param name="fileName">The attachment filename.</param>
    /// <param name="sizeBytes">The attachment size in bytes.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>True if the file should be skipped, false if it should be processed.</returns>
    bool ShouldFilterAttachment(string fileName, long sizeBytes, string? contentType);
}

/// <summary>
/// Request to process email attachments.
/// </summary>
public class ProcessAttachmentsRequest
{
    /// <summary>
    /// The email activity ID these attachments belong to.
    /// </summary>
    public Guid EmailId { get; init; }

    /// <summary>
    /// The parent email document ID (sprk_document).
    /// Attachment documents will reference this as their parent.
    /// </summary>
    public Guid ParentDocumentId { get; init; }

    /// <summary>
    /// SPE container ID for file uploads.
    /// </summary>
    public string ContainerId { get; init; } = string.Empty;

    /// <summary>
    /// Drive ID within the SPE container.
    /// </summary>
    public string DriveId { get; init; } = string.Empty;

    /// <summary>
    /// The attachments to process.
    /// </summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Whether to queue documents for AI processing after creation.
    /// </summary>
    public bool QueueForAiProcessing { get; init; } = true;

    /// <summary>
    /// Optional Matter/Account/Contact to associate attachments with.
    /// </summary>
    public Guid? AssociatedEntityId { get; init; }

    /// <summary>
    /// Entity type for association (e.g., "sprk_matter", "account").
    /// </summary>
    public string? AssociatedEntityType { get; init; }
}

/// <summary>
/// Represents a single email attachment.
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Original filename of the attachment.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// MIME content type (e.g., "application/pdf").
    /// </summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Size of the attachment in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// The attachment content stream.
    /// </summary>
    public Stream Content { get; init; } = Stream.Null;

    /// <summary>
    /// Whether this is an inline attachment (embedded in HTML body).
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// Content-ID for inline attachments.
    /// </summary>
    public string? ContentId { get; init; }
}

/// <summary>
/// Result of attachment processing.
/// </summary>
public class AttachmentProcessingResult
{
    /// <summary>
    /// Whether processing was successful overall.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Total number of attachments in the email.
    /// </summary>
    public int TotalAttachments { get; init; }

    /// <summary>
    /// Number of attachments that were filtered out.
    /// </summary>
    public int FilteredCount { get; init; }

    /// <summary>
    /// Number of attachments successfully processed.
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// Number of attachments that failed to process.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Created document records for successful attachments.
    /// </summary>
    public IReadOnlyList<AttachmentDocumentRecord> Documents { get; init; } = [];

    /// <summary>
    /// Details about filtered attachments (for logging/debugging).
    /// </summary>
    public IReadOnlyList<FilteredAttachmentInfo> FilteredAttachments { get; init; } = [];

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Create a success result.
    /// </summary>
    public static AttachmentProcessingResult Succeeded(
        int total,
        int filtered,
        IReadOnlyList<AttachmentDocumentRecord> documents,
        IReadOnlyList<FilteredAttachmentInfo> filteredAttachments) => new()
    {
        Success = true,
        TotalAttachments = total,
        FilteredCount = filtered,
        ProcessedCount = documents.Count,
        FailedCount = total - filtered - documents.Count,
        Documents = documents,
        FilteredAttachments = filteredAttachments
    };

    /// <summary>
    /// Create a failure result.
    /// </summary>
    public static AttachmentProcessingResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

/// <summary>
/// Information about a created attachment document.
/// </summary>
public class AttachmentDocumentRecord
{
    /// <summary>
    /// The created document ID (sprk_documentid).
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Original attachment filename.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Graph drive item ID for the uploaded file.
    /// </summary>
    public string? GraphItemId { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string ContentType { get; init; } = string.Empty;
}

/// <summary>
/// Information about a filtered (skipped) attachment.
/// </summary>
public class FilteredAttachmentInfo
{
    /// <summary>
    /// The attachment filename.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Reason why the attachment was filtered.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Size of the filtered attachment.
    /// </summary>
    public long SizeBytes { get; init; }
}
