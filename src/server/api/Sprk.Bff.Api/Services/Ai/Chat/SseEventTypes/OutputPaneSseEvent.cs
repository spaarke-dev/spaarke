using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// SSE event emitted when the AI determines the primary structured output to display
/// in the center output pane of the three-pane layout.
///
/// Wire format:
/// <code>
/// data: {"type":"output_pane","content":null,"data":{"widgetType":"...","widgetData":{...}}}
///
/// </code>
///
/// The <c>data</c> field contains the <see cref="OutputPaneSseEventData"/> payload.
/// The BFF relays the AI tool output verbatim — <see cref="OutputPaneSseEventData.WidgetData"/>
/// is raw JSON (<see cref="JsonElement"/>) because the BFF does not know the frontend widget schema.
///
/// ADR-013: Extends the existing BFF streaming pipeline. Do not introduce a separate streaming service.
/// ADR-019: If widget data cannot be serialized, emit a terminal error SSE event (type: error).
/// </summary>
public static class OutputPaneSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client matches on.
    /// </summary>
    public const string EventType = "output_pane";
}

/// <summary>
/// Structured data payload for "output_pane" SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Sprk.Bff.Api.Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="WidgetType">
/// The output widget type identifier (e.g., "table", "timeline", "chart").
/// Maps to <c>OutputWidgetType</c> string values in the frontend registry.
/// </param>
/// <param name="WidgetData">
/// Raw JSON payload for the widget, relayed verbatim from the AI tool output.
/// The BFF does not interpret this data — the frontend widget schema owns the shape.
/// </param>
public record OutputPaneSseEventData(
    [property: JsonPropertyName("widgetType")] string WidgetType,
    [property: JsonPropertyName("widgetData")] JsonElement WidgetData);
