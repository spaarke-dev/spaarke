using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for searching documents to share from the Office add-in.
/// Supports searching by name, content type, date range, and filtering by container/entity association.
/// </summary>
/// <remarks>
/// <para>
/// Query parameters for GET /office/search/documents endpoint:
/// - q: Search term (min 2 chars required)
/// - entityType: Filter by associated entity type (optional)
/// - entityId: Filter by specific entity association (optional)
/// - containerId: Filter by SPE container (optional)
/// - contentType: Filter by content type (optional)
/// - modifiedAfter: Filter by modification date range start (optional)
/// - modifiedBefore: Filter by modification date range end (optional)
/// - skip: Number of results to skip for pagination (optional, default 0)
/// - top: Maximum results to return (optional, default 20, max 50)
/// </para>
/// </remarks>
public record DocumentSearchRequest
{
    /// <summary>
    /// Search query string. Must be at least 2 characters.
    /// Searches document name, filename, and description.
    /// </summary>
    /// <example>contract</example>
    [Required]
    [MinLength(2, ErrorMessage = "Search query must be at least 2 characters")]
    [MaxLength(200, ErrorMessage = "Search query cannot exceed 200 characters")]
    public required string Query { get; init; }

    /// <summary>
    /// Filter by associated entity type. If specified with EntityId, filters to exact entity.
    /// If specified alone, filters to all documents associated with that entity type.
    /// </summary>
    /// <remarks>
    /// Valid values: Matter, Project, Invoice, Account, Contact
    /// </remarks>
    /// <example>Matter</example>
    public AssociationEntityType? EntityType { get; init; }

    /// <summary>
    /// Filter by specific entity association ID.
    /// Should be used with EntityType for best results.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// Filter by SPE container ID.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid? ContainerId { get; init; }

    /// <summary>
    /// Filter by folder path within the container.
    /// </summary>
    /// <example>/Legal/Contracts</example>
    [MaxLength(500, ErrorMessage = "Folder path cannot exceed 500 characters")]
    public string? FolderPath { get; init; }

    /// <summary>
    /// Filter by content type (MIME type pattern). Supports partial matching.
    /// </summary>
    /// <example>application/pdf</example>
    /// <example>application/vnd.openxmlformats</example>
    [MaxLength(100, ErrorMessage = "Content type cannot exceed 100 characters")]
    public string? ContentType { get; init; }

    /// <summary>
    /// Filter by documents modified on or after this date.
    /// </summary>
    /// <example>2026-01-01T00:00:00Z</example>
    public DateTimeOffset? ModifiedAfter { get; init; }

    /// <summary>
    /// Filter by documents modified on or before this date.
    /// </summary>
    /// <example>2026-01-31T23:59:59Z</example>
    public DateTimeOffset? ModifiedBefore { get; init; }

    /// <summary>
    /// Number of results to skip for pagination.
    /// </summary>
    /// <example>0</example>
    [Range(0, int.MaxValue)]
    public int Skip { get; init; } = 0;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    /// <example>20</example>
    [Range(1, 50)]
    public int Top { get; init; } = 20;
}
