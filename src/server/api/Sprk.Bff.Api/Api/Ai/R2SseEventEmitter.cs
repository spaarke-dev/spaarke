using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Infrastructure.Sse;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Emits R2 SSE event types (workspace_widget, context_update, context_highlight,
/// workspace_action, capability_change, safety_annotation) into the active HTTP response
/// stream via the same <see cref="ChatSseEvent"/> wire format used by R1 events.
///
/// Each emit method:
///   1. Serializes the strongly-typed payload to a <see cref="JsonElement"/>.
///   2. Validates the payload via <see cref="SseEventSchemaValidator.ValidateAsync"/>
///      (lightweight structural check — ADR-015: no payload content is logged on failure).
///   3. Writes <c>data: {json}\n\n</c> to the SSE response stream on success, or logs and
///      skips the event when validation fails to protect the client from malformed frames.
///
/// R1 event types (token, done, error, typing_start, typing_end, citations, suggestions,
/// plan_preview, plan_step_start, plan_step_complete) are NOT handled here — they continue
/// to be emitted via <see cref="ChatEndpoints"/> directly to preserve backward compatibility.
///
/// Lifecycle: create once per request using <see cref="ChatEndpoints.CreateR2Emitter"/>.
/// </summary>
public sealed class R2SseEventEmitter
{
    private readonly Func<ChatSseEvent, CancellationToken, Task> _writer;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Initialises the emitter with the SSE writer delegate from
    /// <see cref="ChatEndpoints.CreateSseWriter"/> and a logger for validation failures.
    /// </summary>
    /// <param name="writer">
    /// SSE write delegate produced by <see cref="ChatEndpoints.CreateSseWriter"/>.
    /// </param>
    /// <param name="logger">
    /// Logger used to record validation failures (ADR-015: payload content is never logged).
    /// Any <see cref="ILogger"/> implementation is accepted — the emitter does not require
    /// a specific category.
    /// </param>
    public R2SseEventEmitter(
        Func<ChatSseEvent, CancellationToken, Task> writer,
        ILogger logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // R2 Event Emitters
    // =========================================================================

    /// <summary>
    /// Emits a <c>workspace_widget</c> event that instructs the AI shell to render or
    /// update a widget in the Workspace pane.
    ///
    /// Lifecycle: emitted during streaming (between typing_start and typing_end) when an
    /// AI tool produces output that should be rendered as a workspace widget.
    /// </summary>
    /// <param name="widgetId">Stable identifier for the widget instance.</param>
    /// <param name="widgetType">
    /// Widget type discriminator. One of: document-preview, action-panel,
    /// suggestion-list, capability-status.
    /// </param>
    /// <param name="payload">Widget-specific data (arbitrary JSON object).</param>
    /// <param name="priority">Rendering priority 1–10 (1 = highest).</param>
    /// <param name="tabId">Optional tab ID for multi-tab workspace layouts.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitWorkspaceWidgetAsync(
        string widgetId,
        string widgetType,
        object payload,
        int priority,
        string? tabId = null,
        CancellationToken cancellationToken = default)
    {
        var data = new WorkspaceWidgetPayload(widgetId, widgetType, payload, priority, tabId);
        return EmitAsync(ChatSseR2EventTypes.WorkspaceWidget, data, cancellationToken);
    }

    /// <summary>
    /// Emits a <c>context_update</c> event that notifies the client that the agent's
    /// active context has changed (e.g. the document or entity scope shifted).
    ///
    /// Lifecycle: emitted during streaming when a tool call changes the active context.
    /// </summary>
    /// <param name="contextType">
    /// Context type discriminator. One of: document, entity, conversation, user-intent.
    /// </param>
    /// <param name="contextId">Identifier of the new context resource.</param>
    /// <param name="delta">Partial update data describing what changed.</param>
    /// <param name="confidence">Confidence score 0.0–1.0 for the detected context change.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitContextUpdateAsync(
        string contextType,
        string contextId,
        object delta,
        double confidence,
        CancellationToken cancellationToken = default)
    {
        var data = new ContextUpdatePayload(contextType, contextId, delta, confidence);
        return EmitAsync(ChatSseR2EventTypes.ContextUpdate, data, cancellationToken);
    }

    /// <summary>
    /// Emits a <c>context_highlight</c> event that instructs the source pane to scroll
    /// to and highlight text ranges in a document (e.g. a cited passage).
    ///
    /// Lifecycle: emitted after the last token (post-stream), once citations are resolved.
    /// </summary>
    /// <param name="documentId">ID of the document containing the highlighted passage.</param>
    /// <param name="highlights">
    /// One or more character-range highlights (each carrying startOffset and endOffset).
    /// </param>
    /// <param name="highlightType">
    /// Highlight semantic. One of: relevant, cited, conflicting.
    /// </param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitContextHighlightAsync(
        string documentId,
        IReadOnlyList<RangeHighlight> highlights,
        string highlightType,
        CancellationToken cancellationToken = default)
    {
        var data = new ContextHighlightPayload(documentId, highlights, highlightType);
        return EmitAsync(ChatSseR2EventTypes.ContextHighlight, data, cancellationToken);
    }

    /// <summary>
    /// Emits a <c>workspace_action</c> event that asks the shell to perform a
    /// workspace-level action (navigate, open-document, run-playbook, dismiss).
    ///
    /// Lifecycle: emitted during streaming when an AI tool decides to trigger a UI action.
    /// </summary>
    /// <param name="actionId">Unique identifier for this action invocation.</param>
    /// <param name="actionType">
    /// Action type. One of: navigate, open-document, run-playbook, dismiss.
    /// </param>
    /// <param name="label">Human-readable label displayed in any confirmation UI.</param>
    /// <param name="requiresConfirmation">
    /// Whether the shell must prompt the user before executing the action.
    /// </param>
    /// <param name="targetWidgetId">Optional ID of the widget the action targets.</param>
    /// <param name="parameters">Optional action parameters (arbitrary JSON object).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitWorkspaceActionAsync(
        string actionId,
        string actionType,
        string label,
        bool requiresConfirmation,
        string? targetWidgetId = null,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var data = new WorkspaceActionPayload(actionId, actionType, label, requiresConfirmation, targetWidgetId, parameters);
        return EmitAsync(ChatSseR2EventTypes.WorkspaceAction, data, cancellationToken);
    }

    /// <summary>
    /// Emits a <c>capability_change</c> event that notifies the client that an AI
    /// capability's availability has changed.
    ///
    /// Lifecycle: emitted during streaming when the agent detects a tool is degraded
    /// or unavailable (e.g. search index quota exceeded).
    /// </summary>
    /// <param name="capability">
    /// Capability name. One of: search, summarize, cite, memory, safety, playbook.
    /// </param>
    /// <param name="status">
    /// Availability status. One of: available, degraded, unavailable.
    /// </param>
    /// <param name="retryAfterSeconds">
    /// Seconds until the capability is expected to recover. Required when status is
    /// 'degraded'; optional otherwise.
    /// </param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitCapabilityChangeAsync(
        string capability,
        string status,
        int? retryAfterSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var data = new CapabilityChangePayload(capability, status, retryAfterSeconds);
        return EmitAsync(ChatSseR2EventTypes.CapabilityChange, data, cancellationToken);
    }

    /// <summary>
    /// Emits a <c>safety_annotation</c> event carrying post-stream groundedness data,
    /// citation verification results, and content-policy outcomes.
    ///
    /// Lifecycle: emitted ONLY after the last token (post-stream, after typing_end) to
    /// avoid blocking perceived response latency. Callers MUST respect this ordering
    /// constraint (spec FR-801: safety annotations are post-stream only).
    /// </summary>
    /// <param name="severity">Severity level. One of: info, warning, blocked.</param>
    /// <param name="category">
    /// Annotation category. One of: jailbreak, indirect-attack, groundedness, content-policy.
    /// </param>
    /// <param name="action">Action taken. One of: logged, filtered, blocked.</param>
    /// <param name="userMessage">Human-readable explanation shown to the user when relevant.</param>
    /// <param name="groundedness">Optional groundedness score and citation verification data.</param>
    /// <param name="citations">Optional citation verification result sets (verified/unverified/partial).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    public Task EmitSafetyAnnotationAsync(
        string severity,
        string category,
        string action,
        string userMessage,
        SafetyGroundedness? groundedness = null,
        SafetyCitations? citations = null,
        CancellationToken cancellationToken = default)
    {
        var data = new SafetyAnnotationPayload(severity, category, action, userMessage, groundedness, citations);
        return EmitAsync(ChatSseR2EventTypes.SafetyAnnotation, data, cancellationToken);
    }

    // =========================================================================
    // Core emission logic
    // =========================================================================

    /// <summary>
    /// Serializes <paramref name="payload"/> to a <see cref="JsonElement"/>, validates it
    /// against the registered R2 schema for <paramref name="eventType"/>, then writes the
    /// SSE frame on success.
    ///
    /// On validation failure: logs a warning (without the payload content, ADR-015) and
    /// returns without writing. The stream is left intact so R1 events can continue.
    /// </summary>
    private async Task EmitAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        // Serialize to JsonElement so the schema validator can inspect it.
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.Clone();

        // Validate structural contract before writing to the stream (lightweight, synchronous).
        var validation = await SseEventSchemaValidator.ValidateAsync(eventType, element, cancellationToken);
        if (!validation.IsValid)
        {
            // ADR-015: log only event type and error strings — never the payload.
            _logger.LogWarning(
                "R2SseEventEmitter: payload validation failed for event '{EventType}'. " +
                "Event skipped. Errors: {Errors}",
                eventType,
                string.Join("; ", validation.Errors));
            return;
        }

        // Wrap in the common ChatSseEvent envelope and write to the SSE stream.
        // The Data property carries the full structured payload; Content is null for R2 events.
        var evt = new ChatSseEvent(eventType, null, payload);
        await _writer(evt, cancellationToken);
    }
}

