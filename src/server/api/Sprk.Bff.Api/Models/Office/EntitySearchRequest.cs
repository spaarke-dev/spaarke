using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for searching association targets (entities) in the Office add-in.
/// Supports typeahead search for Matters, Projects, Invoices, Accounts, and Contacts.
/// </summary>
/// <remarks>
/// <para>
/// Query parameters for GET /office/search/entities endpoint:
/// - q: Search term (min 2 chars required)
/// - type: Comma-separated entity types to filter (optional, defaults to all)
/// - skip: Number of results to skip for pagination (optional, default 0)
/// - top: Maximum results to return (optional, default 20, max 50)
/// </para>
/// </remarks>
public record EntitySearchRequest
{
    /// <summary>
    /// Search query string. Must be at least 2 characters.
    /// </summary>
    /// <example>acme</example>
    [Required]
    [MinLength(2, ErrorMessage = "Search query must be at least 2 characters")]
    [MaxLength(200, ErrorMessage = "Search query cannot exceed 200 characters")]
    public required string Query { get; init; }

    /// <summary>
    /// Entity types to search. If null or empty, searches all supported types.
    /// </summary>
    /// <remarks>
    /// Valid values: Matter, Project, Invoice, Account, Contact
    /// </remarks>
    /// <example>["Matter", "Account"]</example>
    public string[]? EntityTypes { get; init; }

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

/// <summary>
/// Valid entity types for association targets.
/// </summary>
public enum AssociationEntityType
{
    /// <summary>Legal matter entity (sprk_matter).</summary>
    Matter,

    /// <summary>Project entity (sprk_project).</summary>
    Project,

    /// <summary>Invoice entity (sprk_invoice).</summary>
    Invoice,

    /// <summary>Account entity (account - standard Dataverse).</summary>
    Account,

    /// <summary>Contact entity (contact - standard Dataverse).</summary>
    Contact
}
