namespace Spaarke.Dataverse;

/// <summary>
/// Interface for Dataverse service operations
/// </summary>
public interface IDataverseService
{
    // Document operations
    Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default);

    // Analysis operations
    Task<AnalysisEntity?> GetAnalysisAsync(string id, CancellationToken ct = default);
    Task<AnalysisActionEntity?> GetAnalysisActionAsync(string id, CancellationToken ct = default);
    Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default);
    Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default);

    // Health checks
    Task<bool> TestConnectionAsync();
    Task<bool> TestDocumentOperationsAsync();

    // Metadata operations (Phase 7)

    /// <summary>
    /// Get the EntitySetName (plural collection name) for an entity logical name.
    /// Example: "sprk_matter" → "sprk_matters"
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_matter")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Entity set name (e.g., "sprk_matters")</returns>
    Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default);

    /// <summary>
    /// Get lookup navigation property metadata for a child → parent relationship.
    /// This is the property name used in @odata.bind (case-sensitive!).
    /// Example: sprk_document → sprk_matter returns "sprk_Matter" (capital M)
    /// </summary>
    /// <param name="childEntityLogicalName">Child entity (e.g., "sprk_document")</param>
    /// <param name="relationshipSchemaName">Relationship schema name (e.g., "sprk_matter_document")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Lookup metadata with navigation property name</returns>
    Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);

    /// <summary>
    /// Get collection navigation property for a parent → child relationship.
    /// This is used for relationship URL creation (Option B).
    /// Example: sprk_matter → sprk_document returns "sprk_matter_document"
    /// </summary>
    /// <param name="parentEntityLogicalName">Parent entity (e.g., "sprk_matter")</param>
    /// <param name="relationshipSchemaName">Relationship schema name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection navigation property name</returns>
    Task<string> GetCollectionNavigationAsync(
        string parentEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);
}
