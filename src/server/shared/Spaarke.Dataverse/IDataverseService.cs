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
    Task<Guid> CreateAnalysisAsync(Guid documentId, string? name = null, Guid? playbookId = null, CancellationToken ct = default);
    Task<Guid> CreateAnalysisOutputAsync(AnalysisOutputEntity output, CancellationToken ct = default);
    Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default);
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

    // ========================================
    // Email-to-Document Operations (Phase 4)
    // ========================================

    /// <summary>
    /// Get the main .eml document record by email activity lookup.
    /// Returns the document where sprk_Email lookup equals the email activity ID
    /// and sprk_isemailarchive is true.
    /// </summary>
    /// <param name="emailId">The Dataverse email activity ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The main .eml document entity, or null if not found</returns>
    Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default);

    /// <summary>
    /// Get child documents (attachments) by parent document lookup.
    /// Returns documents where sprk_ParentDocument lookup equals the parent document ID.
    /// </summary>
    /// <param name="parentDocumentId">The parent document ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of child document entities</returns>
    Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default);

    // ========================================
    // Relationship Query Operations (Visualization)
    // ========================================

    /// <summary>
    /// Get documents associated with the same Matter.
    /// Returns documents where sprk_Matter lookup equals the given Matter ID.
    /// </summary>
    /// <param name="matterId">The Matter ID</param>
    /// <param name="excludeDocumentId">Optional document ID to exclude from results</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of document entities linked to this Matter</returns>
    Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default);

    /// <summary>
    /// Get documents associated with the same Project.
    /// Returns documents where sprk_Project lookup equals the given Project ID.
    /// </summary>
    /// <param name="projectId">The Project ID</param>
    /// <param name="excludeDocumentId">Optional document ID to exclude from results</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of document entities linked to this Project</returns>
    Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default);

    /// <summary>
    /// Get documents associated with the same Invoice.
    /// Returns documents where sprk_Invoice lookup equals the given Invoice ID.
    /// </summary>
    /// <param name="invoiceId">The Invoice ID</param>
    /// <param name="excludeDocumentId">Optional document ID to exclude from results</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of document entities linked to this Invoice</returns>
    Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default);

    /// <summary>
    /// Get documents in the same email thread (same ConversationIndex prefix).
    /// Uses startswith filter on sprk_EmailConversationIndex.
    /// </summary>
    /// <param name="conversationIndexPrefix">First 44 chars of ConversationIndex (thread root)</param>
    /// <param name="excludeDocumentId">Optional document ID to exclude from results</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of document entities in this email thread</returns>
    Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default);

    // ========================================
    // Office Add-in Operations (SDAP Project)
    // ========================================

    /// <summary>
    /// Create a new ProcessingJob record for tracking async operations.
    /// </summary>
    Task<Guid> CreateProcessingJobAsync(object request, CancellationToken ct = default);

    /// <summary>
    /// Update an existing ProcessingJob with new status/progress.
    /// </summary>
    Task UpdateProcessingJobAsync(Guid id, object request, CancellationToken ct = default);

    /// <summary>
    /// Get a ProcessingJob by ID.
    /// </summary>
    Task<object?> GetProcessingJobAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get a ProcessingJob by idempotency key (for duplicate detection).
    /// </summary>
    Task<object?> GetProcessingJobByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Create a new EmailArtifact record for tracking saved email metadata.
    /// </summary>
    Task<Guid> CreateEmailArtifactAsync(object request, CancellationToken ct = default);

    /// <summary>
    /// Get an EmailArtifact by ID.
    /// </summary>
    Task<object?> GetEmailArtifactAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create a new AttachmentArtifact record for tracking saved email attachments.
    /// </summary>
    Task<Guid> CreateAttachmentArtifactAsync(object request, CancellationToken ct = default);

    /// <summary>
    /// Get an AttachmentArtifact by ID.
    /// </summary>
    Task<object?> GetAttachmentArtifactAsync(Guid id, CancellationToken ct = default);
}
