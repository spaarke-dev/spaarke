namespace Sprk.Bff.Api.Models;

/// <summary>
/// EmailArtifact entity model (sprk_emailartifact).
/// Stores email metadata and body snapshots for emails saved from Outlook.
/// Created by: SDAP Office Integration project (tasks 010-012)
/// </summary>
public class EmailArtifact
{
    /// <summary>Email Artifact ID (sprk_emailartifactid - primary key)</summary>
    public Guid Id { get; set; }

    /// <summary>Email artifact name (sprk_name - primary name field, auto-generated from Subject + Date)</summary>
    public required string Name { get; set; }

    /// <summary>Email subject line (sprk_subject - up to 400 chars, searchable)</summary>
    public string? Subject { get; set; }

    /// <summary>Email sender address (sprk_sender - up to 320 chars, searchable)</summary>
    public string? Sender { get; set; }

    /// <summary>JSON array of recipient objects (sprk_recipients - up to 10K chars)</summary>
    public string? Recipients { get; set; }

    /// <summary>JSON array of CC recipient objects (sprk_ccrecipients - up to 10K chars)</summary>
    public string? CcRecipients { get; set; }

    /// <summary>When email was sent (sprk_sentdate)</summary>
    public DateTime? SentDate { get; set; }

    /// <summary>When email was received (sprk_receiveddate)</summary>
    public DateTime? ReceivedDate { get; set; }

    /// <summary>Internet message ID from headers (sprk_messageid - up to 256 chars, indexed)</summary>
    public string? MessageId { get; set; }

    /// <summary>SHA256 hash for duplicate detection (sprk_internetheadershash - 64 chars, indexed)</summary>
    public string? InternetHeadersHash { get; set; }

    /// <summary>Email conversation/thread ID (sprk_conversationid - up to 256 chars)</summary>
    public string? ConversationId { get; set; }

    /// <summary>Email importance choice value (sprk_importance).
    /// Values: Low=0, Normal=1, High=2
    /// </summary>
    public int? Importance { get; set; }

    /// <summary>Whether email has attachments (sprk_hasattachments)</summary>
    public bool? HasAttachments { get; set; }

    /// <summary>First 2000 chars of email body (sprk_bodypreview - searchable)</summary>
    public string? BodyPreview { get; set; }

    /// <summary>Document ID (sprk_document - lookup to sprk_document).
    /// Maps to _sprk_document_value in Dataverse Web API.
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Created date/time (system field)</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Modified date/time (system field)</summary>
    public DateTime ModifiedOn { get; set; }

    /// <summary>Owner ID (system field - _ownerid_value)</summary>
    public Guid? OwnerId { get; set; }
}

/// <summary>
/// Request model for creating a new EmailArtifact
/// </summary>
public class CreateEmailArtifactRequest
{
    public required string Name { get; set; }
    public string? Subject { get; set; }
    public string? Sender { get; set; }
    public string? Recipients { get; set; }
    public string? CcRecipients { get; set; }
    public DateTime? SentDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string? MessageId { get; set; }
    public string? InternetHeadersHash { get; set; }
    public string? ConversationId { get; set; }
    public int? Importance { get; set; }
    public bool? HasAttachments { get; set; }
    public string? BodyPreview { get; set; }
    public Guid? DocumentId { get; set; }
}

/// <summary>
/// Request model for updating an existing EmailArtifact
/// </summary>
public class UpdateEmailArtifactRequest
{
    public string? Subject { get; set; }
    public string? Sender { get; set; }
    public string? Recipients { get; set; }
    public string? CcRecipients { get; set; }
    public DateTime? SentDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string? MessageId { get; set; }
    public string? InternetHeadersHash { get; set; }
    public string? ConversationId { get; set; }
    public int? Importance { get; set; }
    public bool? HasAttachments { get; set; }
    public string? BodyPreview { get; set; }
    public Guid? DocumentId { get; set; }
}

/// <summary>
/// Email importance enumeration (matches sprk_importance choice values)
/// </summary>
public enum EmailImportance
{
    Low = 0,
    Normal = 1,
    High = 2
}
