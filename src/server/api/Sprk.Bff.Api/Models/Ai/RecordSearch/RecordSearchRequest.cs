using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Request for record search API endpoint (POST /api/ai/search/records).
/// </summary>
/// <remarks>
/// Searches the spaarke-records-index for entity records (Matters, Projects, Invoices)
/// using hybrid semantic + keyword search.
/// </remarks>
public sealed record RecordSearchRequest
{
    /// <summary>
    /// Search query text. Max 1000 characters.
    /// </summary>
    [Required(ErrorMessage = "Query is required")]
    [StringLength(1000, ErrorMessage = "Query must not exceed 1000 characters")]
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>
    /// Dataverse entity logical names to search (e.g., "sprk_matter", "sprk_project", "sprk_invoice").
    /// At least one record type must be specified.
    /// </summary>
    [Required(ErrorMessage = "RecordTypes is required")]
    [MinLength(1, ErrorMessage = "At least one record type must be specified")]
    [JsonPropertyName("recordTypes")]
    public required IReadOnlyList<string> RecordTypes { get; init; }

    /// <summary>
    /// Optional filters for organizations, people, and reference numbers.
    /// </summary>
    [JsonPropertyName("filters")]
    public RecordSearchFilters? Filters { get; init; }

    /// <summary>
    /// Search options for pagination and hybrid mode.
    /// </summary>
    [JsonPropertyName("options")]
    public RecordSearchOptions? Options { get; init; }
}

/// <summary>
/// Known Dataverse entity logical names for record search.
/// </summary>
public static class RecordEntityType
{
    /// <summary>Matter entity.</summary>
    public const string Matter = "sprk_matter";

    /// <summary>Project entity.</summary>
    public const string Project = "sprk_project";

    /// <summary>Invoice entity.</summary>
    public const string Invoice = "sprk_invoice";

    /// <summary>All valid record type values.</summary>
    public static readonly string[] ValidTypes = [Matter, Project, Invoice];

    /// <summary>Checks if the record type is valid.</summary>
    public static bool IsValid(string? recordType) =>
        !string.IsNullOrWhiteSpace(recordType) && ValidTypes.Contains(recordType.ToLowerInvariant());
}
