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

    /// <summary>Parent document GUID (for attachments). Maps to sprk_ParentDocumentId.</summary>
    public string? ParentDocumentId { get; set; }

    /// <summary>Parent document lookup. Maps to sprk_ParentDocumentName.</summary>
    public Guid? ParentDocumentLookup { get; set; }

    /// <summary>Parent file name (for attachments). Maps to sprk_ParentFileName.</summary>
    public string? ParentFileName { get; set; }

    /// <summary>Parent Graph item ID (for attachments). Maps to sprk_ParentGraphItemId.</summary>
    public string? ParentGraphItemId { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Record Association Lookups (Phase 2 - Record Matching)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Matter lookup (sprk_matter). Maps to sprk_Matter@odata.bind.</summary>
    public Guid? MatterLookup { get; set; }

    /// <summary>Project lookup (sprk_project). Maps to sprk_Project@odata.bind.</summary>
    public Guid? ProjectLookup { get; set; }

    /// <summary>Invoice lookup (sprk_invoice). Maps to sprk_Invoice@odata.bind.</summary>
    public Guid? InvoiceLookup { get; set; }
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
