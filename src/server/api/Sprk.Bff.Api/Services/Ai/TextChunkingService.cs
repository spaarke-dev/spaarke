using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Implementation of text chunking service for splitting documents into smaller segments.
/// </summary>
/// <remarks>
/// <para>
/// This service consolidates chunking logic previously duplicated across multiple tool handlers.
/// It provides configurable chunking with support for:
/// </para>
/// <list type="bullet">
/// <item>Configurable chunk size and overlap</item>
/// <item>Sentence boundary preservation</item>
/// <item>Position tracking for each chunk</item>
/// </list>
/// </remarks>
public sealed class TextChunkingService : ITextChunkingService
{
    private readonly ILogger<TextChunkingService> _logger;

    /// <summary>
    /// Sentence terminators to look for when preserving sentence boundaries.
    /// </summary>
    private static readonly char[] SentenceTerminators = ['.', '!', '?'];

    /// <summary>
    /// Initializes a new instance of the <see cref="TextChunkingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public TextChunkingService(ILogger<TextChunkingService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextChunk>> ChunkTextAsync(
        string? text,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? ChunkingOptions.Default;

        // Handle empty or null text
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogDebug("ChunkText called with empty text, returning empty list");
            return Task.FromResult<IReadOnlyList<TextChunk>>(Array.Empty<TextChunk>());
        }

        // Handle text shorter than or equal to chunk size
        if (text.Length <= effectiveOptions.ChunkSize)
        {
            _logger.LogDebug(
                "Text length {TextLength} <= chunk size {ChunkSize}, returning single chunk",
                text.Length,
                effectiveOptions.ChunkSize);

            var singleChunk = new TextChunk
            {
                Content = text,
                Index = 0,
                StartPosition = 0,
                EndPosition = text.Length
            };

            return Task.FromResult<IReadOnlyList<TextChunk>>(new[] { singleChunk });
        }

        // Perform chunking
        var chunks = ChunkTextInternal(text, effectiveOptions);

        _logger.LogDebug(
            "Chunked text of {TextLength} chars into {ChunkCount} chunks (size: {ChunkSize}, overlap: {Overlap}, preserveSentences: {PreserveSentences})",
            text.Length,
            chunks.Count,
            effectiveOptions.ChunkSize,
            effectiveOptions.Overlap,
            effectiveOptions.PreserveSentenceBoundaries);

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    /// <summary>
    /// Internal chunking algorithm that splits text into overlapping segments.
    /// </summary>
    private List<TextChunk> ChunkTextInternal(string text, ChunkingOptions options)
    {
        var chunks = new List<TextChunk>();
        var position = 0;
        var chunkIndex = 0;

        while (position < text.Length)
        {
            // Calculate the end position for this chunk
            var remainingLength = text.Length - position;
            var chunkLength = Math.Min(options.ChunkSize, remainingLength);
            var endPosition = position + chunkLength;

            // Try to find a sentence boundary if we're not at the end of the text
            if (options.PreserveSentenceBoundaries && endPosition < text.Length)
            {
                var adjustedEnd = FindSentenceBoundary(text, position, endPosition, options.ChunkSize);
                if (adjustedEnd > position)
                {
                    endPosition = adjustedEnd;
                    chunkLength = endPosition - position;
                }
            }

            // Extract the chunk content
            var content = text.Substring(position, chunkLength);

            chunks.Add(new TextChunk
            {
                Content = content,
                Index = chunkIndex,
                StartPosition = position,
                EndPosition = endPosition
            });

            chunkIndex++;

            // Calculate the advance distance (accounting for overlap)
            var advance = chunkLength - options.Overlap;

            // Ensure we always advance to avoid infinite loops
            if (advance <= 0)
            {
                position += chunkLength;
            }
            else
            {
                position += advance;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Finds a suitable sentence boundary near the target end position.
    /// </summary>
    /// <param name="text">The full text being chunked.</param>
    /// <param name="startPosition">The start position of the current chunk.</param>
    /// <param name="targetEnd">The target end position.</param>
    /// <param name="chunkSize">The configured chunk size.</param>
    /// <returns>
    /// The adjusted end position at a sentence boundary, or the original target end
    /// if no suitable boundary is found.
    /// </returns>
    private static int FindSentenceBoundary(string text, int startPosition, int targetEnd, int chunkSize)
    {
        // Search backwards from the target end to find a sentence terminator
        // Only consider boundaries in the second half of the chunk to avoid very short chunks
        var minBoundaryPosition = startPosition + (chunkSize / 2);

        for (var i = targetEnd - 1; i >= minBoundaryPosition; i--)
        {
            // Check if this position ends a sentence (terminator followed by space or at end)
            if (IsSentenceEnd(text, i))
            {
                // Return position after the terminator (include the terminator in the chunk)
                return i + 1;
            }
        }

        // No suitable sentence boundary found, use original target
        return targetEnd;
    }

    /// <summary>
    /// Checks if the character at the given position is the end of a sentence.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <param name="position">The position to check.</param>
    /// <returns>True if this position is a sentence end.</returns>
    private static bool IsSentenceEnd(string text, int position)
    {
        if (position < 0 || position >= text.Length)
            return false;

        var c = text[position];

        // Check if it's a sentence terminator
        if (!SentenceTerminators.Contains(c))
            return false;

        // A sentence end is a terminator followed by whitespace or at end of text
        var nextPosition = position + 1;
        if (nextPosition >= text.Length)
            return true;

        return char.IsWhiteSpace(text[nextPosition]);
    }
}
