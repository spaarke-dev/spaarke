namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for chunking text into smaller segments for processing.
/// Used by RAG indexing pipeline and AI tool handlers to split large documents
/// into manageable pieces while preserving semantic coherence.
/// </summary>
/// <remarks>
/// <para>
/// This service consolidates chunking logic previously duplicated across 7+ tool handlers.
/// All chunking operations should use this interface for consistency.
/// </para>
/// <para>
/// Default configuration (from spec):
/// - Chunk size: 4000 characters
/// - Overlap: 200 characters
/// - Preserve sentence boundaries: true
/// </para>
/// </remarks>
public interface ITextChunkingService
{
    /// <summary>
    /// Chunks the provided text into smaller segments based on the specified options.
    /// </summary>
    /// <param name="text">The text to chunk. If null or empty, returns an empty list.</param>
    /// <param name="options">
    /// Optional chunking configuration. If null, uses default options
    /// (4000 char chunks, 200 char overlap, sentence boundary preservation).
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A list of <see cref="TextChunk"/> objects containing the chunked text segments
    /// with metadata about their position in the original text.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The chunking algorithm:
    /// 1. If text length &lt;= chunk size, returns single chunk
    /// 2. Otherwise, splits at chunk size boundaries
    /// 3. If PreserveSentenceBoundaries is true, adjusts boundaries to end at sentence terminators
    /// 4. Applies overlap between consecutive chunks to maintain context
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<TextChunk>> ChunkTextAsync(
        string? text,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for text chunking operations.
/// </summary>
/// <remarks>
/// <para>
/// Default values align with the RAG Document Ingestion Pipeline specification:
/// - ChunkSize: 4000 characters (balances context size with embedding model limits)
/// - Overlap: 200 characters (maintains semantic continuity between chunks)
/// - PreserveSentenceBoundaries: true (avoids splitting mid-sentence)
/// </para>
/// </remarks>
public sealed record ChunkingOptions
{
    /// <summary>
    /// The target size for each text chunk in characters.
    /// </summary>
    /// <remarks>
    /// Chunks may be smaller if sentence boundary preservation is enabled
    /// and no suitable boundary is found near the target size.
    /// </remarks>
    public int ChunkSize { get; init; } = DefaultChunkSize;

    /// <summary>
    /// The number of characters to overlap between consecutive chunks.
    /// </summary>
    /// <remarks>
    /// Overlap helps maintain context across chunk boundaries, improving
    /// retrieval quality in RAG scenarios. Should be less than ChunkSize/2.
    /// </remarks>
    public int Overlap { get; init; } = DefaultOverlap;

    /// <summary>
    /// Whether to adjust chunk boundaries to align with sentence endings.
    /// </summary>
    /// <remarks>
    /// When true, the chunker will look for sentence terminators (". ", "? ", "! ")
    /// near the target boundary and adjust the chunk to end at the sentence.
    /// This improves semantic coherence of chunks.
    /// </remarks>
    public bool PreserveSentenceBoundaries { get; init; } = true;

    /// <summary>
    /// Default chunk size in characters (from spec: 4000).
    /// </summary>
    public const int DefaultChunkSize = 4000;

    /// <summary>
    /// Default overlap size in characters (from spec: 200).
    /// </summary>
    public const int DefaultOverlap = 200;

    /// <summary>
    /// Gets the default chunking options as specified in the RAG pipeline spec.
    /// </summary>
    public static ChunkingOptions Default => new();
}

/// <summary>
/// Represents a chunk of text extracted from a larger document.
/// </summary>
/// <remarks>
/// <para>
/// Each chunk contains:
/// - The actual text content
/// - Its index in the sequence of chunks
/// - Position information for mapping back to the original document
/// </para>
/// </remarks>
public sealed record TextChunk
{
    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The zero-based index of this chunk in the sequence.
    /// </summary>
    /// <remarks>
    /// Useful for maintaining order when processing chunks in parallel
    /// or when reconstructing the original document.
    /// </remarks>
    public required int Index { get; init; }

    /// <summary>
    /// The character position in the original text where this chunk starts.
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// The character position in the original text where this chunk ends (exclusive).
    /// </summary>
    public required int EndPosition { get; init; }

    /// <summary>
    /// The length of this chunk in characters.
    /// </summary>
    public int Length => Content.Length;
}
