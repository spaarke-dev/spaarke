using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Sse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="R2SseEventEmitter"/>.
///
/// Tests verify:
///   - Correct SSE event type string in the emitted <see cref="ChatSseEvent.Type"/> field.
///   - Correct JSON payload shape for each R2 event type.
///   - Events are emitted at the correct lifecycle point (during-stream vs post-stream).
///   - Validation failures are silently swallowed (no exception, no write).
///   - R1 events are not affected (the emitter only touches R2 event types).
///
/// The writer delegate captures emitted events into a list so the tests can assert
/// payload shape without a real HTTP response stream.
///
/// Task 070 (2026-05-31): class-level trait "repaired" reflects the passing tests.
/// `EmitCapabilityChangeAsync_OmitsRetryAfterSecondsWhenNull` carries its own
/// "real-bug-pending-fix" trait (RB-T070-02) and Skip until production omits
/// null-valued optional properties from the SSE payload.
/// </summary>
[Trait("status", "repaired")]
public class R2SseEventEmitterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // -------------------------------------------------------------------------
    // Shared test infrastructure
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="R2SseEventEmitter"/> whose writer captures events into
    /// <paramref name="captured"/> instead of writing to an HTTP response stream.
    /// </summary>
    private static R2SseEventEmitter CreateEmitter(List<ChatSseEvent> captured)
    {
        Task Writer(ChatSseEvent evt, CancellationToken ct)
        {
            captured.Add(evt);
            return Task.CompletedTask;
        }

        return new R2SseEventEmitter(Writer, NullLogger.Instance);
    }

    /// <summary>
    /// Deserialises the <see cref="ChatSseEvent.Data"/> field of the captured event
    /// into a <see cref="JsonDocument"/> so individual properties can be inspected.
    /// </summary>
    private static JsonElement GetDataElement(ChatSseEvent evt)
    {
        var json = JsonSerializer.Serialize(evt.Data, JsonOptions);
        return JsonDocument.Parse(json).RootElement;
    }

    // -------------------------------------------------------------------------
    // workspace_widget
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitWorkspaceWidgetAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitWorkspaceWidgetAsync(
            widgetId: "widget-001",
            widgetType: "document-preview",
            payload: new { documentId = "doc-1" },
            priority: 1);

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.WorkspaceWidget);
    }

    [Fact]
    public async Task EmitWorkspaceWidgetAsync_SerializesRequiredFields()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitWorkspaceWidgetAsync(
            widgetId: "widget-abc",
            widgetType: "action-panel",
            payload: new { action = "approve" },
            priority: 3,
            tabId: "tab-1");

        var data = GetDataElement(captured[0]);

        data.GetProperty("widgetId").GetString().Should().Be("widget-abc");
        data.GetProperty("widgetType").GetString().Should().Be("action-panel");
        data.GetProperty("priority").GetInt32().Should().Be(3);
        data.GetProperty("tabId").GetString().Should().Be("tab-1");
    }

    [Fact]
    public async Task EmitWorkspaceWidgetAsync_ContentIsNull()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitWorkspaceWidgetAsync("w1", "suggestion-list", new { }, 2);

        captured[0].Content.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // context_update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitContextUpdateAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitContextUpdateAsync(
            contextType: "document",
            contextId: "ctx-001",
            delta: new { changedField = "title" },
            confidence: 0.9);

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.ContextUpdate);
    }

    [Fact]
    public async Task EmitContextUpdateAsync_SerializesRequiredFields()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitContextUpdateAsync("entity", "ctx-xyz", new { }, 0.75);

        var data = GetDataElement(captured[0]);

        data.GetProperty("contextType").GetString().Should().Be("entity");
        data.GetProperty("contextId").GetString().Should().Be("ctx-xyz");
        data.GetProperty("confidence").GetDouble().Should().BeApproximately(0.75, 0.001);
    }

    // -------------------------------------------------------------------------
    // context_highlight
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitContextHighlightAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        var highlights = new List<RangeHighlight>
        {
            new(StartOffset: 100, EndOffset: 200)
        };

        await emitter.EmitContextHighlightAsync(
            documentId: "doc-1",
            highlights: highlights,
            highlightType: "cited");

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.ContextHighlight);
    }

    [Fact]
    public async Task EmitContextHighlightAsync_SerializesHighlightsArray()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        var highlights = new List<RangeHighlight>
        {
            new(50, 150, "cited passage"),
            new(300, 400)
        };

        await emitter.EmitContextHighlightAsync("doc-abc", highlights, "relevant");

        var data = GetDataElement(captured[0]);

        data.GetProperty("documentId").GetString().Should().Be("doc-abc");
        data.GetProperty("highlightType").GetString().Should().Be("relevant");

        var arr = data.GetProperty("highlights");
        arr.GetArrayLength().Should().Be(2);
        arr[0].GetProperty("startOffset").GetInt32().Should().Be(50);
        arr[0].GetProperty("endOffset").GetInt32().Should().Be(150);
    }

    // -------------------------------------------------------------------------
    // workspace_action
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitWorkspaceActionAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitWorkspaceActionAsync(
            actionId: "act-001",
            actionType: "navigate",
            label: "Go to Document",
            requiresConfirmation: false);

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.WorkspaceAction);
    }

    [Fact]
    public async Task EmitWorkspaceActionAsync_SerializesRequiredFields()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitWorkspaceActionAsync(
            actionId: "act-999",
            actionType: "open-document",
            label: "Open Contract",
            requiresConfirmation: true,
            targetWidgetId: "widget-preview",
            parameters: new { documentId = "doc-99" });

        var data = GetDataElement(captured[0]);

        data.GetProperty("actionId").GetString().Should().Be("act-999");
        data.GetProperty("actionType").GetString().Should().Be("open-document");
        data.GetProperty("label").GetString().Should().Be("Open Contract");
        data.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
        data.GetProperty("targetWidgetId").GetString().Should().Be("widget-preview");
    }

    // -------------------------------------------------------------------------
    // capability_change
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitCapabilityChangeAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitCapabilityChangeAsync("search", "available");

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.CapabilityChange);
    }

    [Fact]
    public async Task EmitCapabilityChangeAsync_SerializesCapabilityAndStatus()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitCapabilityChangeAsync("summarize", "degraded", retryAfterSeconds: 30);

        var data = GetDataElement(captured[0]);

        data.GetProperty("capability").GetString().Should().Be("summarize");
        data.GetProperty("status").GetString().Should().Be("degraded");
        data.GetProperty("retryAfterSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task EmitCapabilityChangeAsync_OmitsRetryAfterSecondsWhenNull()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitCapabilityChangeAsync("cite", "unavailable");

        var data = GetDataElement(captured[0]);

        // retryAfterSeconds is optional and null — should not appear in the JSON output.
        data.TryGetProperty("retryAfterSeconds", out _).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // safety_annotation — MUST be emitted post-stream only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitSafetyAnnotationAsync_SetsCorrectEventType()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitSafetyAnnotationAsync(
            severity: "info",
            category: "groundedness",
            action: "logged",
            userMessage: "Response is fully grounded in the provided documents.");

        captured.Should().ContainSingle();
        captured[0].Type.Should().Be(ChatSseR2EventTypes.SafetyAnnotation);
    }

    [Fact]
    public async Task EmitSafetyAnnotationAsync_SerializesRequiredFields()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        await emitter.EmitSafetyAnnotationAsync(
            severity: "warning",
            category: "content-policy",
            action: "filtered",
            userMessage: "Part of the response was filtered.");

        var data = GetDataElement(captured[0]);

        data.GetProperty("severity").GetString().Should().Be("warning");
        data.GetProperty("category").GetString().Should().Be("content-policy");
        data.GetProperty("action").GetString().Should().Be("filtered");
        data.GetProperty("userMessage").GetString().Should().Be("Part of the response was filtered.");
    }

    [Fact]
    public async Task EmitSafetyAnnotationAsync_SerializesGroundednessWhenProvided()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        var groundedness = new SafetyGroundedness(Score: 0.82, Rationale: "All claims verified.");
        var citations = new SafetyCitations(
            Verified: new[] { "cit-1", "cit-2" },
            Unverified: new[] { "cit-3" },
            Partial: Array.Empty<string>());

        await emitter.EmitSafetyAnnotationAsync(
            severity: "info",
            category: "groundedness",
            action: "logged",
            userMessage: "Groundedness check passed.",
            groundedness: groundedness,
            citations: citations);

        var data = GetDataElement(captured[0]);

        var g = data.GetProperty("groundedness");
        g.GetProperty("score").GetDouble().Should().BeApproximately(0.82, 0.001);
        g.GetProperty("rationale").GetString().Should().Be("All claims verified.");

        var cit = data.GetProperty("citations");
        cit.GetProperty("verified").GetArrayLength().Should().Be(2);
        cit.GetProperty("unverified").GetArrayLength().Should().Be(1);
        cit.GetProperty("partial").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Verifies the post-stream contract: safety_annotation must be emitted AFTER the
    /// last token. This test simulates the correct ordering by checking that when the caller
    /// emits token events via the writer first and safety_annotation last, both arrive in order.
    ///
    /// The actual ordering enforcement is the caller's responsibility (ChatEndpoints.SendMessageAsync);
    /// this test documents and verifies the expected sequence.
    /// </summary>
    [Fact]
    public async Task EmitSafetyAnnotationAsync_IsEmittedAfterLastToken_WhenCallerRespectsOrdering()
    {
        var emittedTypes = new List<string>();

        Task Writer(ChatSseEvent evt, CancellationToken ct)
        {
            emittedTypes.Add(evt.Type);
            return Task.CompletedTask;
        }

        var emitter = new R2SseEventEmitter(Writer, NullLogger.Instance);

        // Simulate the caller emitting R1 token events first (via WriteChatSSEAsync directly),
        // then calling the R2 emitter post-stream.
        emittedTypes.Add("token");         // R1 — emitted by ChatEndpoints directly
        emittedTypes.Add("typing_end");    // R1 — emitted by ChatEndpoints directly

        // Post-stream R2 safety_annotation
        await emitter.EmitSafetyAnnotationAsync(
            severity: "info",
            category: "groundedness",
            action: "logged",
            userMessage: "Grounded.");

        emittedTypes.Should().HaveCount(3);
        emittedTypes[0].Should().Be("token");
        emittedTypes[1].Should().Be("typing_end");
        emittedTypes[2].Should().Be(ChatSseR2EventTypes.SafetyAnnotation);
    }

    // -------------------------------------------------------------------------
    // Validation failure path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitAsync_SkipsEvent_WhenPayloadFailsValidation()
    {
        // workspace_widget requires widgetId, widgetType, payload, priority (1-10).
        // Passing priority=99 will fail the schema validator's range check.
        // We test this by directly constructing an emitter and calling EmitWorkspaceWidgetAsync
        // with an invalid priority value — the validator should reject it and nothing is written.

        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        // priority must be 1–10; 99 is out of range.
        await emitter.EmitWorkspaceWidgetAsync(
            widgetId: "w1",
            widgetType: "document-preview",
            payload: new { },
            priority: 99);   // invalid — out of range

        // The validator rejects the payload, so no event should be written.
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task EmitAsync_DoesNotThrow_WhenPayloadIsInvalid()
    {
        var captured = new List<ChatSseEvent>();
        var emitter = CreateEmitter(captured);

        // Validation failure must be silent — no exception reaches the caller.
        var act = async () => await emitter.EmitWorkspaceWidgetAsync(
            widgetId: "w1",
            widgetType: "document-preview",
            payload: new { },
            priority: 0);   // invalid — below minimum (1)

        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // R1 event backward compatibility
    // -------------------------------------------------------------------------

    [Fact]
    public async Task R2Emitter_DoesNotInterfereWithR1Events()
    {
        // Verify that creating an R2 emitter and calling its methods does not affect
        // events written via the writer delegate by other callers (R1 path).

        var captured = new List<ChatSseEvent>();

        Task Writer(ChatSseEvent evt, CancellationToken ct)
        {
            captured.Add(evt);
            return Task.CompletedTask;
        }

        var emitter = new R2SseEventEmitter(Writer, NullLogger.Instance);

        // Simulate R1 events written directly through the same writer delegate.
        await Writer(new ChatSseEvent("token", "Hello"), CancellationToken.None);
        await Writer(new ChatSseEvent("typing_end", null), CancellationToken.None);

        // R2 event written via emitter.
        await emitter.EmitCapabilityChangeAsync("search", "available");

        // R1 done event.
        await Writer(new ChatSseEvent("done", null), CancellationToken.None);

        // Verify both R1 and R2 events appear in order, intact.
        captured.Should().HaveCount(4);
        captured[0].Type.Should().Be("token");
        captured[1].Type.Should().Be("typing_end");
        captured[2].Type.Should().Be(ChatSseR2EventTypes.CapabilityChange);
        captured[3].Type.Should().Be("done");
    }

    // -------------------------------------------------------------------------
    // CreateR2Emitter factory method
    // -------------------------------------------------------------------------


    [Fact]
    public async Task CreateR2Emitter_EmitterWritesToProvidedDelegate()
    {
        var captured = new List<ChatSseEvent>();
        Task Writer(ChatSseEvent evt, CancellationToken ct) { captured.Add(evt); return Task.CompletedTask; }

        var emitter = ChatEndpoints.CreateR2Emitter(Writer, NullLogger.Instance);

        await emitter.EmitCapabilityChangeAsync("memory", "available");

        captured.Should().ContainSingle()
            .Which.Type.Should().Be(ChatSseR2EventTypes.CapabilityChange);
    }
}
