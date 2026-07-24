namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for POST /api/communications/threads (R3 UAT 2026-07-23 item 9).
/// </summary>
public sealed record CreateRecordThreadResponse
{
    /// <summary>The new record-anchored thread's <c>sprk_communicationthreadid</c>.</summary>
    public required Guid ThreadId { get; init; }
}
