namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Extracts attachments from RFC 5322 compliant .eml file streams.
/// The single live consumer is the Office upload-finalization worker, which parses attachments
/// out of a .eml file already stored in SharePoint Embedded.
/// </summary>
public interface IEmailToEmlConverter
{
    /// <summary>
    /// Extract attachments from an existing .eml file stream.
    /// Uses MimeKit to parse the .eml and return attachment metadata with content streams.
    /// </summary>
    /// <param name="emlStream">The .eml file stream to parse.</param>
    /// <returns>List of attachments with their content streams and metadata.</returns>
    /// <remarks>
    /// This method does not require Dataverse access - it parses the .eml file directly.
    /// Use this when you need to process attachments from an already-converted .eml file.
    /// The returned streams are MemoryStreams that the caller must dispose.
    /// Maximum attachment size is 250MB (NFR-05).
    /// </remarks>
    IReadOnlyList<EmailAttachmentInfo> ExtractAttachments(Stream emlStream);
}

/// <summary>
/// Information about an email attachment for separate document creation.
/// </summary>
public class EmailAttachmentInfo
{
    /// <summary>
    /// The activitymimeattachment ID.
    /// For attachments extracted from .eml files, this will be Guid.Empty.
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
    /// Whether this is an inline attachment (embedded in HTML body via Content-ID reference).
    /// Inline attachments are typically images displayed within the email body.
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// Content-ID for inline attachments (without angle brackets).
    /// Used for matching cid: references in HTML body.
    /// </summary>
    public string? ContentId { get; init; }

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
