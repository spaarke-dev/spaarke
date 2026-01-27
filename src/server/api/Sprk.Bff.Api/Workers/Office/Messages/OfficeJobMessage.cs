using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Workers.Office.Messages;

/// <summary>
/// Base message for Office job processing.
/// Follows ADR-004 job contract schema.
/// </summary>
public record OfficeJobMessage
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public required Guid JobId { get; init; }

    /// <summary>
    /// Type of job (UploadFinalization, Profile, Indexing, DeepAnalysis).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required OfficeJobType JobType { get; init; }

    /// <summary>
    /// Subject ID - typically the document or email artifact ID.
    /// </summary>
    public string? SubjectId { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Idempotency key for duplicate detection.
    /// Format: SHA256 hash of canonical payload.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Current attempt number (1-based).
    /// </summary>
    public int Attempt { get; init; } = 1;

    /// <summary>
    /// Maximum retry attempts.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// User ID who initiated the job.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Job-specific payload.
    /// </summary>
    public JsonElement Payload { get; init; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Payload for upload finalization jobs.
/// </summary>
public record UploadFinalizationPayload
{
    /// <summary>
    /// Type of content being uploaded.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SaveContentType ContentType { get; init; }

    /// <summary>
    /// Target association type (Matter, Project, Invoice, Account, Contact).
    /// Null if saving document without association.
    /// </summary>
    public string? AssociationType { get; init; }

    /// <summary>
    /// Target association entity ID.
    /// Null if saving document without association.
    /// </summary>
    public Guid? AssociationId { get; init; }

    /// <summary>
    /// Target container ID for SPE storage.
    /// </summary>
    public required string ContainerId { get; init; }

    /// <summary>
    /// Target folder path within the container.
    /// </summary>
    public string? FolderPath { get; init; }

    /// <summary>
    /// Temporary file location (blob storage URL or local path).
    /// </summary>
    public required string TempFileLocation { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// MIME content type of the file.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Email metadata (for email saves).
    /// </summary>
    public EmailArtifactPayload? EmailMetadata { get; init; }

    /// <summary>
    /// Attachment metadata (for attachment saves).
    /// </summary>
    public AttachmentArtifactPayload? AttachmentMetadata { get; init; }

    /// <summary>
    /// Whether to trigger AI processing after upload.
    /// </summary>
    public bool TriggerAiProcessing { get; init; } = true;

    /// <summary>
    /// AI processing options.
    /// </summary>
    public AiProcessingOptions? AiOptions { get; init; }

    /// <summary>
    /// Document ID from Dataverse (sprk_document).
    /// When the file is uploaded directly to SPE by SaveAsync, this contains
    /// the Document record ID that was created. The worker should use this
    /// instead of creating a new Document record.
    /// </summary>
    public Guid? DocumentId { get; init; }
}

/// <summary>
/// Email artifact payload for creating EmailArtifact record.
/// </summary>
public record EmailArtifactPayload
{
    /// <summary>
    /// Outlook message ID.
    /// </summary>
    public string? OutlookMessageId { get; init; }

    /// <summary>
    /// RFC 2822 Internet message ID.
    /// </summary>
    public string? InternetMessageId { get; init; }

    /// <summary>
    /// Conversation ID for threading.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Email subject.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    public required string SenderEmail { get; init; }

    /// <summary>
    /// Sender display name.
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Recipients as JSON (to, cc, bcc).
    /// </summary>
    public string? RecipientsJson { get; init; }

    /// <summary>
    /// When the email was sent.
    /// </summary>
    public DateTimeOffset? SentDate { get; init; }

    /// <summary>
    /// When the email was received.
    /// </summary>
    public DateTimeOffset? ReceivedDate { get; init; }

    /// <summary>
    /// Body preview (first 500 chars).
    /// </summary>
    public string? BodyPreview { get; init; }

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// Email importance level.
    /// </summary>
    public int Importance { get; init; } = 1; // Normal
}

/// <summary>
/// Attachment artifact payload for creating AttachmentArtifact record.
/// </summary>
public record AttachmentArtifactPayload
{
    /// <summary>
    /// Outlook attachment ID.
    /// </summary>
    public required string OutlookAttachmentId { get; init; }

    /// <summary>
    /// Parent email artifact ID (if from an email).
    /// </summary>
    public Guid? EmailArtifactId { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public required string OriginalFileName { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Whether this is an inline attachment.
    /// </summary>
    public bool IsInline { get; init; }
}

/// <summary>
/// AI processing options.
/// </summary>
public record AiProcessingOptions
{
    /// <summary>
    /// Whether to generate profile summary.
    /// </summary>
    public bool ProfileSummary { get; init; } = true;

    /// <summary>
    /// Whether to index for RAG.
    /// </summary>
    public bool RagIndex { get; init; } = true;

    /// <summary>
    /// Whether to perform deep analysis.
    /// </summary>
    public bool DeepAnalysis { get; init; }
}
