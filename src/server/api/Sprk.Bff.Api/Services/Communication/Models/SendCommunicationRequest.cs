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
    /// Determines how the email is sent.
    /// SharedMailbox (default): sends via app-only auth through the approved shared mailbox.
    /// User: sends as the authenticated user via OBO (On-Behalf-Of) flow.
    /// </summary>
    public SendMode SendMode { get; init; } = SendMode.SharedMailbox;

    /// <summary>
    /// Whether to archive the sent email as .eml in SharePoint Embedded.
    /// Default false.
    /// </summary>
    public bool ArchiveToSpe { get; init; } = false;

    /// <summary>
    /// Optional array of Dataverse <c>sprk_document</c> record GUIDs to attach.
    /// These are DMS <b>Document record</b> IDs — NOT raw SPE driveItem/File IDs. The server
    /// resolves each Document to its associated SPE File via the record's
    /// <c>sprk_graphdriveid</c> + <c>sprk_graphitemid</c>, downloads it, and includes it as a
    /// base64 <c>FileAttachment</c> in the Graph sendMail payload. Max 150 attachments, 35 MB total.
    /// Email attachments are always tracked Documents in Spaarke's DMS (see ADR-045); the field
    /// name is accurate — do not rename to "DriveItemIds".
    /// </summary>
    public string[]? AttachmentDocumentIds { get; init; }

    /// <summary>
    /// For reply/forward sends: the parent message's RFC-2822 <c>Internet-Message-Id</c>.
    /// When provided, it is stamped onto <c>sprk_communication.sprk_inreplyto</c> to preserve
    /// reply-thread continuity (feeds the W1 thread-continuity association rung). Optional.
    /// </summary>
    public string? InReplyToMessageId { get; init; }
}
