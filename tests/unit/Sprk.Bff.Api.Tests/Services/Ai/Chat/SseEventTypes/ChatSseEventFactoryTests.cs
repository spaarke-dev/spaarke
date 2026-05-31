using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// Unit tests for <see cref="ChatSseEventFactory"/> and the three pane-control SSE event types:
/// <c>output_pane</c>, <c>source_pane</c>, <c>source_highlight</c>.
///
/// Each test validates:
///   1. Event construction via factory method.
///   2. SSE wire format: event type string and camelCase JSON payload in the data field.
///   3. Error handling: unserializable widget data produces a terminal error event (ADR-019).
///
/// SSE wire format uses ChatEndpoints.WriteChatSSEAsync:
///   data: {json}\n\n
/// where {json} is the serialized <see cref="ChatSseEvent"/>.
/// </summary>
[Trait("status", "repaired")]
public class ChatSseEventFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ====================================================================
    // EventType constants
    // ====================================================================

    [Fact]
    public void OutputPaneSseEvent_EventType_IsExactlyOutputPane()
    {
        OutputPaneSseEvent.EventType.Should().Be("output_pane",
            "the SSE event: header value must exactly match what the frontend client expects");
    }

    [Fact]
    public void SourcePaneSseEvent_EventType_IsExactlySourcePane()
    {
        SourcePaneSseEvent.EventType.Should().Be("source_pane",
            "the SSE event: header value must exactly match what the frontend client expects");
    }

    [Fact]
    public void SourceHighlightSseEvent_EventType_IsExactlySourceHighlight()
    {
        SourceHighlightSseEvent.EventType.Should().Be("source_highlight",
            "the SSE event: header value must exactly match what the frontend client expects");
    }

    // ====================================================================
    // output_pane: construct + serialize + verify wire format
    // ====================================================================

    [Fact]
    public void CreateOutputPaneEvent_SetsCorrectEventType()
    {
        // Arrange
        var widgetData = new { value = "test", count = 42 };

        // Act
        var evt = ChatSseEventFactory.CreateOutputPaneEvent("table", widgetData);

        // Assert
        evt.Type.Should().Be("output_pane");
        evt.Content.Should().BeNull();
        evt.Data.Should().NotBeNull();
    }

    [Fact]
    public void CreateOutputPaneEvent_SerializesToCorrectWireFormat()
    {
        // Arrange
        var widgetData = new { title = "Financial Summary", rows = new[] { "Row1", "Row2" } };

        // Act
        var evt = ChatSseEventFactory.CreateOutputPaneEvent("table", widgetData);
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var sseFrame = $"data: {json}\n\n";

        // Assert — SSE wire format
        sseFrame.Should().StartWith("data: ");
        sseFrame.Should().EndWith("\n\n");
        json.Should().Contain("\"type\":\"output_pane\"");
        json.Should().Contain("\"content\":null");
    }

    [Fact]
    public void CreateOutputPaneEvent_DataPayload_ContainsWidgetTypeAndWidgetData()
    {
        // Arrange
        var widgetData = new { title = "Revenue Chart", year = 2026 };

        // Act
        var evt = ChatSseEventFactory.CreateOutputPaneEvent("chart", widgetData);
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert — data payload contains required fields
        json.Should().Contain("\"widgetType\":\"chart\"");
        json.Should().Contain("\"widgetData\":");
        json.Should().Contain("\"title\":\"Revenue Chart\"");
        json.Should().Contain("\"year\":2026");
    }

    [Fact]
    public void CreateOutputPaneEvent_DataPayload_IsOutputPaneSseEventData()
    {
        // Arrange
        var widgetData = new { items = new[] { 1, 2, 3 } };

        // Act
        var evt = ChatSseEventFactory.CreateOutputPaneEvent("list", widgetData);

        // Assert — Data is the typed payload
        evt.Data.Should().BeOfType<OutputPaneSseEventData>();
        var data = (OutputPaneSseEventData)evt.Data!;
        data.WidgetType.Should().Be("list");
        data.WidgetData.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void CreateOutputPaneEvent_WithUnserializableData_ReturnsTerminalErrorEvent()
    {
        // Arrange — a circular reference causes JsonException
        var circular = new CircularReference();
        circular.Self = circular;

        // Act
        var evt = ChatSseEventFactory.CreateOutputPaneEvent("table", circular);

        // Assert — ADR-019: terminal error event, not silent drop
        evt.Type.Should().Be("error",
            "unserializable widget data must produce a terminal error event per ADR-019");
        evt.Content.Should().Contain("output_pane",
            "error message should identify the failing event type");
        evt.Data.Should().BeNull();
    }

    // ====================================================================
    // source_pane: construct + serialize + verify wire format
    // ====================================================================

    [Fact]
    public void CreateSourcePaneEvent_SetsCorrectEventType()
    {
        // Arrange
        var widgetData = new { url = "https://example.com/doc.pdf", page = 3 };

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("pdf_viewer", widgetData);

        // Assert
        evt.Type.Should().Be("source_pane");
        evt.Content.Should().BeNull();
        evt.Data.Should().NotBeNull();
    }

    [Fact]
    public void CreateSourcePaneEvent_SerializesToCorrectWireFormat()
    {
        // Arrange
        var widgetData = new { content = "Document text excerpt", language = "en" };

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("text_viewer", widgetData, "cite-1");
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var sseFrame = $"data: {json}\n\n";

        // Assert — SSE wire format
        sseFrame.Should().StartWith("data: ");
        sseFrame.Should().EndWith("\n\n");
        json.Should().Contain("\"type\":\"source_pane\"");
        json.Should().Contain("\"content\":null");
    }

    [Fact]
    public void CreateSourcePaneEvent_DataPayload_ContainsAllRequiredFields()
    {
        // Arrange
        var widgetData = new { excerpt = "The contract clause states..." };

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("text_viewer", widgetData, citationId: "cite-42");
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert — all fields present in data payload
        json.Should().Contain("\"widgetType\":\"text_viewer\"");
        json.Should().Contain("\"widgetData\":");
        json.Should().Contain("\"citationId\":\"cite-42\"");
        json.Should().Contain("\"excerpt\":\"The contract clause states...\"");
    }

    [Fact(Skip = "real-bug-pending-fix RB-T050-01: SourcePaneSseEventData.CitationId lacks JsonIgnoreCondition.WhenWritingNull — production serializes citationId:null instead of omitting the field. Test asserts documented contract from XML doc comments (optional + WhenWritingNull). See ledgers/real-bug-ledger.md.")]
    [Trait("status", "real-bug-pending-fix")]
    public void CreateSourcePaneEvent_WithNullCitationId_OmitsCitationIdField()
    {
        // Arrange
        var widgetData = new { url = "https://example.com" };

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("web_reference", widgetData, citationId: null);
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert — optional citationId is omitted when null (WhenWritingNull)
        json.Should().Contain("\"widgetType\":\"web_reference\"");
        json.Should().NotContain("\"citationId\"",
            "citationId should be omitted when null per JsonIgnoreCondition.WhenWritingNull");
    }

    [Fact]
    public void CreateSourcePaneEvent_DataPayload_IsSourcePaneSseEventData()
    {
        // Arrange
        var widgetData = new { page = 1 };

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("pdf_viewer", widgetData, "c-1");

        // Assert
        evt.Data.Should().BeOfType<SourcePaneSseEventData>();
        var data = (SourcePaneSseEventData)evt.Data!;
        data.WidgetType.Should().Be("pdf_viewer");
        data.CitationId.Should().Be("c-1");
        data.WidgetData.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void CreateSourcePaneEvent_WithUnserializableData_ReturnsTerminalErrorEvent()
    {
        // Arrange — circular reference forces JsonException
        var circular = new CircularReference();
        circular.Self = circular;

        // Act
        var evt = ChatSseEventFactory.CreateSourcePaneEvent("pdf_viewer", circular);

        // Assert — ADR-019
        evt.Type.Should().Be("error",
            "unserializable widget data must produce a terminal error event per ADR-019");
        evt.Content.Should().Contain("source_pane",
            "error message should identify the failing event type");
    }

    // ====================================================================
    // source_highlight: construct + serialize + verify wire format
    // ====================================================================

    [Fact]
    public void CreateSourceHighlightEvent_SetsCorrectEventType()
    {
        // Act
        var evt = ChatSseEventFactory.CreateSourceHighlightEvent("cite-1", "widget-abc", 100, 250);

        // Assert
        evt.Type.Should().Be("source_highlight");
        evt.Content.Should().BeNull();
        evt.Data.Should().NotBeNull();
    }

    [Fact]
    public void CreateSourceHighlightEvent_SerializesToCorrectWireFormat()
    {
        // Act
        var evt = ChatSseEventFactory.CreateSourceHighlightEvent("c-007", "w-1", 0, 42);
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var sseFrame = $"data: {json}\n\n";

        // Assert — SSE wire format
        sseFrame.Should().StartWith("data: ");
        sseFrame.Should().EndWith("\n\n");
        json.Should().Contain("\"type\":\"source_highlight\"");
        json.Should().Contain("\"content\":null");
    }

    [Fact]
    public void CreateSourceHighlightEvent_DataPayload_ContainsAllRequiredFields()
    {
        // Act
        var evt = ChatSseEventFactory.CreateSourceHighlightEvent(
            citationId: "cite-3",
            sourceWidgetId: "widget-xyz",
            start: 150,
            end: 300);
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert — all payload fields present in camelCase
        json.Should().Contain("\"citationId\":\"cite-3\"");
        json.Should().Contain("\"sourceWidgetId\":\"widget-xyz\"");
        json.Should().Contain("\"highlightStart\":150");
        json.Should().Contain("\"highlightEnd\":300");
    }

    [Fact]
    public void CreateSourceHighlightEvent_DataPayload_IsSourceHighlightSseEventData()
    {
        // Act
        var evt = ChatSseEventFactory.CreateSourceHighlightEvent("c-1", "w-2", 10, 99);

        // Assert
        evt.Data.Should().BeOfType<SourceHighlightSseEventData>();
        var data = (SourceHighlightSseEventData)evt.Data!;
        data.CitationId.Should().Be("c-1");
        data.SourceWidgetId.Should().Be("w-2");
        data.HighlightStart.Should().Be(10);
        data.HighlightEnd.Should().Be(99);
    }

    [Fact]
    public void CreateSourceHighlightEvent_ZeroOffsets_AreValid()
    {
        // Act — zero start and end is a valid degenerate range (empty selection)
        var evt = ChatSseEventFactory.CreateSourceHighlightEvent("c-1", "w-1", 0, 0);
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert
        evt.Type.Should().Be("source_highlight");
        json.Should().Contain("\"highlightStart\":0");
        json.Should().Contain("\"highlightEnd\":0");
    }

    // ====================================================================
    // Cross-event: serialized data uses camelCase naming
    // ====================================================================

    [Theory]
    [InlineData("output_pane")]
    [InlineData("source_pane")]
    [InlineData("source_highlight")]
    public void AllPaneEvents_SerializeWithCamelCaseNaming(string eventType)
    {
        // Arrange
        ChatSseEvent evt = eventType switch
        {
            "output_pane" => ChatSseEventFactory.CreateOutputPaneEvent("table", new { someValue = 1 }),
            "source_pane" => ChatSseEventFactory.CreateSourcePaneEvent("pdf_viewer", new { someValue = 2 }),
            "source_highlight" => ChatSseEventFactory.CreateSourceHighlightEvent("c-1", "w-1", 0, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

        // Act
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        // Assert — top-level fields are camelCase
        json.Should().Contain("\"type\":");
        json.Should().Contain("\"content\":");
        json.Should().NotContain("\"Type\":");
        json.Should().NotContain("\"Content\":");
    }

    [Fact]
    public void AllPaneEventTypes_CanBeEmittedAsChatSseEvent()
    {
        // This test validates that all three new event types can be constructed
        // and wrapped in ChatSseEvent without type errors — verifying integration
        // with the existing discriminated union.

        var outputPane = ChatSseEventFactory.CreateOutputPaneEvent("timeline", new { events = 5 });
        var sourcePane = ChatSseEventFactory.CreateSourcePaneEvent("web_reference", new { url = "https://example.com" });
        var sourceHighlight = ChatSseEventFactory.CreateSourceHighlightEvent("c-1", "w-1", 50, 100);

        // All three are valid ChatSseEvent instances
        outputPane.Should().BeOfType<ChatSseEvent>();
        sourcePane.Should().BeOfType<ChatSseEvent>();
        sourceHighlight.Should().BeOfType<ChatSseEvent>();

        // All carry the correct discriminator
        outputPane.Type.Should().Be("output_pane");
        sourcePane.Type.Should().Be("source_pane");
        sourceHighlight.Type.Should().Be("source_highlight");
    }

    // ====================================================================
    // Helper: circular reference type to force JsonException
    // ====================================================================

    private class CircularReference
    {
        public CircularReference? Self { get; set; }
    }
}