// =============================================================================
// Strongly-typed R2 payload records
// These records define the exact JSON shapes validated by SseEventSchemaValidator.
// They are internal to this file — callers use the typed EmitXxx methods above.
// =============================================================================

/// <summary>Payload for <c>workspace_widget</c> events.</summary>
internal sealed record WorkspaceWidgetPayload(
    string WidgetId,
    string WidgetType,
    object Payload,
    int Priority,
    string? TabId = null);

/// <summary>Payload for <c>context_update</c> events.</summary>
internal sealed record ContextUpdatePayload(
    string ContextType,
    string ContextId,
    object Delta,
    double Confidence);

/// <summary>
/// Payload for <c>context_highlight</c> events.
/// The <see cref="Highlights"/> list must be non-empty.
/// </summary>
internal sealed record ContextHighlightPayload(
    string DocumentId,
    IReadOnlyList<RangeHighlight> Highlights,
    string HighlightType);

/// <summary>
/// A single character-range highlight within a document.
/// Both offsets are zero-based character indices into the document plain text.
/// </summary>
public sealed record RangeHighlight(int StartOffset, int EndOffset, string? Text = null);

/// <summary>Payload for <c>workspace_action</c> events.</summary>
internal sealed record WorkspaceActionPayload(
    string ActionId,
    string ActionType,
    string Label,
    bool RequiresConfirmation,
    string? TargetWidgetId = null,
    object? Parameters = null);

/// <summary>Payload for <c>capability_change</c> events.</summary>
internal sealed record CapabilityChangePayload(
    string Capability,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? RetryAfterSeconds = null);

/// <summary>Payload for <c>safety_annotation</c> events.</summary>
internal sealed record SafetyAnnotationPayload(
    string Severity,
    string Category,
    string Action,
    string UserMessage,
    SafetyGroundedness? Groundedness = null,
    SafetyCitations? Citations = null);

/// <summary>
/// Groundedness data attached to a <c>safety_annotation</c> event.
/// Score is a float in [0.0, 1.0] where 1.0 = fully grounded.
/// </summary>
public sealed record SafetyGroundedness(double Score, string? Rationale = null);

/// <summary>
/// Citation verification result sets attached to a <c>safety_annotation</c> event.
/// Each list contains citation IDs classified as verified, unverified, or partially verified.
/// </summary>
public sealed record SafetyCitations(
    IReadOnlyList<string>? Verified = null,
    IReadOnlyList<string>? Unverified = null,
    IReadOnlyList<string>? Partial = null);
