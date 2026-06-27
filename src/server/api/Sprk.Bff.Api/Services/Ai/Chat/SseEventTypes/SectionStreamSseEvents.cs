using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// Per-section SSE event types emitted when a <see cref="Nodes.NodeType.DeliverComposite"/>
/// node completes its composition (FR-53 / chat-routing-redesign-r1 task 114a).
///
/// <para>
/// <b>Why these three events</b>: the legacy single-action <c>Output</c> Node model couples
/// (1) schema declaration on the Action, (2) field-position contracts in the schema,
/// (3) a schema-aware widget renderer, and (4) a schema-aware <c>FieldDelta</c> streaming
/// protocol. Adding a section requires touching 5 coordination points. The composite +
/// per-section-event model collapses this to 2 coordination points (section name +
/// section state). The three events form the lifecycle:
/// </para>
/// <list type="bullet">
///   <item><see cref="SectionStartedSseEvent"/> — emitted once when a section begins
///     composition. Lets the front-end mount the section container before content arrives.</item>
///   <item><see cref="SectionDataSseEvent"/> — emitted one or more times as section content
///     becomes available. Phase A (114R / 114a): one consolidated emission per section.
///     Future incremental-streaming support (a downstream task once composite playbooks
///     stream sub-events) emits multiple deltas keyed by the same section name.</item>
///   <item><see cref="SectionCompletedSseEvent"/> — emitted once when a section is finalized,
///     carrying the final consolidated content. Marks the end of the section's lifecycle.</item>
/// </list>
///
/// <para>
/// <b>Ordering invariant (FR-53)</b>: events are keyed by section <i>name</i> (not schema
/// position). The producer (<see cref="PlaybookOrchestrationService"/>) emits sections in
/// completion order; the frontend renders by name match. Sibling sections may interleave
/// in the stream — the section <c>name</c> field is the load-bearing correlation key.
/// </para>
///
/// <para>
/// <b>Backward-compat invariant (FR-53 / project)</b>: these events are emitted ONLY when
/// a <see cref="Nodes.NodeType.DeliverComposite"/> node executes. Existing schema-position
/// playbooks (<see cref="Nodes.NodeType.Output"/> → <c>FieldDelta</c> via the
/// <see cref="PlaybookExecutionEngine.ExecuteChatSummarizeAsync"/> stream) emit zero
/// <c>section_*</c> events and continue to use <c>FieldDelta</c> unchanged until migrated
/// by FR-58 (task 118R). The two emission paths are mutually exclusive on a per-playbook
/// basis.
/// </para>
///
/// <para>
/// <b>ADR-033 streaming preservation</b>: Path 3 chat-summarize streaming via
/// <see cref="IOpenAiClient.StreamStructuredCompletionAsync"/> +
/// <c>IncrementalJsonParser</c> + <c>FieldDelta</c> is UNCHANGED by these event types.
/// The <c>FieldDelta</c> envelope (<see cref="Models.Ai.AnalysisChunk.Delta"/>) flows on
/// a different SSE wire (the <c>/summarize</c> endpoint emits <c>AnalysisChunk</c> JSON);
/// the section events flow on the chat SSE wire (<see cref="ChatEndpoints"/> emits
/// <see cref="Api.Ai.ChatSseEvent"/> JSON). No event-type-name collision.
/// </para>
///
/// <para>
/// <b>ADR-015 tier-1 safety</b>: the payload contains ONLY (a) section names (deterministic
/// playbook-configuration identifiers from <c>sprk_configjson.sections[].sectionName</c>),
/// (b) admin-authored display labels, (c) the section content text (which IS the response
/// being sent to the user — equivalent to a chat token, not user-uploaded content),
/// (d) optional structured-data JSON (the LLM-generated structured output, again equivalent
/// to a chat response token). The producer logs ONLY section names + counts + latency —
/// section content is NEVER duplicated into logs because the SSE wire is the canonical
/// record (the same discipline applied by the <c>output_pane</c> + <c>source_pane</c>
/// emitters).
/// </para>
///
/// <para>
/// <b>ADR-013 boundary</b>: these events are produced inside the
/// <c>Services/Ai/Chat/</c> internal orchestration boundary and emitted through the
/// existing chat SSE writer pipeline. They are NOT part of
/// <c>Services/Ai/PublicContracts/</c>.
/// </para>
/// </summary>
public static class SectionStartedSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client
    /// matches on. Bound by FR-53 — do NOT rename.
    /// </summary>
    public const string EventType = "section_started";
}

