namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Slim DTO for caching analysis state in Redis via IDistributedCache.
/// Replaces the unbounded static Dictionary&lt;Guid, AnalysisInternalModel&gt; _analysisStore.
/// </summary>
/// <remarks>
/// Key pattern: sdap:ai:analysis:{analysisId}
/// TTL: 2 hours (absolute expiration).
/// On cache miss, callers rebuild from Dataverse via ReloadAnalysisFromDataverseAsync.
/// </remarks>
public record AnalysisCacheEntry
{
    /// <summary>Analysis session identifier.</summary>
    public Guid AnalysisId { get; init; }

    /// <summary>Associated Dataverse document identifier.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Extracted document text for AI context.</summary>
    public string? DocumentText { get; init; }

    /// <summary>Current analysis status (e.g., InProgress, Completed, Failed).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>When the cache entry was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
