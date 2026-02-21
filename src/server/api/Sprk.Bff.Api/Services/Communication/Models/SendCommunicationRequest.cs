using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for POST /api/communications/send.
/// </summary>
public sealed record SendCommunicationRequest
{
    /// <summary>
    /// Recipient email addresses (required, at least one).
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one recipient is required.")]
    public required string[] To { get; init; }

    /// <summary>
    /// CC recipient email addresses (optional).
    /// </summary>
    public string[]? Cc { get; init; }

    /// <summary>
    /// BCC recipient email addresses (optional).
    /// </summary>
    public string[]? Bcc { get; init; }

    /// <summary>
    /// Email subject line (required).
    /// </summary>
    [Required]
    public required string Subject { get; init; }

    /// <summary>
    /// Email body content (required).
    /// </summary>
    [Required]
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
    /// Entity associations for this communication (optional).
    /// Links the communication record to Dataverse entities (Matter, Project, Organization, etc.).
    /// </summary>
    public CommunicationAssociation[]? Associations { get; init; }

    /// <summary>
    /// Caller-provided correlation ID for tracing (optional).
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Whether to archive the sent email as .eml in SharePoint Embedded.
    /// Default false.
    /// </summary>
    public bool ArchiveToSpe { get; init; } = false;

    /// <summary>
    /// Optional array of SPE document IDs (driveItem IDs) to attach to the email.
    /// Files are downloaded from SPE via SpeFileStore and included as base64-encoded FileAttachments
    /// in the Graph sendMail payload. Max 150 attachments, 35MB total size.
    /// </summary>
    public string[]? AttachmentDocumentIds { get; init; }
}
