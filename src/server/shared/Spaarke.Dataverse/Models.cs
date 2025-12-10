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

    /// <summary>AI analysis status: None=0, Pending=1, Completed=2, OptedOut=3, Failed=4.</summary>
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
