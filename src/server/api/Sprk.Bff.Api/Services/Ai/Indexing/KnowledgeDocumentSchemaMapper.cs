using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Indexing;

/// <summary>
/// Default <see cref="ISchemaMapper{TDoc}"/> for the <c>spaarke-rag-references</c> index —
/// preserves the byte-for-byte behavior of the pre-refactor <see cref="ReferenceIndexingService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Document ID format: <c>{knowledgeSourceId}_ref_{chunkIndex}</c>.
/// Source filter: <c>knowledgeSourceId eq '...'</c>.
/// Sets <c>TenantId = "system"</c>, mirroring the original golden-reference convention.
/// </para>
/// <para>
/// Authored as part of Task 025 (W3.5 refactor — Q5 audit).
/// </para>
/// </remarks>
public sealed class KnowledgeDocumentSchemaMapper : ISchemaMapper<KnowledgeDocument>
{
    /// <summary>Shared singleton instance — mapper is stateless.</summary>
    public static readonly KnowledgeDocumentSchemaMapper Instance = new();

    /// <inheritdoc />
    public string BuildSourceFilter(string sourceId) =>
        $"knowledgeSourceId eq '{sourceId}'";

    /// <inheritdoc />
    public IReadOnlyList<KnowledgeDocument> BuildDocuments(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        string sourceId,
        SchemaMappingContext context)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentException.ThrowIfNullOrEmpty(sourceId);

        var now = DateTimeOffset.UtcNow;
        var name = context.Name ?? sourceId;
        var domain = context.Domain;
        var tags = context.Tags is { Count: > 0 } ? context.Tags.ToList() : null;

        var docs = new List<KnowledgeDocument>(chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            docs.Add(new KnowledgeDocument
            {
                // Format: {knowledgeSourceId}_ref_{chunkIndex}
                Id = $"{sourceId}_ref_{chunk.Index}",
                TenantId = "system",
                KnowledgeSourceId = sourceId,
                KnowledgeSourceName = name,
                DocumentType = domain,
                Content = chunk.Content,
                ChunkIndex = chunk.Index,
                ChunkCount = chunks.Count,
                ContentVector = i < embeddings.Count ? embeddings[i] : ReadOnlyMemory<float>.Empty,
                Tags = tags,
                FileName = name,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return docs;
    }
}
