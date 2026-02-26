using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Optional filters for record search.
/// </summary>
public sealed record RecordSearchFilters
{
    /// <summary>
    /// Filter by organization names associated with records.
    /// </summary>
    [JsonPropertyName("organizations")]
    public IReadOnlyList<string>? Organizations { get; init; }

    /// <summary>
    /// Filter by people (contact names) associated with records.
    /// </summary>
    [JsonPropertyName("people")]
    public IReadOnlyList<string>? People { get; init; }

    /// <summary>
    /// Filter by reference numbers (e.g., matter numbers, project codes, invoice numbers).
    /// </summary>
    [JsonPropertyName("referenceNumbers")]
    public IReadOnlyList<string>? ReferenceNumbers { get; init; }
}
