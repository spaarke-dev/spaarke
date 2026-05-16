using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// SSE event emitted when the AI response references a specific excerpt in the source pane,
/// instructing the frontend to scroll to and highlight that excerpt in the source viewer.
///
/// Wire format:
/// <code>
/// data: {"type":"source_highlight","content":null,"data":{"citationId":"...","sourceWidgetId":"...","highlightStart":0,"highlightEnd":42}}
///
/// </code>
///
/// The <c>data</c> field contains the <see cref="SourceHighlightSseEventData"/> payload.
/// Character offsets (<see cref="SourceHighlightSseEventData.HighlightStart"/> and
/// <see cref="SourceHighlightSseEventData.HighlightEnd"/>) are zero-based UTF-16 character
/// indices into the source content as rendered by the source pane widget.
///
/// ADR-013: Extends the existing BFF streaming pipeline. Do not introduce a separate streaming service.
/// ADR-019: Serialization failures emit a terminal error SSE event (type: error), not a silent drop.
/// </summary>
public static class SourceHighlightSseEvent
{
    /// <summary>
    /// SSE event type string. Must match the "event:" header the frontend SSE client matches on.
    /// </summary>
    public const string EventType = "source_highlight";
}

/// <summary>
/// Structured data payload for "source_highlight" SSE events.
/// Emitted as the <c>data</c> field of a <see cref="Sprk.Bff.Api.Api.Ai.ChatSseEvent"/>.
/// </summary>
/// <param name="CitationId">
/// Citation identifier linking this highlight to a [N] marker in the AI response text.
/// Enables the frontend to correlate the highlighted excerpt with the inline citation.
/// </param>
/// <param name="SourceWidgetId">
/// Identifier of the source pane widget instance that contains the excerpt to highlight.
/// The frontend uses this to target the correct viewer when multiple sources are open.
/// </param>
/// <param name="HighlightStart">
/// Zero-based character offset of the start of the excerpt to highlight (inclusive).
/// </param>
/// <param name="HighlightEnd">
/// Zero-based character offset of the end of the excerpt to highlight (exclusive).
/// </param>
public record SourceHighlightSseEventData(
    [property: JsonPropertyName("citationId")] string CitationId,
    [property: JsonPropertyName("sourceWidgetId")] string SourceWidgetId,
    [property: JsonPropertyName("highlightStart")] int HighlightStart,
    [property: JsonPropertyName("highlightEnd")] int HighlightEnd);
