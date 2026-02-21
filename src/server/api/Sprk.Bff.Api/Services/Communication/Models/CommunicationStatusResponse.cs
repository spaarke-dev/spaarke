namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for GET /api/communications/{id}/status.
/// </summary>
public sealed record CommunicationStatusResponse
{
    public required Guid CommunicationId { get; init; }
    public required CommunicationStatus Status { get; init; }
    public string? GraphMessageId { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public string? From { get; init; }
}
