namespace Spaarke.Dataverse;

/// <summary>
/// Document CRUD operations and relationship queries.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IDocumentDataverseService
{
    Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default);
    Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default);
    Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default);
    Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default);

    // Email-to-Document operations
    Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default);

    // Relationship query operations (visualization)
    Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default);
}
