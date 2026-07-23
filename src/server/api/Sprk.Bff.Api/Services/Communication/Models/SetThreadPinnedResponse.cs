namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for PATCH /api/communications/threads/{threadId}/pin (task 041 / FR-24). Echoes the thread id
/// and the PERSISTED pinned state, which the client renders in place of its optimistic value (and rolls back to
/// on a non-2xx response).
/// </summary>
public sealed record SetThreadPinnedResponse
{
    /// <summary>The pinned/unpinned thread's <c>sprk_communicationthreadid</c>.</summary>
    public required Guid ThreadId { get; init; }

    /// <summary>The persisted <c>sprk_ispinned</c> value.</summary>
    public required bool IsPinned { get; init; }
}
