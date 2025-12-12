using Azure.Search.Documents.Models;

namespace Sprk.Bff.Api.Services.RecordMatching;

/// <summary>
/// Service interface for matching extracted document entities to Dataverse records.
/// </summary>
public interface IRecordMatchService
{
    /// <summary>
    /// Find matching Dataverse records based on extracted document entities.
    /// </summary>
    /// <param name="request">The match request containing entities and filter criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of matching record suggestions.</returns>
    Task<RecordMatchResponse> MatchAsync(RecordMatchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request model for record matching.
/// </summary>
public class RecordMatchRequest
{
    /// <summary>
    /// Organization names extracted from the document.
    /// </summary>
    public IEnumerable<string> Organizations { get; set; } = [];

    /// <summary>
    /// Person names extracted from the document.
    /// </summary>
    public IEnumerable<string> People { get; set; } = [];

    /// <summary>
    /// Reference numbers extracted from the document.
    /// </summary>
    public IEnumerable<string> ReferenceNumbers { get; set; } = [];

    /// <summary>
    /// Keywords extracted from the document.
    /// </summary>
    public IEnumerable<string> Keywords { get; set; } = [];

    /// <summary>
    /// TL;DR summary to use for semantic/vector search.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Filter by record type. Use "all" to search all types.
    /// Valid values: "sprk_matter", "sprk_project", "sprk_invoice", "all"
    /// </summary>
    public string RecordTypeFilter { get; set; } = "all";

    /// <summary>
    /// Maximum number of suggestions to return.
    /// </summary>
    public int MaxResults { get; set; } = 5;
}

/// <summary>
/// Response model for record matching.
/// </summary>
public class RecordMatchResponse
{
    /// <summary>
    /// List of matching record suggestions, ranked by confidence.
    /// </summary>
    public IReadOnlyList<RecordMatchSuggestion> Suggestions { get; set; } = [];

    /// <summary>
    /// Total number of potential matches found before ranking.
    /// </summary>
    public int TotalMatches { get; set; }
}

/// <summary>
/// A single record match suggestion.
/// </summary>
public class RecordMatchSuggestion
{
    /// <summary>
    /// The Dataverse record ID (GUID).
    /// </summary>
    public required string RecordId { get; set; }

    /// <summary>
    /// The record type (e.g., "sprk_matter", "sprk_project", "sprk_invoice").
    /// </summary>
    public required string RecordType { get; set; }

    /// <summary>
    /// The display name of the record.
    /// </summary>
    public required string RecordName { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0).
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Human-readable reasons explaining why this record matched.
    /// </summary>
    public IReadOnlyList<string> MatchReasons { get; set; } = [];

    /// <summary>
    /// The Dataverse lookup field name to populate when associating this record.
    /// </summary>
    public required string LookupFieldName { get; set; }
}
