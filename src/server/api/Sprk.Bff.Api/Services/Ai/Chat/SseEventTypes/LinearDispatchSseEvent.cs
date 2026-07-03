using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

/// <summary>
/// SSE event emitted by the BFF chat pipeline when an explicit-intent keyword match
/// in the user message deterministically identifies a Linear AI Consumer. The client
/// invokes the target endpoint immediately (no user confirmation). Introduced in R7
/// Wave 12.3 Phase 12.3a per operator direction (2026-07-03).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design contrast with <see cref="PlaybookOptionsSseEvent"/></b>:
/// <c>playbook_options</c> carries the FR-48 invariant "NEVER auto-execute — user MUST click."
/// This event carries the OPPOSITE contract: auto-execute is the intent. They coexist
/// intentionally:
/// <list type="bullet">
///   <item><c>linear_dispatch</c> — explicit-intent (keyword match) → auto-run. NL "summarize this"
///         with a summarize keyword lands here.</item>
///   <item><c>playbook_options</c> — ambiguous-intent (vector match, low confidence) → user picks
///         from top-N candidates. NL "help with this contract" lands here.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Wire format</b>:
/// <code>
/// event: linear_dispatch
/// data: {
///   "type": "linear_dispatch",
///   "data": {
///     "consumerType": "chat-summarize",
///     "dispatchUrl": "/api/ai/chat/sessions/{sessionId}/summarize",
///     "requestBody": { "fileIds": ["..."], "style": null },
///     "reason": "explicit-keyword-match:summarize",
///     "sessionAttachmentIds": ["..."]
///   }
/// }
/// </code>
/// </para>
///
/// <para>
/// <b>ADR-015 tier-1 audit safety</b>: payload contains only deterministic identifiers
/// (consumer-type constant, endpoint URL from a code-defined route table, opaque session
/// file IDs, controlled-vocabulary reason tag). No user message text, no LLM output.
/// </para>
/// </remarks>
public static class LinearDispatchSseEvent
{
    /// <summary>
    /// SSE event type string. Bound by Wave 12.3 client contract — do NOT rename without
    /// coordinating with the client-side <c>ConversationPane.tsx</c> event handler.
    /// </summary>
    public const string EventType = "linear_dispatch";
}

/// <summary>
/// Payload for <see cref="LinearDispatchSseEvent"/>.
/// </summary>
/// <param name="ConsumerType">Consumer-type key (e.g. <c>"chat-summarize"</c>) matching a
/// <c>ConsumerTypes.*</c> constant. Deterministic identifier.</param>
/// <param name="DispatchUrl">Server-relative URL the client should POST to. Populated by the
/// BFF from its known Linear endpoint registry — the client does NOT interpret this to build
/// arbitrary URLs. Contains the session id substituted in the path template.</param>
/// <param name="RequestBody">JSON string carrying the pre-composed request body for the
/// dispatch POST. Client sends it verbatim as <c>application/json</c>.</param>
/// <param name="Reason">Short controlled-vocabulary tag explaining why this dispatch fired
/// (e.g. <c>"explicit-keyword-match:summarize"</c>). Free-form NL forbidden.</param>
/// <param name="SessionAttachmentIds">Session file IDs correlated to the request (for
/// client-side telemetry + candidate correlation). Opaque identifiers.</param>
public sealed record LinearDispatchSseEventData(
    [property: JsonPropertyName("consumerType")] string ConsumerType,
    [property: JsonPropertyName("dispatchUrl")] string DispatchUrl,
    [property: JsonPropertyName("requestBody")] string RequestBody,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("sessionAttachmentIds")] IReadOnlyList<string> SessionAttachmentIds);
