namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for document search results from the Office add-in.
/// Returns matched documents with metadata for preview and sharing.
/// </summary>
public record DocumentSearchResponse
{
    /// <summary>
    /// List of documents matching the search criteria.
    /// </summary>
    public required IReadOnlyList<DocumentSearchResult> Results { get; init; }

    /// <summary>
    /// Total count of documents matching the query (before pagination).
    /// </summary>
    /// <example>42</example>
    public int TotalCount { get; init; }

    /// <summary>
    /// Indicates whether there are more results available beyond the current page.
    /// </summary>
    /// <example>true</example>
    public bool HasMore { get; init; }
}

/// <summary>
/// A single document result from the search.
/// Contains metadata needed for preview in the share picker UI.
/// </summary>
public record DocumentSearchResult
{
    /// <summary>
    /// Unique identifier of the document (sprk_document GUID).
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public required Guid Id { get; init; }

    /// <summary>
    /// Display name of the document.
    /// </summary>
    /// <example>Smith Contract v2.docx</example>
    public required string Name { get; init; }

    /// <summary>
    /// Original filename of the stored file.
    /// </summary>
    /// <example>Smith Contract v2.docx</example>
    public required string FileName { get; init; }

    /// <summary>
    /// URL to view/access the document in Spaarke.
    /// </summary>
    /// <example>https://spaarke.com/documents/3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public required string WebUrl { get; init; }

    /// <summary>
    /// Content type (MIME type) of the document.
    /// </summary>
    /// <example>application/vnd.openxmlformats-officedocument.wordprocessingml.document</example>
    public required string ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    /// <example>245678</example>
    public long Size { get; init; }

    /// <summary>
    /// Last modified date of the document.
    /// </summary>
    /// <example>2026-01-20T10:30:00Z</example>
    public required DateTimeOffset ModifiedDate { get; init; }

    /// <summary>
    /// Name of the user who last modified the document.
    /// </summary>
    /// <example>John Doe</example>
    public string? ModifiedBy { get; init; }

    /// <summary>
    /// URL to thumbnail image if available. Null if no thumbnail.
    /// </summary>
    /// <example>https://api.spaarke.com/thumbnails/3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Type of entity this document is associated with.
    /// </summary>
    /// <example>Matter</example>
    public AssociationEntityType? AssociationType { get; init; }

    /// <summary>
    /// ID of the associated entity.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid? AssociationId { get; init; }

    /// <summary>
    /// Display name of the associated entity.
    /// </summary>
    /// <example>Smith vs Jones</example>
    public string? AssociationName { get; init; }

    /// <summary>
    /// SPE container ID where the file is stored.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid? ContainerId { get; init; }

    /// <summary>
    /// Description of the document if provided.
    /// </summary>
    /// <example>Final version of the service contract</example>
    public string? Description { get; init; }

    /// <summary>
    /// Indicates whether the current user can share this document.
    /// Based on document permissions and user authorization.
    /// </summary>
    /// <example>true</example>
    public bool CanShare { get; init; }
}
