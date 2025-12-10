namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Payload for ai-analyze background jobs.
/// Contains the information needed to analyze a document.
/// </summary>
public record DocumentAnalysisJobPayload(
    Guid DocumentId,
    string DriveId,
    string ItemId)
{
    /// <summary>
    /// Creates a DocumentAnalysisRequest from this payload.
    /// </summary>
    public DocumentAnalysisRequest ToRequest() => new(DocumentId, DriveId, ItemId);
}
