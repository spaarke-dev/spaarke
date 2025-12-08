namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request to summarize a document stored in SharePoint Embedded.
/// </summary>
/// <param name="DocumentId">The Dataverse document record ID.</param>
/// <param name="DriveId">The SPE drive ID containing the file.</param>
/// <param name="ItemId">The SPE item ID of the file.</param>
public record SummarizeRequest(
    Guid DocumentId,
    string DriveId,
    string ItemId);
