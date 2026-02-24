namespace Spaarke.Dataverse;

/// <summary>
/// Request model for creating a new document
/// </summary>
public class CreateDocumentRequest
{
    public required string Name { get; set; }
    public required string ContainerId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request model for updating an existing document
/// </summary>
public class UpdateDocumentRequest
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Basic Document Properties
    // ═══════════════════════════════════════════════════════════════════════════
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? GraphItemId { get; set; }
    public string? GraphDriveId { get; set; }

    /// <summary>SharePoint file URL (enables "Open in SharePoint" links). Maps to sprk_filepath.</summary>
    public string? FilePath { get; set; }

    public bool? HasFile { get; set; }
    public DocumentStatus? Status { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AI Analysis Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>AI-generated summary of the document.</summary>
    public string? Summary { get; set; }

    /// <summary>AI-generated TL;DR bullet points (newline-separated).</summary>
    public string? TlDr { get; set; }

    /// <summary>AI-extracted keywords for search (comma-separated).</summary>
    public string? Keywords { get; set; }

    /// <summary>AI analysis status OptionSet values (Dataverse 100000000+ range):
    /// None=100000000, Pending=100000001, Completed=100000002, OptedOut=100000003,
    /// Failed=100000004, NotSupported=100000005, Skipped=100000006.</summary>
    public int? SummaryStatus { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Extracted Entities Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>AI-extracted organization names (newline-separated).</summary>
    public string? ExtractOrganization { get; set; }

    /// <summary>AI-extracted person names (newline-separated).</summary>
    public string? ExtractPeople { get; set; }

    /// <summary>AI-extracted monetary amounts (newline-separated).</summary>
    public string? ExtractFees { get; set; }

    /// <summary>AI-extracted dates (newline-separated).</summary>
    public string? ExtractDates { get; set; }

    /// <summary>AI-extracted reference numbers (newline-separated).</summary>
    public string? ExtractReference { get; set; }

    /// <summary>AI-classified document type (raw text value).</summary>
    public string? ExtractDocumentType { get; set; }

    /// <summary>Document type choice field value (mapped from AI classification).</summary>
    public int? DocumentType { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Email Metadata Fields (for .eml and .msg files)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Email subject line. Maps to sprk_EmailSubject.</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Email sender address(es). Maps to sprk_EmailFrom.</summary>
    public string? EmailFrom { get; set; }

    /// <summary>Email recipient address(es). Maps to sprk_EmailTo.</summary>
    public string? EmailTo { get; set; }

    /// <summary>Email sent date/time. Maps to sprk_EmailDate.</summary>
    public DateTime? EmailDate { get; set; }

    /// <summary>Email body content (truncated to 10K chars). Maps to sprk_EmailBody.</summary>
    public string? EmailBody { get; set; }

    /// <summary>Email CC recipients. Maps to sprk_EmailCc (if field exists).</summary>
    public string? EmailCc { get; set; }

    /// <summary>Email Message-ID header (RFC 5322). Maps to sprk_EmailMessageId.</summary>
    public string? EmailMessageId { get; set; }

    /// <summary>Email direction choice value: Received=100000000, Sent=100000001. Maps to sprk_EmailDirection.</summary>
    public int? EmailDirection { get; set; }

    /// <summary>Email tracking token. Maps to sprk_EmailTrackingToken.</summary>
    public string? EmailTrackingToken { get; set; }

    /// <summary>Email conversation index. Maps to sprk_EmailConversationIndex.</summary>
    public string? EmailConversationIndex { get; set; }

    /// <summary>Email activity lookup. Maps to sprk_Email@odata.bind.</summary>
    public Guid? EmailLookup { get; set; }

    /// <summary>Is this document an email archive (.eml). Maps to sprk_IsEmailArchive.</summary>
    public bool? IsEmailArchive { get; set; }

    /// <summary>Relationship type choice value: Email Attachment=100000000. Maps to sprk_RelationshipType.</summary>
    public int? RelationshipType { get; set; }

    /// <summary>JSON array of attachment metadata. Maps to sprk_Attachments.</summary>
    public string? Attachments { get; set; }

    /// <summary>
    /// DEPRECATED: This field does not exist in Dataverse schema.
    /// Use ParentDocumentLookup instead (sets sprk_ParentDocument lookup via @odata.bind).
    /// </summary>
    [Obsolete("Use ParentDocumentLookup instead - sprk_parentdocumentid does not exist in Dataverse")]
    public string? ParentDocumentId { get; set; }

    /// <summary>Parent document lookup. Maps to sprk_ParentDocument@odata.bind.</summary>
    public Guid? ParentDocumentLookup { get; set; }

    /// <summary>Parent file name (for attachments). Maps to sprk_ParentFileName.</summary>
    public string? ParentFileName { get; set; }

    /// <summary>Parent Graph item ID (for attachments). Maps to sprk_ParentGraphItemId.</summary>
    public string? ParentGraphItemId { get; set; }

    /// <summary>Parent email's internetMessageId (for attachments from emails). Maps to sprk_emailparentid.</summary>
    public string? EmailParentId { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Record Association Lookups (Phase 2 - Record Matching)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Matter lookup (sprk_matter). Maps to sprk_Matter@odata.bind.</summary>
    public Guid? MatterLookup { get; set; }

    /// <summary>Project lookup (sprk_project). Maps to sprk_Project@odata.bind.</summary>
    public Guid? ProjectLookup { get; set; }

    /// <summary>Invoice lookup (sprk_invoice). Maps to sprk_Invoice@odata.bind.</summary>
    public Guid? InvoiceLookup { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Document Source Tracking
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Document source type choice value. Maps to sprk_SourceType.
    /// Values: UserUpload=659490000, EmailReceived=659490001, EmailSent=659490002,
    /// EmailArchive=659490003, EmailAttachment=659490004, Import=659490005, SystemGenerated=659490006.
    /// </summary>
    public int? SourceType { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Search Index Tracking Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether the document has been indexed for semantic search. Maps to sprk_searchindexed.
    /// </summary>
    public bool? SearchIndexed { get; set; }

    /// <summary>
    /// Name of the search index where document is stored. Maps to sprk_searchindexname.
    /// </summary>
    public string? SearchIndexName { get; set; }

    /// <summary>
    /// Timestamp when document was last indexed. Maps to sprk_searchindexedon.
    /// </summary>
    public DateTime? SearchIndexedOn { get; set; }
}

/// <summary>
/// Document entity model
/// </summary>
public class DocumentEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ContainerId { get; set; }
    public bool HasFile { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? GraphItemId { get; set; }
    public string? GraphDriveId { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }

    /// <summary>Created by user display name. Maps to _createdby_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? CreatedBy { get; set; }

    // Document Profile fields (populated by AI)
    /// <summary>TL;DR summary (1-2 sentences). Maps to sprk_tldr.</summary>
    public string? Tldr { get; set; }

    /// <summary>Full summary (2-4 paragraphs). Maps to sprk_summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Comma-separated keywords. Maps to sprk_keywords.</summary>
    public string? Keywords { get; set; }

    /// <summary>Document type classification (e.g., Contract, NDA, Invoice). Maps to sprk_documenttype.</summary>
    public string? DocumentType { get; set; }

    /// <summary>Extracted entities in JSON format (parties, dates, amounts). Maps to sprk_entities.</summary>
    public string? Entities { get; set; }

    // Email metadata fields (for .eml documents)
    /// <summary>Email subject. Maps to sprk_emailsubject.</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Email sender. Maps to sprk_emailfrom.</summary>
    public string? EmailFrom { get; set; }

    /// <summary>Email recipients. Maps to sprk_emailto.</summary>
    public string? EmailTo { get; set; }

    /// <summary>Email CC recipients. Maps to sprk_emailcc.</summary>
    public string? EmailCc { get; set; }

    /// <summary>Email sent date. Maps to sprk_emaildate.</summary>
    public DateTime? EmailDate { get; set; }

    /// <summary>Email body text (truncated). Maps to sprk_emailbody.</summary>
    public string? EmailBody { get; set; }

    /// <summary>Is this document an email archive (.eml). Maps to sprk_isemailarchive.</summary>
    public bool? IsEmailArchive { get; set; }

    /// <summary>Parent document ID for attachments. Maps to _sprk_parentdocument_value.</summary>
    public string? ParentDocumentId { get; set; }

    /// <summary>Email conversation index for thread correlation. Maps to sprk_emailconversationindex.</summary>
    public string? EmailConversationIndex { get; set; }

    // Record association lookups (for relationship queries)
    /// <summary>Matter ID lookup. Maps to _sprk_matter_value.</summary>
    public string? MatterId { get; set; }

    /// <summary>Matter display name. Maps to _sprk_matter_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? MatterName { get; set; }

    /// <summary>Project ID lookup. Maps to _sprk_project_value.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Project display name. Maps to _sprk_project_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Invoice ID lookup. Maps to _sprk_invoice_value.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Invoice display name. Maps to _sprk_invoice_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? InvoiceName { get; set; }
}

/// <summary>
/// Document status enumeration (matches Dataverse statuscode values)
/// </summary>
public enum DocumentStatus
{
    Draft = 1,
    Error = 2,
    Active = 421500001,
    Processing = 421500002
}

/// <summary>
/// Access level enumeration for documents
/// </summary>
public enum DocumentAccessLevel
{
    None = 0,
    Read = 1,
    Write = 2,
    FullControl = 3
}

/// <summary>
/// Metadata for a lookup navigation property on a child entity.
/// Used for discovering case-sensitive navigation properties for @odata.bind operations.
/// </summary>
public record LookupNavigationMetadata
{
    /// <summary>
    /// Logical name of the lookup attribute (e.g., "sprk_matter")
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Schema name of the lookup attribute (may differ in case, e.g., "sprk_Matter")
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Navigation property name for @odata.bind (CASE-SENSITIVE!)
    /// Example: "sprk_Matter" (capital M)
    /// This is ReferencingEntityNavigationPropertyName from metadata
    /// </summary>
    public required string NavigationPropertyName { get; init; }

    /// <summary>
    /// Target entity logical name (e.g., "sprk_matter")
    /// </summary>
    public required string TargetEntityLogicalName { get; init; }
}

/// <summary>
/// Analysis entity model (sprk_analysis)
/// </summary>
public class AnalysisEntity
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public Guid DocumentId { get; set; }
    public string? WorkingDocument { get; set; }
    public string? ChatHistory { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}

/// <summary>
/// Analysis Action entity model (sprk_analysisaction)
/// </summary>
public class AnalysisActionEntity
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Analysis Output entity model (sprk_analysisoutput).
/// Stores individual output values from analysis execution.
/// </summary>
public class AnalysisOutputEntity
{
    /// <summary>Analysis Output ID (sprk_analysisoutputid)</summary>
    public Guid Id { get; set; }

    /// <summary>Output name for display</summary>
    public string? Name { get; set; }

    /// <summary>The actual output value/content</summary>
    public string? Value { get; set; }

    /// <summary>Parent analysis ID (lookup to sprk_analysis)</summary>
    public Guid AnalysisId { get; set; }

    /// <summary>Output type ID (lookup to sprk_aioutputtype)</summary>
    public Guid? OutputTypeId { get; set; }

    /// <summary>Sequence order for display</summary>
    public int? SortOrder { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════════════
// Event Management Entities (Events and Workflow Automation R1)
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Event entity model (sprk_event)
/// </summary>
public class EventEntity
{
    /// <summary>Event ID (sprk_eventid)</summary>
    public Guid Id { get; set; }

    /// <summary>Event name (sprk_eventname) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Event Type lookup ID (_sprk_eventtype_ref_value)</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Event Type name (from expanded lookup)</summary>
    public string? EventTypeName { get; set; }

    /// <summary>State code: Active (0), Inactive (1)</summary>
    public int StateCode { get; set; }

    /// <summary>Status code: Draft (1), Planned (2), Open (3), OnHold (4), Completed (5), Cancelled (6), Deleted (7)</summary>
    public int StatusCode { get; set; }

    /// <summary>Base date (sprk_basedate)</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date (sprk_duedate)</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Completed date (sprk_completeddate)</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Source: User (0), System (1), Workflow (2), External (3)</summary>
    public int? Source { get; set; }

    /// <summary>Remind at (sprk_remindat)</summary>
    public DateTime? RemindAt { get; set; }

    /// <summary>Related Event lookup ID (_sprk_relatedevent_value)</summary>
    public Guid? RelatedEventId { get; set; }

    /// <summary>Related Event Type: Reminder (0), Notification (1), Extension (2)</summary>
    public int? RelatedEventType { get; set; }

    /// <summary>Related Event Offset Type: HoursBefore (0), HoursAfter (1), DaysBefore (2), DaysAfter (3), Fixed (4)</summary>
    public int? RelatedEventOffsetType { get; set; }

    // Regarding lookup fields (entity-specific lookups)
    /// <summary>Regarding Account lookup (_sprk_regardingaccount_value)</summary>
    public Guid? RegardingAccountId { get; set; }
    /// <summary>Regarding Analysis lookup (_sprk_regardinganalysis_value)</summary>
    public Guid? RegardingAnalysisId { get; set; }
    /// <summary>Regarding Contact lookup (_sprk_regardingcontact_value)</summary>
    public Guid? RegardingContactId { get; set; }
    /// <summary>Regarding Invoice lookup (_sprk_regardinginvoice_value)</summary>
    public Guid? RegardingInvoiceId { get; set; }
    /// <summary>Regarding Matter lookup (_sprk_regardingmatter_value)</summary>
    public Guid? RegardingMatterId { get; set; }
    /// <summary>Regarding Project lookup (_sprk_regardingproject_value)</summary>
    public Guid? RegardingProjectId { get; set; }
    /// <summary>Regarding Budget lookup (_sprk_regardingbudget_value)</summary>
    public Guid? RegardingBudgetId { get; set; }
    /// <summary>Regarding Work Assignment lookup (_sprk_regardingworkassignment_value)</summary>
    public Guid? RegardingWorkAssignmentId { get; set; }

    // Denormalized regarding fields (for unified views)
    /// <summary>Regarding record ID as string (sprk_regardingrecordid)</summary>
    public string? RegardingRecordId { get; set; }
    /// <summary>Regarding record display name (sprk_regardingrecordname)</summary>
    public string? RegardingRecordName { get; set; }
    /// <summary>Regarding record type: Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), WorkAssignment (6), Budget (7)</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Modified date/time</summary>
    public DateTime ModifiedOn { get; set; }
}

/// <summary>
/// Request model for creating a new Event
/// </summary>
public class CreateEventRequest
{
    /// <summary>Event name (required)</summary>
    public required string Name { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Event Type ID</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Base date</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Regarding record type</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Regarding record ID</summary>
    public string? RegardingRecordId { get; set; }

    /// <summary>Regarding record name</summary>
    public string? RegardingRecordName { get; set; }
}

/// <summary>
/// Request model for updating an Event
/// </summary>
public class UpdateEventRequest
{
    /// <summary>Event name</summary>
    public string? Name { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Event Type ID</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Base date</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Status code</summary>
    public int? StatusCode { get; set; }

    /// <summary>Regarding record type</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Regarding record ID</summary>
    public string? RegardingRecordId { get; set; }

    /// <summary>Regarding record name</summary>
    public string? RegardingRecordName { get; set; }
}

/// <summary>
/// Event Type entity model (sprk_eventtype)
/// </summary>
public class EventTypeEntity
{
    /// <summary>Event Type ID (sprk_eventtypeid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Event code (sprk_eventcode)</summary>
    public string? EventCode { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>State code: Active (0), Inactive (1)</summary>
    public int StateCode { get; set; }

    /// <summary>Requires due date: No (0), Yes (1)</summary>
    public int? RequiresDueDate { get; set; }

    /// <summary>Requires base date: No (0), Yes (1)</summary>
    public int? RequiresBaseDate { get; set; }
}

/// <summary>
/// Event Log entity model (sprk_eventlog)
/// </summary>
public class EventLogEntity
{
    /// <summary>Event Log ID (sprk_eventlogid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_eventlogname) - Primary field</summary>
    public string? Name { get; set; }

    /// <summary>Event lookup ID (_sprk_event_value)</summary>
    public Guid EventId { get; set; }

    /// <summary>Action: Created (0), Updated (1), Completed (2), Cancelled (3), Deleted (4)</summary>
    public int Action { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Created by user ID</summary>
    public Guid? CreatedById { get; set; }

    /// <summary>Created by user name</summary>
    public string? CreatedByName { get; set; }
}

/// <summary>
/// Event Log action constants
/// </summary>
public static class EventLogAction
{
    public const int Created = 0;
    public const int Updated = 1;
    public const int Completed = 2;
    public const int Cancelled = 3;
    public const int Deleted = 4;

    public static string GetDisplayName(int action) => action switch
    {
        Created => "Created",
        Updated => "Updated",
        Completed => "Completed",
        Cancelled => "Cancelled",
        Deleted => "Deleted",
        _ => "Unknown"
    };
}

// ═══════════════════════════════════════════════════════════════════════════════════════
// Field Mapping Framework Entities (Events and Workflow Automation R1)
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Field Mapping Profile entity model (sprk_fieldmappingprofile)
/// </summary>
public class FieldMappingProfileEntity
{
    /// <summary>Profile ID (sprk_fieldmappingprofileid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Source entity logical name (sprk_sourceentity)</summary>
    public required string SourceEntity { get; set; }

    /// <summary>Target entity logical name (sprk_targetentity)</summary>
    public required string TargetEntity { get; set; }

    /// <summary>Mapping direction: ParentToChild (0), ChildToParent (1), Bidirectional (2)</summary>
    public int MappingDirection { get; set; }

    /// <summary>Sync mode: OneTime (0), ManualRefresh (1)</summary>
    public int SyncMode { get; set; }

    /// <summary>Is active</summary>
    public bool IsActive { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Child mapping rules (populated via expand or separate query)</summary>
    public List<FieldMappingRuleEntity>? Rules { get; set; }
}

/// <summary>
/// Field Mapping Rule entity model (sprk_fieldmappingrule)
/// </summary>
public class FieldMappingRuleEntity
{
    /// <summary>Rule ID (sprk_fieldmappingruleid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Mapping Profile lookup ID (_sprk_fieldmappingprofile_value)</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Source field schema name (sprk_sourcefield)</summary>
    public required string SourceField { get; set; }

    /// <summary>Source field type: Text (0), Lookup (1), OptionSet (2), Number (3), DateTime (4), Boolean (5), Memo (6)</summary>
    public int SourceFieldType { get; set; }

    /// <summary>Target field schema name (sprk_targetfield)</summary>
    public required string TargetField { get; set; }

    /// <summary>Target field type</summary>
    public int TargetFieldType { get; set; }

    /// <summary>Compatibility mode: Strict (0), Resolve (1)</summary>
    public int CompatibilityMode { get; set; }

    /// <summary>Is required (fail if source is empty)</summary>
    public bool IsRequired { get; set; }

    /// <summary>Default value when source is empty</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Is cascading source (triggers secondary mappings)</summary>
    public bool IsCascadingSource { get; set; }

    /// <summary>Execution order for dependent mappings</summary>
    public int ExecutionOrder { get; set; }

    /// <summary>Is active</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Field mapping direction constants
/// </summary>
public static class MappingDirection
{
    public const int ParentToChild = 0;
    public const int ChildToParent = 1;
    public const int Bidirectional = 2;
}

/// <summary>
/// Field mapping sync mode constants
/// </summary>
public static class SyncMode
{
    public const int OneTime = 0;
    public const int ManualRefresh = 1;
}

/// <summary>
/// Field type constants for mapping rules
/// </summary>
public static class FieldMappingFieldType
{
    public const int Text = 0;
    public const int Lookup = 1;
    public const int OptionSet = 2;
    public const int Number = 3;
    public const int DateTime = 4;
    public const int Boolean = 5;
    public const int Memo = 6;
}

/// <summary>
/// Regarding record type constants
/// </summary>
public static class RegardingRecordType
{
    public const int Project = 0;
    public const int Matter = 1;
    public const int Invoice = 2;
    public const int Analysis = 3;
    public const int Account = 4;
    public const int Contact = 5;
    public const int WorkAssignment = 6;
    public const int Budget = 7;

    /// <summary>
    /// Gets the entity logical name for a regarding record type
    /// </summary>
    public static string? GetEntityLogicalName(int recordType) => recordType switch
    {
        Project => "sprk_project",
        Matter => "sprk_matter",
        Invoice => "sprk_invoice",
        Analysis => "sprk_analysis",
        Account => "account",
        Contact => "contact",
        WorkAssignment => "sprk_workassignment",
        Budget => "sprk_budget",
        _ => null
    };

    /// <summary>
    /// Gets the regarding lookup field name for a regarding record type
    /// </summary>
    public static string? GetLookupFieldName(int recordType) => recordType switch
    {
        Project => "sprk_regardingproject",
        Matter => "sprk_regardingmatter",
        Invoice => "sprk_regardinginvoice",
        Analysis => "sprk_regardinganalysis",
        Account => "sprk_regardingaccount",
        Contact => "sprk_regardingcontact",
        WorkAssignment => "sprk_regardingworkassignment",
        Budget => "sprk_regardingbudget",
        _ => null
    };
}

/// <summary>
/// Lightweight record returned by KPI assessment queries.
/// Contains only the fields needed for scorecard calculations.
/// </summary>
public class KpiAssessmentRecord
{
    /// <summary>Assessment record ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Grade choice value from sprk_kpigradescore (e.g. 100000000=A+, 100000001=A, 100000003=B, 100000005=C, 100000008=F, 100000009=No Grade).</summary>
    public int Grade { get; set; }

    /// <summary>Record creation timestamp (used for ordering).</summary>
    public DateTime CreatedOn { get; set; }
}