/// <summary>
/// Structured data payload for <c>section_started</c> SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="SectionName">
/// Stable section identifier from the composite node's
/// <c>sprk_configjson.sections[].sectionName</c>. Load-bearing correlation key — the
/// frontend matches subsequent <c>section_data</c> / <c>section_completed</c> events on
/// this exact string (case-sensitive). ADR-015 tier-1 safe — deterministic configuration
/// identifier.
/// </param>
/// <param name="DisplayLabel">
/// Optional admin-facing display label from
/// <c>sprk_configjson.sections[].displayLabel</c>. Used by the frontend to render the
/// section header. Null when the playbook author did not supply a label.
/// </param>
/// <param name="SectionIndex">
/// Zero-based index of this section within the composite payload's
/// <c>Sections</c> list (in completion order, not declaration order). Useful for the
/// frontend's progress-indicator display.
/// </param>
/// <param name="TotalSections">
/// Total number of sections in the composite payload. Lets the frontend render
/// "section X of Y" progress without waiting for the terminal event.
/// </param>
public sealed record SectionStartedSseEventData(
    [property: JsonPropertyName("sectionName")] string SectionName,
    [property: JsonPropertyName("displayLabel")] string? DisplayLabel,
    [property: JsonPropertyName("sectionIndex")] int SectionIndex,
    [property: JsonPropertyName("totalSections")] int TotalSections);

/// <summary>
/// SSE event emitted when section content becomes available (incremental or consolidated).
/// FR-53 / chat-routing-redesign-r1 task 114a.
///
/// <para>
/// <b>Phase A (114a)</b>: one consolidated emission per section, carrying the section's
/// full text + structured data. The composite executor today produces non-streaming
/// section results; incremental sub-event streaming is a downstream task (when individual
/// composite-feeding nodes start emitting partial outputs).
/// </para>
/// </summary>
public static class SectionDataSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client
    /// matches on. Bound by FR-53 — do NOT rename.
    /// </summary>
    public const string EventType = "section_data";
}

/// <summary>
/// Structured data payload for <c>section_data</c> SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="SectionName">
/// Stable section identifier — matches the <c>SectionName</c> from the corresponding
/// <c>section_started</c> event. The frontend uses this to route the content to the
/// correct section container.
/// </param>
/// <param name="TextDelta">
/// Text content for the section. In Phase A this is the section's full consolidated text
/// (single emission). In future incremental phases this may be a partial token chunk to
/// append to a buffer the frontend maintains. Null when the section produces only
/// structured data.
/// </param>
/// <param name="StructuredData">
/// Optional structured-data JSON payload for the section. Mirrors the
/// <see cref="Nodes.CompositeSectionResult.StructuredData"/> from the executor output.
/// Null when the section produces only text. ADR-015 tier-1 safe — this IS the response
/// being sent to the chat, not internal diagnostic state.
/// </param>
public sealed record SectionDataSseEventData(
    [property: JsonPropertyName("sectionName")] string SectionName,
    [property: JsonPropertyName("textDelta")] string? TextDelta,
    [property: JsonPropertyName("structuredData")] JsonElement? StructuredData);

/// <summary>
/// SSE event emitted when a section's composition is finalized.
/// FR-53 / chat-routing-redesign-r1 task 114a.
/// </summary>
public static class SectionCompletedSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client
    /// matches on. Bound by FR-53 — do NOT rename.
    /// </summary>
    public const string EventType = "section_completed";
}

/// <summary>
/// Structured data payload for <c>section_completed</c> SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="SectionName">
/// Stable section identifier — matches the <c>SectionName</c> from the corresponding
/// <c>section_started</c> + <c>section_data</c> events.
/// </param>
/// <param name="FinalText">
/// Final consolidated text for the section (idempotent re-emission of the section's
/// text — lets the frontend treat <c>section_completed</c> as a single-event update if
/// it dropped the intermediate <c>section_data</c>). Null when the section produced
/// only structured data.
/// </param>
/// <param name="FinalStructuredData">
/// Final consolidated structured data for the section (idempotent re-emission).
/// Null when the section produced only text.
/// </param>
/// <param name="SourceNodeId">
/// ID of the upstream Action node that produced this section's content. Optional;
/// useful for ops debugging without exposing internal section-text content. ADR-015
/// tier-1 safe — deterministic node identifier.
/// </param>
public sealed record SectionCompletedSseEventData(
    [property: JsonPropertyName("sectionName")] string SectionName,
    [property: JsonPropertyName("finalText")] string? FinalText,
    [property: JsonPropertyName("finalStructuredData")] JsonElement? FinalStructuredData,
    [property: JsonPropertyName("sourceNodeId")] Guid? SourceNodeId);
