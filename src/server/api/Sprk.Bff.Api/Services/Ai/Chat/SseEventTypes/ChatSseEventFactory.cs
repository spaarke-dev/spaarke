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
    /// Creates a "playbook_options" <see cref="ChatSseEvent"/> carrying the top-N candidate
    /// playbooks surfaced to the chat user after file-aware classification
    /// (chat-routing-redesign-r1 task 117a / FR-49).
    ///
    /// <para>
    /// The <paramref name="data"/> shape is locked by FR-49 — the test suite enforces the
    /// field set. The factory wraps the payload in the standard
    /// <see cref="ChatSseEvent"/> envelope; the existing chat SSE writer pipeline
    /// (<c>ChatEndpoints.WriteChatSSEAsync</c>) handles framing + serialization.
    /// </para>
    ///
    /// <para>
    /// <b>FR-48 invariant</b>: this event NEVER carries an auto-execute flag.
    /// <b>FR-51 invariant</b>: <see cref="PlaybookOptionsSseEventData.LibraryModalCta"/>
    /// is always <c>true</c> on the event the
    /// <see cref="PlaybookOptionsEventBuilder"/> produces.
    /// </para>
    ///
    /// <para>
    /// <b>ADR-019 error handling</b>: parity with the pane factories — on serialization
    /// failure a terminal <c>error</c> event is returned rather than throwing through the
    /// SSE connection.
    /// </para>
    /// </summary>
    /// <param name="data">
    /// The structured payload (built by <see cref="PlaybookOptionsEventBuilder"/>). Shape
    /// is locked by FR-49.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "playbook_options" and <c>Data</c>
    /// containing <paramref name="data"/>, or a terminal error event on serialization
    /// failure.
    /// </returns>
    public static ChatSseEvent CreatePlaybookOptionsEvent(PlaybookOptionsSseEventData data)
    {
        try
        {
            // Round-trip through the standard serializer to (a) validate the shape eagerly,
            // and (b) match the wire format produced by ChatEndpoints.WriteChatSSEAsync.
            // We pass the typed record straight through as Data — ChatEndpoints' default
            // serializer (camelCase, WhenWritingNull) is configured to emit the locked shape
            // verbatim.
            _ = JsonSerializer.SerializeToUtf8Bytes(data, SerializerOptions);
            return new ChatSseEvent(PlaybookOptionsSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(PlaybookOptionsSseEvent.EventType, ex);
        }
    }

    /// <summary>
    /// Creates a <c>section_started</c> <see cref="ChatSseEvent"/> announcing that a section
    /// in a composite output payload has begun composition. FR-53 /
    /// chat-routing-redesign-r1 task 114a.
    ///
    /// <para>
    /// <b>FR-53 binding</b>: <see cref="SectionStartedSseEventData.SectionName"/> is the
    /// stable correlation key; subsequent <c>section_data</c> + <c>section_completed</c>
    /// events for the same section MUST carry the same exact name. The event is emitted
    /// keyed by section name, NOT by schema position.
    /// </para>
    ///
    /// <para>
    /// <b>ADR-019 error handling</b>: parity with the pane factories — on serialization
    /// failure a terminal <c>error</c> event is returned rather than throwing through the
    /// SSE connection.
    /// </para>
    /// </summary>
    /// <param name="data">
    /// The structured payload identifying the section + its position in the composite.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "section_started" and <c>Data</c>
    /// containing <paramref name="data"/>, or a terminal error event on serialization
    /// failure.
    /// </returns>
    public static ChatSseEvent CreateSectionStartedEvent(SectionStartedSseEventData data)
    {
        try
        {
            _ = JsonSerializer.SerializeToUtf8Bytes(data, SerializerOptions);
            return new ChatSseEvent(SectionStartedSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(SectionStartedSseEvent.EventType, ex);
        }
    }

    /// <summary>
    /// Creates a <c>section_data</c> <see cref="ChatSseEvent"/> carrying section content
    /// (incremental delta or consolidated emission). FR-53 / chat-routing-redesign-r1
    /// task 114a.
    ///
    /// <para>
    /// <b>Phase A</b>: one consolidated emission per section between
    /// <c>section_started</c> and <c>section_completed</c>. Future incremental phases
    /// emit multiple deltas per section, all sharing the same <c>SectionName</c>.
    /// </para>
    /// </summary>
    /// <param name="data">
    /// The section content payload — text delta and/or structured data.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "section_data" and <c>Data</c>
    /// containing <paramref name="data"/>, or a terminal error event on serialization
    /// failure.
    /// </returns>
    public static ChatSseEvent CreateSectionDataEvent(SectionDataSseEventData data)
    {
        try
        {
            _ = JsonSerializer.SerializeToUtf8Bytes(data, SerializerOptions);
            return new ChatSseEvent(SectionDataSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(SectionDataSseEvent.EventType, ex);
        }
    }

    /// <summary>
    /// Creates a <c>section_completed</c> <see cref="ChatSseEvent"/> announcing that a
    /// section's composition is finalized. FR-53 / chat-routing-redesign-r1 task 114a.
    ///
    /// <para>
    /// <b>Idempotent re-emission</b>: this event carries the FINAL consolidated text +
    /// structured data so a frontend that dropped the intermediate <c>section_data</c>
    /// can still render the section correctly. Frontends that track incremental updates
    /// MUST treat <c>section_completed</c> as the authoritative final state.
    /// </para>
    /// </summary>
    /// <param name="data">
    /// The section completion payload — final text, structured data, and source node ID.
    /// </param>
    /// <returns>
    /// A <see cref="ChatSseEvent"/> with <c>Type</c> = "section_completed" and <c>Data</c>
    /// containing <paramref name="data"/>, or a terminal error event on serialization
    /// failure.
    /// </returns>
    public static ChatSseEvent CreateSectionCompletedEvent(SectionCompletedSseEventData data)
    {
        try
        {
            _ = JsonSerializer.SerializeToUtf8Bytes(data, SerializerOptions);
            return new ChatSseEvent(SectionCompletedSseEvent.EventType, null, data);
        }
        catch (JsonException ex)
        {
            return CreateSerializationErrorEvent(SectionCompletedSseEvent.EventType, ex);
        }
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
