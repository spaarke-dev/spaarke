namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Abstracts Dataverse access for document storage pointers.
/// Isolates mapping logic from business services per senior dev spec (FILE-VIEWER-V5-FIX.md).
/// Resolves Document GUID (Dataverse primary key) to SharePoint Embedded storage identifiers.
/// </summary>
public interface IDocumentStorageResolver
{
    /// <summary>
    /// Resolve Document GUID to SharePoint Embedded storage pointers.
    /// </summary>
    /// <param name="documentId">Dataverse Document primary key (GUID)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (DriveId, ItemId) for Graph API calls</returns>
    /// <exception cref="Infrastructure.Exceptions.SdapProblemException">
    /// Stable error codes:
    /// - document_not_found (404): Document row doesn't exist in Dataverse
    /// - mapping_missing_drive (409): DriveId not populated or invalid format
    /// - mapping_missing_item (409): ItemId not populated or invalid format
    /// </exception>
    Task<(string DriveId, string ItemId)> GetSpePointersAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
