using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.SemanticSearch;

/// <summary>
/// Optional filters for semantic search.
/// </summary>
public sealed record SearchFilters
{
    /// <summary>
    /// Filter by parent entity types (e.g., "matter", "project", "invoice", "account", "contact").
    /// When provided, restricts results to documents whose parentEntityType matches one of the specified values.
    /// </summary>
    [JsonPropertyName("entityTypes")]
    public IReadOnlyList<string>? EntityTypes { get; init; }

    /// <summary>
    /// Filter by document types (e.g., "Contract", "Agreement", "Invoice").
    /// </summary>
    [JsonPropertyName("documentTypes")]
    public IReadOnlyList<string>? DocumentTypes { get; init; }

    /// <summary>
    /// Filter by file types/extensions (e.g., "pdf", "docx", "xlsx").
    /// </summary>
    [JsonPropertyName("fileTypes")]
    public IReadOnlyList<string>? FileTypes { get; init; }

    /// <summary>
    /// Filter by document tags.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Filter by date range on createdAt or updatedAt field.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public DateRangeFilter? DateRange { get; init; }
}

/// <summary>
/// Valid parent entity type values for entity type filtering.
/// </summary>
public static class ValidEntityTypes
{
    /// <summary>Legal matter entity.</summary>
    public const string Matter = "matter";

    /// <summary>Project entity.</summary>
    public const string Project = "project";

    /// <summary>Invoice entity.</summary>
    public const string Invoice = "invoice";

    /// <summary>Account entity.</summary>
    public const string Account = "account";

    /// <summary>Contact entity.</summary>
    public const string Contact = "contact";

    /// <summary>All valid entity type values.</summary>
    public static readonly string[] All = [Matter, Project, Invoice, Account, Contact];

    /// <summary>Checks if the entity type is valid.</summary>
    public static bool IsValid(string? entityType) =>
        !string.IsNullOrWhiteSpace(entityType) && All.Contains(entityType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Date range filter for search.
/// </summary>
public sealed record DateRangeFilter
{
    /// <summary>
    /// Field to filter on: "createdAt" or "updatedAt".
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; init; } = "createdAt";

    /// <summary>
    /// Start of date range (inclusive). ISO 8601 format.
    /// </summary>
    [JsonPropertyName("from")]
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// End of date range (inclusive). ISO 8601 format.
    /// </summary>
    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; init; }
}

/// <summary>
/// Valid date range field values.
/// </summary>
public static class DateRangeField
{
    /// <summary>Filter on document creation date.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>Filter on document last update date.</summary>
    public const string UpdatedAt = "updatedAt";

    /// <summary>All valid field values.</summary>
    public static readonly string[] ValidFields = [CreatedAt, UpdatedAt];

    /// <summary>Checks if the field is valid.</summary>
    public static bool IsValid(string? field) =>
        !string.IsNullOrWhiteSpace(field) && ValidFields.Contains(field, StringComparer.OrdinalIgnoreCase);
}
