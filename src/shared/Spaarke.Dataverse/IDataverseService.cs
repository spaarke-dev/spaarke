namespace Spaarke.Dataverse;

/// <summary>
/// Interface for Dataverse service operations
/// </summary>
public interface IDataverseService
{
    Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default);
    Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default);
    Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default);
    Task<bool> TestConnectionAsync();
    Task<bool> TestDocumentOperationsAsync();
}
