using System.Text.Json;
using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// Static factory for constructing the three pane-control SSE events:
/// <c>output_pane</c>, <c>source_pane</c>, and <c>source_highlight</c>.
///
/// Each factory method returns a <see cref="ChatSseEvent"/> ready to emit through the
/// existing <c>ChatEndpoints.WriteChatSSEAsync</c> pipeline. The structured payload is
/// placed in the <c>Data</c> field; the <c>Type</c> field carries the event type string
/// that the frontend SSE client matches on.
///
/// Error handling (ADR-019): if <paramref name="widgetData"/> cannot be serialized to
/// <see cref="JsonElement"/>, the factory returns a terminal error <see cref="ChatSseEvent"/>
/// (type: "error") rather than propagating the exception or silently dropping the event.
/// This ensures the SSE stream always terminates with a visible signal.
/// </summary>
public static class ChatSseEventFactory
{
    /// <summary>
    /// JSON serializer options used for all factory serializations.
    /// CamelCase naming policy matches the existing ChatEndpoints.WriteChatSSEAsync configuration.
    /// WhenWritingNull omits optional null fields (e.g., CitationId on SourcePaneSseEventData).
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates an "output_pane" <see cref="ChatSseEvent"/> that instructs the frontend
    /// to render the specified widget in the center output pane.
    /// </summary>
    /// <param name="widgetType">
    /// Output widget type identifier (maps to <c>OutputWidgetType</c> in the frontend registry).
    /// </param>
    /// <param name="widgetData">
    /// Widget data object to serialize as the widget payload. The BFF serializes this to
    /// raw JSON (<see cref="JsonElement"/>) and relays it verbatim — the frontend widget owns
    /// the schema interpretation.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "output_pane" and <c>Data</c> containing
    /// the <see cref="OutputPaneSseEventData"/> payload, or a terminal error event if
    /// <paramref name="widgetData"/> cannot be serialized (ADR-019).
    /// </returns>
    public static ChatSseEvent CreateOutputPaneEvent(string widgetType, object widgetData)
    {
        try
        {
            var widgetDataElement = SerializeToJsonElement(widgetData);
            var data = new OutputPaneSseEventData(widgetType, widgetDataElement);
            return new ChatSseEvent(OutputPaneSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(OutputPaneSseEvent.EventType, ex);
        }
    }

    /// <summary>
    /// Creates a "source_pane" <see cref="ChatSseEvent"/> that instructs the frontend
    /// to render the specified source widget in the right-side source pane.
    /// </summary>
    /// <param name="widgetType">
    /// Source widget type identifier (maps to <c>SourceWidgetType</c> in the frontend registry).
    /// </param>
    /// <param name="widgetData">
    /// Widget data object to serialize as the widget payload. Relayed verbatim as raw JSON.
    /// </param>
    /// <param name="citationId">
    /// Optional citation ID linking this source pane content to a [N] marker in the
    /// response text. Pass <c>null</c> when no citation association is needed.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "source_pane" and <c>Data</c> containing
    /// the <see cref="SourcePaneSseEventData"/> payload, or a terminal error event if
    /// <paramref name="widgetData"/> cannot be serialized (ADR-019).
    /// </returns>
    public static ChatSseEvent CreateSourcePaneEvent(
        string widgetType,
        object widgetData,
        string? citationId = null)
    {
        try
        {
            var widgetDataElement = SerializeToJsonElement(widgetData);
            var data = new SourcePaneSseEventData(widgetType, widgetDataElement, citationId);
            return new ChatSseEvent(SourcePaneSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(SourcePaneSseEvent.EventType, ex);
        }
    }

    /// <summary>
    /// Creates a "source_highlight" <see cref="ChatSseEvent"/> that instructs the frontend
    /// to scroll to and highlight the specified excerpt in the source pane viewer.
    /// </summary>
    /// <param name="citationId">
    /// Citation identifier linking this highlight to a [N] marker in the AI response text.
    /// </param>
    /// <param name="sourceWidgetId">
    /// Identifier of the source pane widget instance containing the excerpt.
    /// </param>
    /// <param name="start">Zero-based character offset of the highlight start (inclusive).</param>
    /// <param name="end">Zero-based character offset of the highlight end (exclusive).</param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "source_highlight" and <c>Data</c>
    /// containing the <see cref="SourceHighlightSseEventData"/> payload.
    /// </returns>
    public static ChatSseEvent CreateSourceHighlightEvent(
        string citationId,
        string sourceWidgetId,
        int start,
        int end)
    {
        var data = new SourceHighlightSseEventData(citationId, sourceWidgetId, start, end);
        return new ChatSseEvent(SourceHighlightSseEvent.EventType, null, data);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to a <see cref="JsonElement"/> using the shared
    /// camelCase serializer options.
    ///
    /// Round-trips through JSON bytes: serialize object → parse bytes → return element.
    /// The resulting <see cref="JsonElement"/> is independent of the original object's lifetime.
    /// </summary>
    /// <exception cref="JsonException">Thrown if <paramref name="value"/> cannot be serialized.</exception>
    private static JsonElement SerializeToJsonElement(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    /// <summary>
    /// Creates a terminal error <see cref="ChatSseEvent"/> (type: "error") when widget data
    /// serialization fails. Satisfies ADR-019: do not silently drop events or propagate
    /// serialization exceptions to the client connection.
    /// </summary>
    private static ChatSseEvent CreateSerializationErrorEvent(string failedEventType, JsonException ex)
    {
        var errorMessage = $"Failed to serialize {failedEventType} widget data: {ex.Message}";
        return new ChatSseEvent("error", errorMessage);
    }
}
