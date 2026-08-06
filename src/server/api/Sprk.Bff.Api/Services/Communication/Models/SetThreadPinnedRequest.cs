namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for PATCH /api/communications/threads/{threadId}/pin (task 041 / FR-24) — set or clear the
/// pinned marker on a communication thread. Pin only — no archive/mute/tag equivalent exists on this endpoint.
/// </summary>
public sealed record SetThreadPinnedRequest
{
    /// <summary>The desired pinned state. Required (no default — a missing/null body is a 400).</summary>
    public required bool Pinned { get; init; }
}
