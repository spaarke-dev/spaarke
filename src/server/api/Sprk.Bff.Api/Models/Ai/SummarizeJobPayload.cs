namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Payload for ai-summarize background jobs.
/// Contains the information needed to summarize a document.
/// </summary>
public record SummarizeJobPayload(
    Guid DocumentId,
    string DriveId,
    string ItemId)
{
    /// <summary>
    /// Creates a SummarizeRequest from this payload.
    /// </summary>
    public SummarizeRequest ToRequest() => new(DocumentId, DriveId, ItemId);
}
