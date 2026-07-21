namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for POST /api/communications/threads/{threadId}/rename (task 004 / FR-17). Echoes the
/// thread id and the PERSISTED name (trimmed + truncated), which the client renders in place of the
/// previous label. After this write the thread's <c>sprk_nameisautoderived</c> marker is Edited, so the
/// auto re-derive (<c>ReDeriveThreadNameAsync</c>) never overwrites it (edit-preserve).
/// </summary>
public sealed record RenameThreadResponse
{
    /// <summary>The renamed thread's <c>sprk_communicationthreadid</c>.</summary>
    public required Guid ThreadId { get; init; }

    /// <summary>The persisted (trimmed + truncated) <c>sprk_name</c>.</summary>
    public required string Name { get; init; }
}
