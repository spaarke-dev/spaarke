namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of a playbook vector similarity search.
/// Returned by <see cref="Services.Ai.PlaybookEmbedding.PlaybookEmbeddingService.SearchPlaybooksAsync"/>.
/// </summary>
public record PlaybookSearchResult
{
    /// <summary>
    /// Playbook record identifier (sprk_aiplaybook GUID).
    /// </summary>
    public required string PlaybookId { get; init; }

    /// <summary>
    /// Display name of the playbook.
    /// </summary>
    public required string PlaybookName { get; init; }

    /// <summary>
    /// Human-readable description of what the playbook does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Natural language phrases that trigger this playbook.
    /// </summary>
    public IReadOnlyList<string> TriggerPhrases { get; init; } = [];

    /// <summary>
    /// Dataverse record type this playbook operates on.
    /// </summary>
    public required string RecordType { get; init; }

    /// <summary>
    /// Entity type category.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Cosine similarity score (0.0 to 1.0). Higher is more similar.
    /// </summary>
    public double Score { get; init; }
}
