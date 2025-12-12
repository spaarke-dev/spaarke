using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Structured metadata extracted from email files (.eml, .msg).
/// These fields map directly to Dataverse sprk_Document columns.
/// </summary>
public class EmailMetadata
{
    /// <summary>
    /// Email subject line.
    /// Maps to: sprk_EmailSubject
    /// </summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>
    /// Sender email address(es).
    /// Maps to: sprk_EmailFrom
    /// </summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>
    /// Primary recipient email address(es), comma-separated.
    /// Maps to: sprk_EmailTo
    /// </summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>
    /// CC recipient email address(es), comma-separated.
    /// Not stored in separate field; included in To or kept for reference.
    /// </summary>
    [JsonPropertyName("cc")]
    public string? Cc { get; set; }

    /// <summary>
    /// Email sent date/time.
    /// Maps to: sprk_EmailDate
    /// </summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>
    /// Email body content (plain text, truncated if large).
    /// Maps to: sprk_EmailBody
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    /// List of attachments found in the email.
    /// Maps to: sprk_Attachments (as JSON)
    /// </summary>
    [JsonPropertyName("attachments")]
    public List<EmailAttachment> Attachments { get; set; } = [];

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    [JsonPropertyName("hasAttachments")]
    public bool HasAttachments => Attachments.Count > 0;
}

/// <summary>
/// Metadata about an email attachment.
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Original filename of the attachment.
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the attachment (e.g., "application/pdf").
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Size of the attachment in bytes.
    /// </summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// Content ID for inline attachments (images in HTML body).
    /// </summary>
    [JsonPropertyName("contentId")]
    public string? ContentId { get; set; }

    /// <summary>
    /// Whether this attachment is inline (embedded in HTML body).
    /// </summary>
    [JsonPropertyName("isInline")]
    public bool IsInline { get; set; }

    /// <summary>
    /// If the attachment has been extracted to SPE, the Graph item ID.
    /// Null if not yet extracted.
    /// </summary>
    [JsonPropertyName("graphItemId")]
    public string? GraphItemId { get; set; }

    /// <summary>
    /// If the attachment has been saved as a Dataverse Document, the record ID.
    /// Null if not yet saved.
    /// </summary>
    [JsonPropertyName("documentId")]
    public Guid? DocumentId { get; set; }

    /// <summary>
    /// Whether this attachment has been extracted to a separate file/document.
    /// </summary>
    [JsonPropertyName("extracted")]
    public bool Extracted { get; set; }
}
