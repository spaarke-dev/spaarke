using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// SSE event emitted when the AI references a source document or external reference,
/// instructing the frontend to render it in the right-side source pane.
///
/// Wire format:
/// <code>
/// data: {"type":"source_pane","content":null,"data":{"widgetType":"...","widgetData":{...},"citationId":"..."}}
///
/// </code>
///
/// The <c>data</c> field contains the <see cref="SourcePaneSseEventData"/> payload.
/// The BFF relays the AI tool output verbatim — <see cref="SourcePaneSseEventData.WidgetData"/>
/// is raw JSON (<see cref="JsonElement"/>) because the BFF does not know the frontend widget schema.
///
/// ADR-013: Extends the existing BFF streaming pipeline. Do not introduce a separate streaming service.
/// ADR-019: If widget data cannot be serialized, emit a terminal error SSE event (type: error).
/// </summary>
public static class SourcePaneSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client matches on.
    /// </summary>
    public const string EventType = "source_pane";
}

/// <summary>
/// Structured data payload for "source_pane" SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Sprk.Bff.Api.Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="WidgetType">
/// The source widget type identifier (e.g., "pdf_viewer", "text_viewer", "web_reference").
/// Maps to <c>SourceWidgetType</c> string values in the frontend registry.
/// </param>
/// <param name="WidgetData">
/// Raw JSON payload for the source widget, relayed verbatim from the AI tool output.
/// The BFF does not interpret this data — the frontend widget schema owns the shape.
/// </param>
/// <param name="CitationId">
/// Optional citation ID linking this source pane content to a citation marker
/// in the response text (e.g., "[1]", "[2]"). When present, the frontend can
/// establish a cross-pane link between the response text and the source.
/// </param>
public record SourcePaneSseEventData(
    [property: JsonPropertyName("widgetType")] string WidgetType,
    [property: JsonPropertyName("widgetData")] JsonElement WidgetData,
    [property: JsonPropertyName("citationId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CitationId = null);
