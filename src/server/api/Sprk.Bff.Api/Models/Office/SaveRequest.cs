using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for saving emails, attachments, or documents from Office add-ins.
/// Corresponds to POST /office/save endpoint.
/// </summary>
public record SaveRequest
{
    /// <summary>
    /// Type of content being saved.
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SaveContentType ContentType { get; init; }

    /// <summary>
    /// Target Dataverse entity to associate with (e.g., account, contact, matter).
    /// </summary>
    public SaveEntityReference? TargetEntity { get; init; }

    // ContainerId DELETED 2026-08-30 by task 085. It was the container-selection defect in its purest
    // form: the route authorizes the caller against TargetEntity (via AddEntityAccessFilter) and then
    // wrote the bytes into ContainerId — a DIFFERENT client-supplied field on this same body. The
    // authorization key and the write destination were two independently caller-chosen values for one
    // decision, on an app-only MI write with no SPE ACL to catch a mismatch. That is option (B), which
    // task 083 explicitly rejected.
    //
    // Two facts made it worse than it looked. TargetEntity is OPTIONAL, and EntityAccessFilter calls
    // next() when the target is absent — so a save could run on baseline Office authentication alone
    // and still name its own container. And the value did not stop at the request: it was serialized
    // into the ProcessingJob payload, so a client-chosen container outlived the call.
    //
    // No shipped client ever sent it (the add-in request body is contentType/email/targetEntity/
    // aiOptions/documentMetadata), so the hole was the CONTRACT, not the traffic. Do not reintroduce
    // it: the server now derives the container from the authorized record. Returning the chosen
    // container/drive id in the RESPONSE is fine and necessary — the client needs driveId for
    // sprk_graphdriveid and indexFile(). ACCEPTING one is the vulnerability.

    // FolderPath DELETED 2026-08-28 (stop minting SPE folders on upload paths). It was client-supplied
    // and always null: no producer under src/client/** ever set it (zero hits for `folderPath` there),
    // and no server code constructed one. Its only effect, had a client ever sent it, would have been to
    // make Graph implicitly create the named folder inside the container as a side effect of the upload —
    // which is the defect this change removes. Do not reintroduce it; a deliberate folder is what the SPE
    // Admin "New Folder" action is for.

    /// <summary>
    /// Email-specific metadata (required when ContentType is Email).
    /// </summary>
    public EmailMetadata? Email { get; init; }

    /// <summary>
    /// Attachment-specific metadata (required when ContentType is Attachment).
    /// </summary>
    public AttachmentMetadata? Attachment { get; init; }

    /// <summary>
    /// Document-specific metadata (required when ContentType is Document).
    /// </summary>
    public DocumentMetadata? Document { get; init; }

    /// <summary>
    /// Idempotency key to prevent duplicate processing.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Whether to trigger AI processing after save.
    /// </summary>
    public bool TriggerAiProcessing { get; init; } = true;

    /// <summary>
    /// AI processing options (profile summary, RAG index, deep analysis).
    /// If null, defaults based on TriggerAiProcessing flag.
    /// </summary>
    public AiProcessingOptionsRequest? AiOptions { get; init; }
}

/// <summary>
/// Types of content that can be saved from Office add-ins.
/// </summary>
public enum SaveContentType
{
    /// <summary>
    /// Email message (Outlook).
    /// </summary>
    Email,

    /// <summary>
    /// Email attachment (Outlook).
    /// </summary>
    Attachment,

    /// <summary>
    /// Document (Word).
    /// </summary>
    Document
}

/// <summary>
/// Reference to a Dataverse entity for save association.
/// </summary>
public record SaveEntityReference
{
    /// <summary>
    /// Entity logical name (e.g., "account", "contact", "sprk_matter").
    /// </summary>
    [Required]
    public required string EntityType { get; init; }

    /// <summary>
    /// Entity record ID.
    /// </summary>
    [Required]
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Display name for UI.
    /// </summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// Metadata for saving an email.
/// </summary>
public record EmailMetadata
{
    /// <summary>
    /// Email subject line.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public required string Subject { get; init; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public required string SenderEmail { get; init; }

    /// <summary>
    /// Sender display name.
    /// </summary>
    [MaxLength(200)]
    public string? SenderName { get; init; }

