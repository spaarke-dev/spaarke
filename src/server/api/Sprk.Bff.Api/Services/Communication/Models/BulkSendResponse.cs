namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for POST /api/communications/send-bulk.
/// Contains aggregate counts and per-recipient results.
/// Returns 200 if all succeeded, 207 if partial success/failure.
/// </summary>
public sealed record BulkSendResponse
{
    /// <summary>
    /// Total number of recipients in the bulk request.
    /// </summary>
    public required int TotalRecipients { get; init; }

    /// <summary>
    /// Number of recipients whose emails were sent successfully.
    /// </summary>
    public required int Succeeded { get; init; }

    /// <summary>
    /// Number of recipients whose emails failed to send.
    /// </summary>
    public required int Failed { get; init; }

    /// <summary>
    /// Per-recipient results with individual status and error information.
    /// </summary>
    public required BulkSendResult[] Results { get; init; }
}

/// <summary>
/// Result for a single recipient in a bulk send operation.
/// </summary>
public sealed record BulkSendResult
{
    /// <summary>
    /// The email address of the recipient.
    /// </summary>
    public required string RecipientEmail { get; init; }

    /// <summary>
    /// Send status: "sent" on success, "failed" on error.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Dataverse sprk_communication record ID (null if record creation failed or send failed).
    /// </summary>
    public Guid? CommunicationId { get; init; }

    /// <summary>
    /// Error message if the send failed (null on success).
    /// </summary>
    public string? Error { get; init; }
}
