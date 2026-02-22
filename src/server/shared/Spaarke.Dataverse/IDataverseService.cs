using Microsoft.Xrm.Sdk;

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

    /// <summary>
    /// Retrieve a Dataverse entity by ID with specified columns.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_invoice", "sprk_matter")</param>
    /// <param name="id">Record ID</param>
    /// <param name="columns">Array of column names to retrieve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Retrieved entity</returns>
    Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default);
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

    // ========================================
    // Event Management Operations (Events and Workflow Automation R1)
    // ========================================

    /// <summary>
    /// Query events with optional filtering and pagination.
    /// </summary>
    /// <param name="regardingRecordType">Filter by regarding record type (0-7)</param>
    /// <param name="regardingRecordId">Filter by specific regarding record ID</param>
    /// <param name="eventTypeId">Filter by event type ID</param>
    /// <param name="statusCode">Filter by status code</param>
    /// <param name="priority">Filter by priority (0-3)</param>
    /// <param name="dueDateFrom">Filter events with due date on or after this date</param>
    /// <param name="dueDateTo">Filter events with due date on or before this date</param>
    /// <param name="skip">Number of records to skip (for pagination)</param>
    /// <param name="top">Number of records to return (max 100)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple of events array and total count</returns>
    Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
        int? regardingRecordType = null,
        string? regardingRecordId = null,
        Guid? eventTypeId = null,
        int? statusCode = null,
        int? priority = null,
        DateTime? dueDateFrom = null,
        DateTime? dueDateTo = null,
        int skip = 0,
        int top = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single event by ID.
    /// </summary>
    /// <param name="id">Event ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event entity or null if not found</returns>
    Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create a new event.
    /// </summary>
    /// <param name="request">Create event request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created event ID and timestamp</returns>
    Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update an existing event.
    /// </summary>
    /// <param name="id">Event ID</param>
    /// <param name="request">Update event request</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateEventAsync(Guid id, UpdateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update event status (for complete/cancel/delete operations).
    /// </summary>
    /// <param name="id">Event ID</param>
    /// <param name="statusCode">New status code</param>
    /// <param name="completedDate">Completed date (for completion only)</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default);

    /// <summary>
    /// Query event logs for a specific event.
    /// </summary>
    /// <param name="eventId">Event ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of event log entries ordered by created date descending</returns>
    Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Create an event log entry.
    /// </summary>
    /// <param name="eventId">Event ID</param>
    /// <param name="action">Action type (Created, Updated, Completed, Cancelled, Deleted)</param>
    /// <param name="description">Description of the action</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created event log ID</returns>
    Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, CancellationToken ct = default);

    /// <summary>
    /// Get all event types.
    /// </summary>
    /// <param name="activeOnly">Return only active event types</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of event type entities</returns>
    Task<EventTypeEntity[]> GetEventTypesAsync(bool activeOnly = true, CancellationToken ct = default);

    /// <summary>
    /// Get a single event type by ID.
    /// </summary>
    /// <param name="id">Event type ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event type entity or null if not found</returns>
    Task<EventTypeEntity?> GetEventTypeAsync(Guid id, CancellationToken ct = default);

    // ========================================
    // Field Mapping Operations (Events and Workflow Automation R1)
    // ========================================

    /// <summary>
    /// Query all active field mapping profiles.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of field mapping profiles</returns>
    Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a field mapping profile by source and target entity pair.
    /// </summary>
    /// <param name="sourceEntity">Source entity logical name</param>
    /// <param name="targetEntity">Target entity logical name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Profile with rules or null if not found</returns>
    Task<FieldMappingProfileEntity?> GetFieldMappingProfileAsync(
        string sourceEntity,
        string targetEntity,
        CancellationToken ct = default);

    /// <summary>
    /// Get field mapping rules for a profile.
    /// </summary>
    /// <param name="profileId">Profile ID</param>
    /// <param name="activeOnly">Return only active rules</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of mapping rules ordered by execution order</returns>
    Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
        Guid profileId,
        bool activeOnly = true,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve field values from a source record.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name</param>
    /// <param name="recordId">Record ID</param>
    /// <param name="fields">Field names to retrieve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dictionary of field name to value</returns>
    Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        string[] fields,
        CancellationToken ct = default);

    /// <summary>
    /// Query child records for push mapping operation.
    /// </summary>
    /// <param name="childEntityLogicalName">Child entity logical name</param>
    /// <param name="parentLookupField">Parent lookup field name</param>
    /// <param name="parentRecordId">Parent record ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of child record IDs</returns>
    Task<Guid[]> QueryChildRecordIdsAsync(
        string childEntityLogicalName,
        string parentLookupField,
        Guid parentRecordId,
        CancellationToken ct = default);

    /// <summary>
    /// Update multiple fields on a record.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name</param>
    /// <param name="recordId">Record ID</param>
    /// <param name="fields">Dictionary of field name to value</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default);

    // ========================================
    // Generic Entity Operations (Finance Intelligence Module R1)
    // ========================================

    /// <summary>
    /// Create a new entity record.
    /// </summary>
    /// <param name="entity">Entity to create (set entity logical name and attributes)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created entity ID</returns>
    /// <example>
    /// var billingEvent = new Entity("sprk_billingevent")
    /// {
    ///     ["sprk_invoiceid"] = new EntityReference("sprk_invoice", invoiceId),
    ///     ["sprk_linesequence"] = lineNumber,
    ///     ["sprk_amount"] = new Money(amount)
    /// };
    /// var id = await _dataverseService.CreateAsync(billingEvent, ct);
    /// </example>
    Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default);

    /// <summary>
    /// Update an existing entity record with field values from a dictionary.
    /// Builds an Entity object from the dictionary and updates via ServiceClient.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_invoice")</param>
    /// <param name="id">Record ID to update</param>
    /// <param name="fields">Dictionary of field name to value (supports primitives, EntityReference, Money, OptionSetValue)</param>
    /// <param name="ct">Cancellation token</param>
    /// <example>
    /// var fields = new Dictionary&lt;string, object&gt;
    /// {
    ///     ["sprk_status"] = new OptionSetValue(2),
    ///     ["sprk_reviewedon"] = DateTime.UtcNow,
    ///     ["sprk_matterid"] = new EntityReference("sprk_matter", matterId)
    /// };
    /// await _dataverseService.UpdateAsync("sprk_invoice", invoiceId, fields, ct);
    /// </example>
    Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default);

    /// <summary>
    /// Update multiple entity records in a single batch operation using ExecuteMultipleRequest.
    /// Use for bulk updates to improve performance (reduces round-trips to Dataverse).
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name for all records</param>
    /// <param name="updates">List of (recordId, fields) tuples to update</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task</returns>
    /// <remarks>
    /// Uses ExecuteMultipleRequest with ContinueOnError=false for transactional behavior.
    /// If any update fails, the entire batch fails. For partial failure tolerance,
    /// use individual UpdateAsync calls with try/catch.
    /// </remarks>
    Task BulkUpdateAsync(
        string entityLogicalName,
        List<(Guid id, Dictionary<string, object> fields)> updates,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve a Dataverse entity using alternate key(s) instead of GUID.
    /// Alternate keys provide portable logical identifiers that remain stable across environments.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_analysisplaybook")</param>
    /// <param name="alternateKeyValues">Key-value pairs for alternate key lookup (e.g., { "sprk_playbookcode", "PB-013" })</param>
    /// <param name="columns">Array of column names to retrieve (null = all columns)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Retrieved entity</returns>
    /// <exception cref="InvalidOperationException">Thrown if entity not found or alternate key not indexed</exception>
    /// <example>
    /// // Retrieve playbook by code (portable across environments)
    /// var keyValues = new KeyAttributeCollection { { "sprk_playbookcode", "PB-013" } };
    /// var playbook = await _dataverseService.RetrieveByAlternateKeyAsync(
    ///     "sprk_analysisplaybook",
    ///     keyValues,
    ///     new[] { "sprk_name", "sprk_configjson" },
    ///     ct);
    /// </example>
    Task<Entity> RetrieveByAlternateKeyAsync(
        string entityLogicalName,
        KeyAttributeCollection alternateKeyValues,
        string[]? columns = null,
        CancellationToken ct = default);

    // ========================================
    // KPI Assessment Operations (Matter Performance KPI R1)
    // ========================================

    /// <summary>
    /// Query KPI assessments for a parent record (matter or project), optionally filtered by performance area.
    /// Returns assessments ordered by createdon descending (most recent first).
    /// </summary>
    /// <param name="parentId">Parent record ID (matter or project)</param>
    /// <param name="parentLookupField">Lookup field name on sprk_kpiassessment (e.g., "sprk_matter" or "sprk_project")</param>
    /// <param name="performanceArea">Optional performance area filter (100000000=Guidelines, 100000001=Budget, 100000002=Outcomes)</param>
    /// <param name="top">Maximum number of records to return (0 = all)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of KPI assessment records ordered by createdon descending</returns>
    Task<KpiAssessmentRecord[]> QueryKpiAssessmentsAsync(
        Guid parentId,
        string parentLookupField = "sprk_matter",
        int? performanceArea = null,
        int top = 0,
        CancellationToken ct = default);

    // ========================================
    // Approved Sender Operations (Email Communication R1)
    // ========================================

    /// <summary>
    /// Query all active sprk_approvedsender records from Dataverse.
    /// Returns records where statecode eq 0 (Active).
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of Entity objects with sprk_name, sprk_email, sprk_isdefault attributes</returns>
    Task<Entity[]> QueryApprovedSendersAsync(CancellationToken ct = default);

    // ========================================
    // Communication Account Operations (Email Communication R1 — Phase 6)
    // ========================================

    /// <summary>
    /// Query sprk_communicationaccount records from Dataverse with an OData filter and select.
    /// </summary>
    /// <param name="filter">OData $filter expression (e.g., "sprk_sendenableds eq true and statecode eq 0")</param>
    /// <param name="select">Comma-separated $select fields</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Array of Entity objects with requested attributes</returns>
    Task<Entity[]> QueryCommunicationAccountsAsync(string filter, string select, CancellationToken ct = default);
}
