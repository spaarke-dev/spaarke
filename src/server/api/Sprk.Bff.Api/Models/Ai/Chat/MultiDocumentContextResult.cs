using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Groups selected chunks for a single document within a multi-document context request.
/// Each group tracks its own token usage against the shared 30K budget.
/// </summary>
/// <param name="DocumentId">Dataverse sprk_document ID.</param>
/// <param name="DocumentName">Display name of the document (for attribution headers).</param>
/// <param name="SelectedChunks">
/// Chunks selected for this document after proportional budget allocation and
/// conversation-aware relevance ranking.
/// </param>
/// <param name="TotalChunks">Total chunks produced from the full document before selection.</param>
/// <param name="TokensAllocated">Token budget allocated to this document (after leftover reallocation).</param>
/// <param name="TokensUsed">Actual tokens used by the selected chunks.</param>
/// <param name="WasTruncated">True when the document exceeded its allocated budget.</param>
public sealed record DocumentChunkGroup(
    string DocumentId,
    string? DocumentName,
    IReadOnlyList<DocumentChunk> SelectedChunks,
    int TotalChunks,
    int TokensAllocated,
    int TokensUsed,
    bool WasTruncated);

/// <summary>
/// Scored chunk for cross-document interleaving. Carries the relevance score and
/// source document index so chunks from all documents can be merged by relevance.
/// </summary>
/// <param name="Chunk">The document chunk.</param>
/// <param name="RelevanceScore">
/// Cosine similarity score between the chunk embedding and the latest user message embedding.
/// Higher values indicate greater relevance. Range: -1 to 1.
/// </param>
/// <param name="DocumentIndex">
/// Zero-based index into the <see cref="MultiDocumentContextResult.DocumentGroups"/> list
/// identifying which document this chunk belongs to.
/// </param>
/// <param name="DocumentName">Display name of the source document for attribution.</param>
internal sealed record ScoredChunk(
    DocumentChunk Chunk,
    double RelevanceScore,
    int DocumentIndex,
    string? DocumentName);

/// <summary>
/// Result of multi-document context injection produced by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.DocumentContextService.InjectMultiDocumentContextAsync"/>.
///
/// Contains per-document chunk groups (with individual token allocations) and a
/// cross-document merged chunk list interleaved by relevance score. Used by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.PlaybookChatContextProvider"/>
/// to append multi-document content to the system prompt.
/// </summary>
/// <param name="DocumentGroups">
/// Per-document groups with selected chunks and token usage. One entry per document ID
/// in the original request (including documents that yielded no content).
/// </param>
/// <param name="MergedChunks">
/// Cross-document interleaved chunks sorted by relevance score (highest first).
/// This ordering ensures the most pertinent content from any document appears first
/// in the prompt, enabling effective comparison and cross-reference tasks.
/// </param>
/// <param name="TotalTokensUsed">
/// Sum of tokens across all selected chunks. Always &lt;= 30K budget.
/// </param>
/// <param name="AnyTruncated">
/// True when at least one document exceeded its proportional budget allocation.
/// </param>
public sealed record MultiDocumentContextResult(
    IReadOnlyList<DocumentChunkGroup> DocumentGroups,
    IReadOnlyList<DocumentChunk> MergedChunks,
    int TotalTokensUsed,
    bool AnyTruncated)
{
    /// <summary>
    /// Creates an empty result for cases where no documents could be processed.
    /// </summary>
    public static MultiDocumentContextResult Empty() =>
        new(Array.Empty<DocumentChunkGroup>(), Array.Empty<DocumentChunk>(), 0, false);

    /// <summary>
    /// Formats the merged chunks as a single string block suitable for injection
    /// into the system prompt's document context section, with per-document attribution headers.
    /// </summary>
    /// <remarks>
    /// Each document's chunks are preceded by a <c>[Document: {filename}]</c> header
    /// to help the AI attribute quotes correctly in cross-document analysis.
    /// Chunks are presented in relevance order (not document order) so the most
    /// pertinent content from any document appears first.
    /// </remarks>
    public string FormatForSystemPrompt()
    {
        if (MergedChunks.Count == 0)
            return string.Empty;

        var parts = new List<string>();

        if (AnyTruncated)
        {
            parts.Add(
                $"[Showing the most relevant sections from {DocumentGroups.Count} documents. " +
                "Ask about a specific section or document to surface more content.]");
        }

        // Group merged chunks by their document to add attribution headers
        // but maintain relevance ordering within the output
        string? lastDocId = null;
        foreach (var chunk in MergedChunks)
        {
            // Find the document group for this chunk to get the document name
            var group = DocumentGroups.FirstOrDefault(g =>
                g.SelectedChunks.Contains(chunk));

            var docId = group?.DocumentId;
            if (docId != lastDocId && group != null)
            {
                var docLabel = !string.IsNullOrWhiteSpace(group.DocumentName)
                    ? group.DocumentName
                    : group.DocumentId;
                parts.Add($"[Document: {docLabel}]");
                lastDocId = docId;
            }

            parts.Add(chunk.Content);
        }

        return string.Join("\n\n", parts);
    }
}
