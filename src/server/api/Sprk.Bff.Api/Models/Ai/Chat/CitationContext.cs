using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Metadata for a single citation source returned by a search tool.
///
/// Captures just enough information to allow the AI to reference a source inline
/// (e.g., "[1]") and for the frontend to render a citation footnote.
///
/// Constraint (ADR-015): <see cref="Excerpt"/> is capped at 200 characters to avoid
/// leaking full document content into citation metadata.
/// </summary>
/// <param name="CitationId">Sequential citation number (1-based), assigned in tool-call order.</param>
/// <param name="ChunkId">The unique chunk ID from the search index (e.g., "{speFileId}_{chunkIndex}").</param>
/// <param name="SourceName">Display name of the source document or knowledge article.</param>
/// <param name="PageNumber">
/// Page number in the source document, when available. Null for knowledge sources
/// that lack page-level granularity.
/// </param>
/// <param name="Excerpt">Short excerpt (max 200 chars) from the matched content for preview.</param>
public sealed record CitationMetadata(
    int CitationId,
    string ChunkId,
    string SourceName,
    int? PageNumber,
    string Excerpt);

/// <summary>
/// Thread-safe, per-message accumulator for citation metadata produced by search tools.
///
/// Created by <see cref="Services.Ai.Chat.SprkChatAgent"/> before each message and passed
/// to tool classes so they can register citations during execution. Citation IDs are assigned
/// sequentially, starting at 1, and are deterministic: they follow tool-call execution order.
///
/// The context is reset per chat message to keep citation numbering scoped to a single
/// assistant response (not accumulated across the conversation).
/// </summary>
public sealed class CitationContext
{
    /// <summary>
    /// Maximum excerpt length per ADR-015 (source metadata must not contain full document content).
    /// </summary>
    public const int MaxExcerptLength = 200;

    private readonly ConcurrentBag<CitationMetadata> _citations = [];
    private int _nextId;

    /// <summary>
    /// Registers a new citation and returns the assigned citation ID.
    /// Thread-safe: multiple tool calls can register concurrently.
    /// </summary>
    /// <param name="chunkId">The chunk ID from the search index.</param>
    /// <param name="sourceName">Display name of the source document or article.</param>
    /// <param name="pageNumber">Optional page number in the source document.</param>
    /// <param name="excerpt">Content excerpt (will be truncated to <see cref="MaxExcerptLength"/>).</param>
    /// <returns>The assigned 1-based citation ID.</returns>
    public int AddCitation(string chunkId, string sourceName, int? pageNumber, string excerpt)
    {
        var id = Interlocked.Increment(ref _nextId);
        var truncatedExcerpt = excerpt.Length > MaxExcerptLength
            ? excerpt[..MaxExcerptLength] + "..."
            : excerpt;

        _citations.Add(new CitationMetadata(id, chunkId, sourceName, pageNumber, truncatedExcerpt));
        return id;
    }

    /// <summary>
    /// Returns all citations registered so far, ordered by <see cref="CitationMetadata.CitationId"/>.
    /// </summary>
    public IReadOnlyList<CitationMetadata> GetCitations() =>
        _citations.OrderBy(c => c.CitationId).ToList();

    /// <summary>
    /// Returns the number of citations registered.
    /// </summary>
    public int Count => _citations.Count;

    /// <summary>
    /// Resets the citation context for a new message. Clears all accumulated citations
    /// and resets the citation ID counter to zero so numbering restarts at [1].
    ///
    /// Called by <see cref="Services.Ai.Chat.SprkChatAgent.SendMessageAsync"/> before each
    /// message to ensure citation numbering is scoped to a single assistant response.
    /// </summary>
    public void Reset()
    {
        _citations.Clear();
        Interlocked.Exchange(ref _nextId, 0);
    }
}
