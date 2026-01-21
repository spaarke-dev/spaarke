using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for entity search results in the Office add-in.
/// Contains matched entities for association picker display.
/// </summary>
/// <remarks>
/// <para>
/// Returned from GET /office/search/entities endpoint.
/// Supports pagination via hasMore and totalCount fields.
/// </para>
/// </remarks>
public record EntitySearchResponse
{
    /// <summary>
    /// List of matched entities.
    /// </summary>
    public required IReadOnlyList<EntitySearchResult> Results { get; init; }

    /// <summary>
    /// Total count of matching entities (before pagination).
    /// </summary>
    /// <example>42</example>
    public int TotalCount { get; init; }

    /// <summary>
    /// Indicates if there are more results available beyond the current page.
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// Correlation ID for tracing this request.
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Individual entity search result for display in the association picker.
/// </summary>
/// <remarks>
/// <para>
/// Each result represents a Dataverse entity that can be used as an
/// association target for documents saved from Office add-ins.
/// </para>
/// </remarks>
public record EntitySearchResult
{
    /// <summary>
    /// Unique identifier (primary key) of the entity.
    /// </summary>
    /// <example>a1b2c3d4-e5f6-7890-abcd-ef1234567890</example>
    public required Guid Id { get; init; }

    /// <summary>
    /// Entity type name (e.g., Matter, Account, Contact).
    /// </summary>
    /// <example>Matter</example>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AssociationEntityType EntityType { get; init; }

    /// <summary>
    /// Dataverse logical name of the entity (e.g., sprk_matter, account, contact).
    /// </summary>
    /// <example>sprk_matter</example>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Display name of the entity (primary name field).
    /// </summary>
    /// <example>Smith vs Jones</example>
    public required string Name { get; init; }

    /// <summary>
    /// Additional display information for the entity (varies by type).
    /// </summary>
    /// <remarks>
    /// Examples by type:
    /// - Matter: "Client: Acme Corp | Status: Active"
    /// - Account: "Industry: Manufacturing | City: Chicago"
    /// - Contact: "Company: Acme Corp | Title: CEO"
    /// </remarks>
    /// <example>Client: Acme Corp | Status: Active</example>
    public string? DisplayInfo { get; init; }

    /// <summary>
    /// Primary field value used for matching/display (e.g., email for contacts).
    /// </summary>
    /// <example>john.smith@acme.com</example>
    public string? PrimaryField { get; init; }

    /// <summary>
    /// Icon URL for entity type display.
    /// </summary>
    /// <example>/icons/matter.svg</example>
    public string? IconUrl { get; init; }

    /// <summary>
    /// Last modified date for sorting by recency.
    /// </summary>
    public DateTimeOffset? ModifiedOn { get; init; }
}
