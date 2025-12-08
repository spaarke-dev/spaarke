namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// A chunk of streaming summarization output.
/// Used for SSE streaming responses.
/// </summary>
/// <param name="Content">The text content of this chunk (partial summary text).</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="Summary">The complete summary text (only set when Done=true).</param>
/// <param name="Error">Error message if summarization failed.</param>
public record SummarizeChunk(
    string Content,
    bool Done,
    string? Summary = null,
    string? Error = null)
{
    /// <summary>
    /// Create a content chunk (streaming partial result).
    /// </summary>
    public static SummarizeChunk FromContent(string content) => new(content, Done: false);

    /// <summary>
    /// Create the final chunk with complete summary.
    /// </summary>
    public static SummarizeChunk Completed(string summary) => new(string.Empty, Done: true, Summary: summary);

    /// <summary>
    /// Create an error chunk.
    /// </summary>
    public static SummarizeChunk FromError(string error) => new(string.Empty, Done: true, Error: error);
}
