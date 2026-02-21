namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for POST /api/communications/send-bulk.
/// Sends the same base communication (subject, body, attachments) to multiple recipients,
/// each with their own sprk_communication record and optional per-recipient template data.
/// </summary>
public sealed record BulkSendRequest
{
    /// <summary>
    /// Email subject line (required). Shared across all recipients.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Email body content (required). Shared across all recipients.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Body content format. Defaults to HTML.
    /// </summary>
    public BodyFormat BodyFormat { get; init; } = BodyFormat.HTML;

    /// <summary>
    /// Sender mailbox address. If null, the default approved sender is used.
    /// Must match an entry in the approved senders list.
    /// </summary>
    public string? FromMailbox { get; init; }

    /// <summary>
    /// Communication type. Defaults to Email.
    /// </summary>
    public CommunicationType CommunicationType { get; init; } = CommunicationType.Email;

    /// <summary>
    /// Optional array of SPE document IDs (driveItem IDs) to attach to each email.
    /// Files are downloaded once from SPE and reused across all sends.
    /// Max 150 attachments, 35MB total size.
    /// </summary>
    public string[]? AttachmentDocumentIds { get; init; }

    /// <summary>
    /// Whether to archive each sent email as .eml in SharePoint Embedded.
    /// Default false.
    /// </summary>
    public bool ArchiveToSpe { get; init; } = false;

    /// <summary>
    /// Entity associations for this communication (optional).
    /// Links each communication record to Dataverse entities (Matter, Project, Organization, etc.).
    /// Shared across all recipients.
    /// </summary>
    public CommunicationAssociation[]? Associations { get; init; }

    /// <summary>
    /// Array of recipients to send to (required, 1-50 recipients).
    /// Each recipient gets their own email and sprk_communication record.
    /// </summary>
    public required BulkRecipient[] Recipients { get; init; }
}

/// <summary>
/// Represents a single recipient in a bulk send operation.
/// </summary>
public sealed record BulkRecipient
{
    /// <summary>
    /// Primary recipient email address (required).
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// CC recipient email addresses for this specific send (optional).
    /// </summary>
    public string[]? Cc { get; init; }

    /// <summary>
    /// Per-recipient template data for future template substitution (optional).
    /// Keys are placeholder names, values are replacement text.
    /// </summary>
    public Dictionary<string, string>? TemplateData { get; init; }
}
