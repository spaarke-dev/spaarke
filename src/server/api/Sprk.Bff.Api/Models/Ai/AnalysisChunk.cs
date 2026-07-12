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
/// </list>
/// </para>
/// <para>
/// The R5 <c>"delta"</c> event type + <c>FieldDelta</c> payload were DELETED 2026-07-07
/// (redesign-r1 task 050, per ADR-037 as amended: FieldDelta deletable at cutover; the
/// client dual-render was removed at task 046 and <c>FromDelta</c> had zero call sites).
/// Section-name-keyed <c>section_*</c> events are the structured-streaming contract.
/// </para>
/// </summary>
/// <param name="Type">Event type: "text" for streaming content, "complete" for final result, "error" for errors.</param>
/// <param name="Content">The text content of this chunk (partial summary text, for type="text").</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="Summary">The complete summary text (only set when Done=true, for backward compatibility).</param>
/// <param name="Result">
/// The structured analysis result (only set when type="complete"). Declared as
/// <see cref="object"/> so the terminal chunk can carry EITHER a coerced
/// <see cref="DocumentAnalysisResult"/> (the summarize / document-analysis consumers —
/// see <see cref="Completed(DocumentAnalysisResult)"/>) OR an arbitrary capability output
/// passed through VERBATIM as a <see cref="System.Text.Json.JsonElement"/> (compose and
/// other non-DAR dispositions — see <see cref="CompletedRaw"/>). Widened from
/// <c>DocumentAnalysisResult?</c> by spaarkeai-compose-r2 (UAT wire-loss fix): coercing
/// EVERY payload to DAR silently dropped the compose fields (<c>explanation</c>,
/// <c>keyConcepts</c>, ...) on the wire because System.Text.Json discards unknown
/// properties, so the client received an empty DAR blob instead of prose. System.Text.Json
/// serializes an <c>object</c>-typed property by its RUNTIME type, so a DAR value still
/// serializes to the identical wire shape (byte-for-byte, incl. its defaults) — the
/// summarize contract is unchanged — while a <c>JsonElement</c> serializes as its raw JSON.
/// (Same object-typed-serialization pattern already used by <see cref="AnalysisChunkChip.Args"/>.)
/// </param>
/// <param name="Error">Error message if analysis failed.</param>
/// <param name="Chips">Next-step consumer chips (only set when type="chips"). Additive ai-architecture-redesign-r1 G-P1 UAT-fix variant (2026-07-05); null/omitted for all other event types. Existing consumers ignore the unknown discriminant.</param>
public record AnalysisChunk(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("chips")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AnalysisChunkChip>? Chips = null)
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
    /// Create the final chunk carrying an arbitrary structured capability output VERBATIM
    /// (spaarkeai-compose-r2 wire-loss fix). Unlike <see cref="Completed(DocumentAnalysisResult)"/>,
    /// the payload is NOT coerced to the <see cref="DocumentAnalysisResult"/> shape — the raw
    /// <see cref="System.Text.Json.JsonElement"/> is carried on <see cref="Result"/> and serializes
    /// to the wire's <c>result</c> field as-is, so a compose action's fields
    /// (<c>explanation</c>, <c>keyConcepts</c>, <c>changes</c>, ...) SURVIVE for the client's
    /// shape-detecting formatter. Used by <c>SessionDispatchOrchestrator.DeserializeResultChunk</c>
    /// for any terminal ledger payload that is a JSON object WITHOUT the DocumentAnalysisResult
    /// signature. No top-level <see cref="Summary"/> is synthesized — the client reads the
    /// structured <c>result</c> object directly (<c>chunk.result ?? chunk.summary</c>).
    /// </summary>
    public static AnalysisChunk CompletedRaw(System.Text.Json.JsonElement payload) =>
        new(Type: "complete", Content: string.Empty, Done: true, Result: payload);

    /// <summary>
    /// Create an error chunk.
    /// </summary>
    public static AnalysisChunk FromError(string error) =>
        new(Type: "error", Content: string.Empty, Done: true, Error: error);

    /// <summary>
    /// Create a next-step chips chunk (ai-architecture-redesign-r1 G-P1 UAT fix, 2026-07-05).
    /// Type is "chips"; emitted by the Click-dispatch stream AFTER the terminal
    /// <c>complete</c> chunk so a dispatched capability's <c>sprk_chiptransitions</c>
    /// render as the NEXT chip set (e.g. summarize → "Summarize again"). Wire shape of
    /// each chip matches the Event-path <c>EventChip</c> vocabulary
    /// (<c>{targetBindingId, label, args?, requiresAttachments?}</c>) — ONE client parser
    /// (<c>parseConsumerChips</c>) reads both.
    /// </summary>
    public static AnalysisChunk FromChips(IReadOnlyList<AnalysisChunkChip> chips) =>
        new(Type: "chips", Content: string.Empty, Done: false, Chips: chips);
}

/// <summary>
/// One next-step consumer chip carried by an <see cref="AnalysisChunk"/> with
/// <see cref="AnalysisChunk.Type"/> = <c>"chips"</c>. Serialized names are pinned to the
/// task-022/023b unified chip wire vocabulary (camelCase <c>EventChip</c> shape) so the
/// client's single <c>parseConsumerChips</c> path consumes Event-path and Click-path chips
/// identically (ADR-039: <c>targetBindingId</c> IS the routing decision).
/// </summary>
/// <param name="TargetBindingId">The <c>sprk_playbookconsumer</c> row GUID to dispatch on click.</param>
/// <param name="Label">User-facing chip label.</param>
/// <param name="Args">Optional capability args forwarded verbatim (e.g. <c>{ fileIds: [...] }</c>).</param>
/// <param name="RequiresAttachments">Whether the target capability needs session attachments (client-side disabled-chip precondition).</param>
public record AnalysisChunkChip(
    [property: JsonPropertyName("targetBindingId")] string TargetBindingId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("args")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Args = null,
    [property: JsonPropertyName("requiresAttachments")] bool RequiresAttachments = false);