    /// <summary>
    /// Recipients (To, Cc, Bcc).
    /// </summary>
    public List<Recipient>? Recipients { get; init; }

    /// <summary>
    /// When the email was received.
    /// </summary>
    public DateTimeOffset? ReceivedDate { get; init; }

    /// <summary>
    /// When the email was sent.
    /// </summary>
    public DateTimeOffset? SentDate { get; init; }

    /// <summary>
    /// Exchange conversation ID for threading.
    /// </summary>
    [MaxLength(200)]
    public string? ConversationId { get; init; }

    /// <summary>
    /// RFC 2822 Internet message ID.
    /// </summary>
    [MaxLength(998)]
    public string? InternetMessageId { get; init; }

    /// <summary>
    /// Email body content (HTML or text).
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Whether the body is HTML.
    /// </summary>
    public bool IsBodyHtml { get; init; }

    /// <summary>
    /// List of attachments to include.
    /// </summary>
    public List<AttachmentReference>? Attachments { get; init; }

    /// <summary>
    /// Selected attachment filenames to create as Documents.
    /// If null or empty, all attachments are created as Documents.
    /// Used to respect user's attachment selection in the add-in UI.
    /// </summary>
    public List<string>? SelectedAttachmentFileNames { get; init; }
}

/// <summary>
/// Email recipient information.
/// </summary>
public record Recipient
{
    /// <summary>
    /// Recipient type (To, Cc, Bcc).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RecipientType Type { get; init; }

    /// <summary>
    /// Email address.
    /// </summary>
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    public string? Name { get; init; }
}

/// <summary>
/// Recipient types.
/// </summary>
public enum RecipientType
{
    To,
    Cc,
    Bcc
}

/// <summary>
/// Reference to an attachment for inclusion.
/// </summary>
public record AttachmentReference
{
    /// <summary>
    /// Attachment ID from Office.js.
    /// </summary>
    [Required]
    public required string AttachmentId { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    [Required]
    public required string FileName { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Base64-encoded attachment content.
    /// Retrieved client-side via Office.js getAttachmentContentAsync().
    /// </summary>
    public string? ContentBase64 { get; init; }

    /// <summary>
    /// Whether this is an inline attachment (embedded in HTML body via cid: reference).
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// Content-ID for inline attachments (without angle brackets).
    /// Used for cid: references in HTML body.
    /// </summary>
    public string? ContentId { get; init; }
}

/// <summary>
/// Metadata for saving an attachment.
/// </summary>
public record AttachmentMetadata
{
    /// <summary>
    /// Attachment ID from Office.js.
    /// </summary>
    [Required]
    public required string AttachmentId { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string FileName { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    [MaxLength(100)]
    public string? ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// Base64-encoded content (for inline attachments).
    /// </summary>
    public string? ContentBase64 { get; init; }

    /// <summary>
    /// Parent email ID if saving from an email context.
    /// </summary>
    public Guid? ParentEmailArtifactId { get; init; }

    /// <summary>
    /// Parent email's internetMessageId (for fetching attachment content via Graph API).
    /// When provided, server will fetch the email via Graph API and extract this attachment's content.
    /// </summary>
    [MaxLength(998)]
    public string? ParentEmailId { get; init; }
}

/// <summary>
/// Metadata for saving a document (Word).
/// </summary>
public record DocumentMetadata
{
    /// <summary>
    /// Document filename.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string FileName { get; init; }

    /// <summary>
    /// Document title (can differ from filename).
    /// </summary>
    [MaxLength(500)]
    public string? Title { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    [MaxLength(100)]
    public string? ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// Base64-encoded document content.
    /// </summary>
    public string? ContentBase64 { get; init; }

    /// <summary>
    /// Whether this is a new version of an existing document.
    /// </summary>
    public bool IsNewVersion { get; init; }

    /// <summary>
    /// Existing document ID if updating.
    /// </summary>
    public Guid? ExistingDocumentId { get; init; }

    /// <summary>
    /// Version comment.
    /// </summary>
    [MaxLength(1000)]
    public string? VersionComment { get; init; }
}

/// <summary>
/// AI processing options for document processing.
/// </summary>
public record AiProcessingOptionsRequest
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
