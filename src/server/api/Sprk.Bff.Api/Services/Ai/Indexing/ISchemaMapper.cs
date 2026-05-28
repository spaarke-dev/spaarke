using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Indexing;

/// <summary>
/// Maps chunked + embedded source content into typed AI Search documents for indexing,
/// and describes how to identify those documents during idempotent deletion.
/// </summary>
/// <typeparam name="TDoc">The concrete AI Search document type (e.g., <c>KnowledgeDocument</c>, Observation row, Precedent projection).</typeparam>
/// <remarks>
/// <para>
/// Authored as part of Task 025 (W3.5 refactor — Q5 audit) to allow
/// <see cref="ReferenceIndexingService"/> to target arbitrary AI Search indexes (e.g.,
/// <c>spaarke-insights-index</c> for D-P4 Precedent projection and D-P11 Observation mirror)
/// without duplicating the chunk → embed → delete → upsert pipeline.
/// </para>
/// <para>
/// Implementations MUST:
/// <list type="bullet">
///   <item>Be stateless and thread-safe (mappers are constructed per call, but may be cached).</item>
///   <item>Produce deterministic document IDs for the same (source, chunkIndex) pair so re-indexing is idempotent.</item>
///   <item>Honor the chunk → embedding alignment by index — element <c>i</c> of <paramref name="embeddings"/> corresponds to element <c>i</c> of <paramref name="chunks"/>.</item>
/// </list>
/// </para>
/// </remarks>
public interface ISchemaMapper<TDoc> where TDoc : class
{
    /// <summary>
    /// The OData filter clause used to find existing documents for the given source identifier,
    /// e.g., <c>knowledgeSourceId eq 'abc-123'</c> or <c>precedentId eq 'def-456'</c>.
    /// </summary>
    /// <param name="sourceId">The source identifier (already OData-escaped by the caller).</param>
    /// <returns>An OData filter expression matching all documents derived from the source.</returns>
    string BuildSourceFilter(string sourceId);

    /// <summary>
    /// Build typed AI Search documents from the produced chunks and their embeddings.
    /// </summary>
    /// <param name="chunks">Text chunks produced by <see cref="ITextChunkingService"/>.</param>
    /// <param name="embeddings">Embeddings aligned to chunks by index.</param>
    /// <param name="sourceId">Stable source identifier used to compose document IDs and for filtering.</param>
    /// <param name="context">Optional caller-supplied context (display name, domain, tags, metadata).</param>
    /// <returns>The documents to upsert into the target index.</returns>
    IReadOnlyList<TDoc> BuildDocuments(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        string sourceId,
        SchemaMappingContext context);
}

/// <summary>
/// Caller-supplied context passed to <see cref="ISchemaMapper{TDoc}.BuildDocuments"/>.
/// </summary>
/// <remarks>
/// Optional fields allow generic mappers to surface display-friendly metadata
/// without forcing every call site to populate every field.
/// </remarks>
public sealed record SchemaMappingContext
{
    /// <summary>Display name of the source (e.g., knowledge source name, precedent title).</summary>
    public string? Name { get; init; }

    /// <summary>Domain/category classification (e.g., "legal", "finance", "Observation").</summary>
    public string? Domain { get; init; }

    /// <summary>Tags for categorization and filtering.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Arbitrary additional context the mapper can interpret (per-mapper convention).</summary>
    public IReadOnlyDictionary<string, object?>? Extras { get; init; }

    /// <summary>Empty context — useful when callers have no display metadata to surface.</summary>
    public static SchemaMappingContext Empty { get; } = new();
}
