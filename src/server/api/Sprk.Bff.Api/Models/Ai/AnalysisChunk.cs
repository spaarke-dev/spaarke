using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// A chunk of streaming document analysis output.
/// Used for SSE streaming responses.
/// <para>
/// Supported event types (<see cref="Type"/>):
/// <list type="bullet">
///   <item><c>"text"</c> — streaming partial content (see <see cref="FromContent"/>).</item>
///   <item><c>"complete"</c> — final chunk with full result (see <see cref="Completed(string)"/> / <see cref="Completed(DocumentAnalysisResult)"/>).</item>
///   <item><c>"error"</c> — error chunk (see <see cref="FromError"/>).</item>
///   <item><c>"delta"</c> — structured-field token delta (see <see cref="FromDelta"/>); R5 additive variant carrying <see cref="FieldDelta"/>.</item>
/// </list>
/// </para>
/// <para>
/// R5 additive contract (per spec NFR-10 + R5 CLAUDE.md §3.1 "Specifically prohibited"):
/// R5 EXTENDS this envelope with the <see cref="Delta"/> property and the <c>"delta"</c> event type;
/// it MUST NOT introduce a parallel SSE envelope. Existing wizard consumers
/// (<c>src/solutions/LegalWorkspace/.../summarizeService.ts</c>) ignore unknown discriminants,
/// so the <c>"delta"</c> event is silently dropped by v1.0 consumers — that IS the back-compat
/// contract. The <see cref="Delta"/> property is serialized with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> so existing serialized payloads
/// remain byte-identical when <see cref="Delta"/> is <c>null</c>.
/// </para>
/// </summary>
/// <param name="Type">Event type: "text" for streaming content, "complete" for final result, "error" for errors, "delta" for structured-field token deltas (R5 additive).</param>
/// <param name="Content">The text content of this chunk (partial summary text, for type="text").</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="Summary">The complete summary text (only set when Done=true, for backward compatibility).</param>
/// <param name="Result">The structured analysis result (only set when type="complete").</param>
/// <param name="Error">Error message if analysis failed.</param>
/// <param name="Delta">Structured-field token delta payload (only set when type="delta"). Additive R5 variant; null/omitted for all other event types.</param>
public record AnalysisChunk(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("result")] DocumentAnalysisResult? Result = null,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("delta")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FieldDelta? Delta = null)
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

    /// <summary>
    /// Create a structured-field delta chunk (R5 additive variant).
    /// Type is "delta"; carries a <see cref="FieldDelta"/> payload tagging a token chunk
    /// with a JSON path (e.g., <c>"tldr"</c>, <c>"fileHighlights[0].summary"</c>) and a
    /// monotonic <paramref name="sequence"/> number for ordering correctness.
    /// </summary>
    /// <remarks>
    /// Producer: D1-06 incremental JSON parser over Azure OpenAI Structured Outputs.
    /// Consumer: D2-07 <c>StructuredOutputStreamWidget</c> via PaneEventBus
    /// <c>workspace.field_delta</c> event (D2-06).
    /// The model does NOT validate <paramref name="path"/> syntax — that is the producer +
    /// consumer contract. Existing v1.0 wizard consumers ignore unknown discriminants
    /// (see <c>summarizeService.ts</c> lines ~80–100 — no branch for <c>"delta"</c>).
    /// </remarks>
    public static AnalysisChunk FromDelta(string path, string content, int sequence) =>
        new(Type: "delta", Content: string.Empty, Done: false,
            Delta: new FieldDelta(Path: path, Content: content, Sequence: sequence));
}

/// <summary>
/// A structured-field token delta payload carried by an <see cref="AnalysisChunk"/> with
/// <see cref="AnalysisChunk.Type"/> = <c>"delta"</c>. Enables ChatGPT/Claude-style
/// progressive rendering of structured AI output (per R5 spec FR-02 + design §4.3).
/// </summary>
/// <param name="Path">JSON path identifying which field in the streaming structured output this chunk targets. Examples: <c>"tldr"</c>, <c>"summary"</c>, <c>"fileHighlights[0].summary"</c>. The model does NOT validate path syntax; this is a producer/consumer contract.</param>
/// <param name="Content">The token-chunk text to append to the field identified by <see cref="Path"/>.</param>
/// <param name="Sequence">Monotonic sequence number from the producer, for downstream ordering correctness if deltas are reordered in transit.</param>
public record FieldDelta(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("sequence")] int Sequence);
