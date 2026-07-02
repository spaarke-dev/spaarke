namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Response payload for the Document Profile linear consumer. Not directly
/// returned to the client today (SSE is the client contract) — used internally
/// so <see cref="DocumentProfileService"/> can log / test / return structured
/// results independent of the SSE emission.
/// </summary>
public sealed record DocumentProfileResult
{
    public required Guid DocumentId { get; init; }
    public required IReadOnlyDictionary<string, object?> UpdatedFields { get; init; }
    public required bool RagIndexingEnqueued { get; init; }
    public string? RagIndexingSkipReason { get; init; }
}
