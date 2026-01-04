namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Converts Dataverse Email activities to RFC 5322 compliant .eml files.
/// Used by both manual "Save as Document" and automatic email processing.
/// </summary>
public interface IEmailToEmlConverter
{
    /// <summary>
    /// Convert a Dataverse Email activity to an RFC 5322 compliant .eml file stream.
    /// </summary>
    /// <param name="emailActivityId">The Dataverse email activity ID (activityid).</param>
    /// <param name="includeAttachments">Whether to embed attachments in the .eml file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the .eml stream, metadata, and any attachment info.</returns>
    Task<EmlConversionResult> ConvertToEmlAsync(
        Guid emailActivityId,
        bool includeAttachments = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a sanitized filename for the .eml file based on email subject and date.
    /// Format: YYYY-MM-DD_Subject.eml (max 100 chars, special chars removed)
    /// </summary>
    /// <param name="emailActivityId">The Dataverse email activity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sanitized filename for the .eml file.</returns>
    Task<string> GenerateEmlFileNameAsync(
        Guid emailActivityId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of converting a Dataverse Email to .eml format.
/// </summary>
public class EmlConversionResult
{
    /// <summary>
    /// Whether the conversion was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The generated .eml file stream (RFC 5322 format).
    /// Caller is responsible for disposing.
    /// </summary>
    public Stream? EmlStream { get; init; }

    /// <summary>
    /// Metadata extracted from the email.
    /// </summary>
    public EmailActivityMetadata? Metadata { get; init; }

    /// <summary>
    /// List of attachments with their content for separate document creation.
    /// Only populated if includeAttachments was true.
    /// </summary>
    public IReadOnlyList<EmailAttachmentInfo> Attachments { get; init; } = [];

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Size of the generated .eml file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    public static EmlConversionResult Succeeded(
        Stream emlStream,
        EmailActivityMetadata metadata,
        IReadOnlyList<EmailAttachmentInfo> attachments,
        long fileSizeBytes) => new()
        {
            Success = true,
            EmlStream = emlStream,
            Metadata = metadata,
            Attachments = attachments,
            FileSizeBytes = fileSizeBytes
        };

    public static EmlConversionResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Metadata extracted from a Dataverse Email activity.
/// Maps to sprk_document email fields.
/// </summary>
public class EmailActivityMetadata
{
    /// <summary>
    /// The Dataverse email activity ID.
    /// </summary>
    public Guid ActivityId { get; init; }

    /// <summary>
    /// Email subject line.
    /// Maps to: sprk_EmailSubject
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Sender email address.
    /// Maps to: sprk_EmailFrom
    /// </summary>
    public string From { get; init; } = string.Empty;

    /// <summary>
    /// Recipients (To field).
    /// Maps to: sprk_EmailTo
    /// </summary>
    public string To { get; init; } = string.Empty;

    /// <summary>
    /// CC recipients.
    /// </summary>
    public string? Cc { get; init; }

    /// <summary>
    /// Email body (HTML or plain text, truncated for storage).
    /// Maps to: sprk_EmailBody
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// RFC 5322 Message-ID header.
    /// Maps to: sprk_EmailMessageId
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Email direction: Received (100000000) or Sent (100000001).
    /// Maps to: sprk_EmailDirection
    /// </summary>
    public int Direction { get; init; }

    /// <summary>
    /// Email date (sent or received).
    /// Maps to: sprk_EmailDate
    /// </summary>
    public DateTime? EmailDate { get; init; }

    /// <summary>
    /// Tracking token for association matching.
    /// Maps to: sprk_EmailTrackingToken
    /// </summary>
    public string? TrackingToken { get; init; }

    /// <summary>
    /// Conversation index for threading.
    /// Maps to: sprk_EmailConversationIndex
    /// </summary>
    public string? ConversationIndex { get; init; }

    /// <summary>
    /// The regarding object ID (for association).
    /// </summary>
    public Guid? RegardingObjectId { get; init; }

    /// <summary>
    /// The regarding object type (e.g., "sprk_matter").
    /// </summary>
    public string? RegardingObjectType { get; init; }
}

/// <summary>
/// Information about an email attachment for separate document creation.
/// </summary>
public class EmailAttachmentInfo
{
    /// <summary>
    /// The activitymimeattachment ID.
    /// </summary>
    public Guid AttachmentId { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// MIME type of the attachment.
    /// </summary>
    public string MimeType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Attachment content as a stream.
    /// Caller is responsible for disposing.
    /// </summary>
    public Stream? Content { get; init; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Whether this attachment should be processed as a separate document.
    /// False for signature images, spacers, etc.
    /// </summary>
    public bool ShouldCreateDocument { get; init; } = true;

    /// <summary>
    /// Reason why the attachment won't be processed (if ShouldCreateDocument is false).
    /// </summary>
    public string? SkipReason { get; init; }
}
