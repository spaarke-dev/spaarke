namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for POST /api/communications/threads/{threadId}/rename (task 004 / FR-17) — set a
/// user-chosen name on a communication thread. A blank/whitespace <see cref="Name"/> is rejected (400);
/// the accepted name is trimmed and truncated to the <c>sprk_name</c> field max (200 chars) on write.
/// </summary>
public sealed record RenameThreadRequest
{
    /// <summary>The user-chosen thread name. Required; blank/whitespace is a 400.</summary>
    public string? Name { get; init; }
}
