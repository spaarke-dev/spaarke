namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of a RAG indexing pipeline run for a single document.
/// </summary>
/// <remarks>
/// Returned by <see cref="Sprk.Bff.Api.Services.Ai.RagIndexingPipeline.IndexDocumentAsync"/>
/// to report the outcome of chunking, embedding, and indexing into both the
/// knowledge index and the discovery index.
/// </remarks>
public record IndexingResult
{
    /// <summary>
    /// The source document identifier that was indexed.
    /// Matches the <c>documentId</c> field stored in each AI Search chunk.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Number of chunks successfully uploaded to the knowledge index
    /// (512-token curated chunks per <see cref="ChunkOptions.ForKnowledgeIndex"/>).
    /// </summary>
    public int KnowledgeChunksIndexed { get; init; }

    /// <summary>
    /// Number of chunks successfully uploaded to the discovery index
    /// (1024-token auto-populated chunks per <see cref="ChunkOptions.ForDiscoveryIndex"/>).
    /// </summary>
    public int DiscoveryChunksIndexed { get; init; }

    /// <summary>
    /// Total wall-clock time for the full pipeline run (parse → chunk → embed → index)
    /// measured in milliseconds.
    /// NFR-11 target: complete within 60 000 ms.
    /// </summary>
    public long DurationMs { get; init; }
}
