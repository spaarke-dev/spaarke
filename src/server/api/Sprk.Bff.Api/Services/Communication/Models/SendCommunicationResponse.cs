namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for POST /api/communications/send.
/// </summary>
public sealed record SendCommunicationResponse
{
    /// <summary>
    /// Dataverse sprk_communication record ID (null if Dataverse tracking is not yet enabled — Phase 1).
    /// </summary>
    public Guid? CommunicationId { get; init; }

    /// <summary>
    /// Graph message ID returned by sendMail.
    /// </summary>
    public required string GraphMessageId { get; init; }

    /// <summary>
    /// Current status of the communication.
    /// </summary>
    public required CommunicationStatus Status { get; init; }

    /// <summary>
    /// Timestamp when the email was sent.
    /// </summary>
    public required DateTimeOffset SentAt { get; init; }

    /// <summary>
    /// Sender email address used.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Dataverse sprk_document record ID for the archived .eml file (null if archival not requested or failed).
    /// </summary>
    public Guid? ArchivedDocumentId { get; init; }

    /// <summary>
    /// Warning message if archival was requested but failed (email still sent successfully).
    /// </summary>
    public string? ArchivalWarning { get; init; }

    /// <summary>
    /// Number of file attachments included in the sent email.
    /// Zero if no attachments were provided.
    /// </summary>
    public int AttachmentCount { get; init; }

    /// <summary>
    /// Warning message if attachment record creation in Dataverse failed (email still sent successfully).
    /// Null when attachment records were created successfully or no attachments were provided.
    /// </summary>
    public string? AttachmentRecordWarning { get; init; }
}
