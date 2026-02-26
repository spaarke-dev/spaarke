using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.RecordSearch;

/// <summary>
/// Individual search result from record search.
/// </summary>
public sealed record RecordSearchResult
{
    /// <summary>
    /// Dataverse record ID (GUID).
    /// </summary>
    [JsonPropertyName("recordId")]
    public required string RecordId { get; init; }

    /// <summary>
    /// Dataverse entity logical name (e.g., "sprk_matter", "sprk_project", "sprk_invoice").
    /// </summary>
    [JsonPropertyName("recordType")]
    public required string RecordType { get; init; }

    /// <summary>
    /// Display name of the record.
    /// </summary>
    [JsonPropertyName("recordName")]
    public required string RecordName { get; init; }

    /// <summary>
    /// Optional description or summary of the record.
    /// </summary>
    [JsonPropertyName("recordDescription")]
    public string? RecordDescription { get; init; }

    /// <summary>
    /// Combined relevance score from RRF fusion (0.0-1.0).
    /// </summary>
    [JsonPropertyName("confidenceScore")]
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// AI-generated explanation strings describing why this record matched the query.
    /// </summary>
    [JsonPropertyName("matchReasons")]
    public IReadOnlyList<string>? MatchReasons { get; init; }

    /// <summary>
    /// Organizations associated with this record.
    /// </summary>
    [JsonPropertyName("organizations")]
    public IReadOnlyList<string>? Organizations { get; init; }

    /// <summary>
    /// People (contact names) associated with this record.
    /// </summary>
    [JsonPropertyName("people")]
    public IReadOnlyList<string>? People { get; init; }

    /// <summary>
    /// Keywords extracted from the record content.
    /// </summary>
    [JsonPropertyName("keywords")]
    public IReadOnlyList<string>? Keywords { get; init; }

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Record last modification timestamp.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset? ModifiedAt { get; init; }
}
