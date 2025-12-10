using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// A chunk of streaming document analysis output.
/// Used for SSE streaming responses.
/// </summary>
/// <param name="Type">Event type: "text" for streaming content, "complete" for final result, "error" for errors.</param>
/// <param name="Content">The text content of this chunk (partial summary text, for type="text").</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="Summary">The complete summary text (only set when Done=true, for backward compatibility).</param>
/// <param name="Result">The structured analysis result (only set when type="complete").</param>
/// <param name="Error">Error message if analysis failed.</param>
public record AnalysisChunk(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("result")] DocumentAnalysisResult? Result = null,
    [property: JsonPropertyName("error")] string? Error = null)
{
    /// <summary>
    /// Create a content chunk (streaming partial result).
    /// Type is "text" for real-time streaming feedback.
    /// </summary>
    public static AnalysisChunk FromContent(string content) =>
        new(Type: "text", Content: content, Done: false);

    /// <summary>
    /// Create the final chunk with complete summary (legacy method for backward compatibility).
    /// </summary>
    public static AnalysisChunk Completed(string summary) =>
        new(Type: "complete", Content: string.Empty, Done: true, Summary: summary);

    /// <summary>
    /// Create the final chunk with structured analysis result.
    /// Type is "complete" for the final SSE event with parsed result.
    /// </summary>
    public static AnalysisChunk Completed(DocumentAnalysisResult result) =>
        new(Type: "complete", Content: string.Empty, Done: true, Summary: result.Summary, Result: result);

    /// <summary>
    /// Create an error chunk.
    /// </summary>
    public static AnalysisChunk FromError(string error) =>
        new(Type: "error", Content: string.Empty, Done: true, Error: error);
}
