namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for POST /api/communications/threads/direct (task 043 / FR-09).
/// </summary>
public sealed record StartDirectThreadResponse
{
    /// <summary>The Direct thread's <c>sprk_communicationthreadid</c> — new or reused.</summary>
    public required Guid ThreadId { get; init; }

    /// <summary>The caller's <c>systemuserid</c> (echoed for client convenience).</summary>
    public required Guid CallerSystemUserId { get; init; }

    /// <summary>The other participant's <c>systemuserid</c> (echoed for client convenience).</summary>
    public required Guid OtherParticipantSystemUserId { get; init; }
}
