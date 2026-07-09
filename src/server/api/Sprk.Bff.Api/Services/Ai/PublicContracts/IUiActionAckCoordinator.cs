using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// ─────────────────────────────────────────────────────────────────────────────
// D-F3 UI-action truthfulness — client-ack contract (spec FR-A1-08 / NFR-08;
// task AIR2-037).
//
// WHAT THIS IS: the ONE ack mechanism a UI-affecting tool result (open tab,
// open Compose, navigation) gates on before it may claim success. A tool
// handler emits an SSE frame carrying a server-issued frame id, then calls
// <see cref="IUiActionAckCoordinator.WaitForAckAsync"/> with that SAME frame
// id. The tool result completes ONLY when the client posts back an
// acknowledgment referencing the frame id (see the
// <c>POST /api/ai/chat/sessions/{sessionId}/ack</c> endpoint, which calls
// <see cref="IUiActionAckCoordinator.TryAcknowledge"/>), or times out —
// producing an HONEST failure rather than a fabricated "I opened the tab"
// (the R2-D failure mode this task structurally prevents).
//
// NO SECOND ACK MECHANISM (CLAUDE.md §11 + task constraint): this coordinator
// is the ONLY ack-gating primitive. It generalizes over ANY UI-claiming tool
// keyed by (sessionId, frameId) — it does not special-case workspace tabs.
// Compose r2's open-Compose / save-back destination writes reuse the SAME
// (sessionId, frameId) contract (they already dispatch through the chat tool
// pipeline via SendWorkspaceArtifactHandler's "Workspace" widgetType), so no
// Compose-side fork is required.
//
// PLACEMENT (ADR-013 / root CLAUDE.md §10): lives under
// Services/Ai/PublicContracts/ so Compose r2 and other consumers bind to the
// published ack shape via the facade, never to an AI-internal ack type.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of a UI-action ack wait (see <see cref="IUiActionAckCoordinator.WaitForAckAsync"/>).
/// </summary>
public enum UiActionAckOutcome
{
    /// <summary>The client posted an acknowledgment referencing the frame id before the timeout elapsed.</summary>
    Acknowledged,

    /// <summary>
    /// No acknowledgment arrived within the timeout. The caller MUST render an honest failure —
    /// NEVER a fabricated success (FR-A1-08 / NFR-08).
    /// </summary>
    TimedOut,
}

/// <summary>
/// Coordinates client acknowledgments for UI-affecting tool results (D-F3 / FR-A1-08).
/// </summary>
/// <remarks>
/// A single tenant-agnostic in-process implementation (<c>Chat.UiActionAckCoordinator</c>)
/// backs this contract — registered as a singleton so pending waits survive across the
/// scoped request that emitted the frame and the (separate) scoped request that later
/// posts the ack. Keys are (sessionId, frameId); frame ids are server-issued GUIDs
/// (per-tool-call), so cross-session/cross-frame collisions are not a practical concern.
/// </remarks>
public interface IUiActionAckCoordinator
{
    /// <summary>
    /// Registers a pending ack wait for <paramref name="frameId"/> within
    /// <paramref name="sessionId"/> and waits until either a matching
    /// <see cref="TryAcknowledge"/> call resolves it or <paramref name="timeout"/> elapses.
    /// </summary>
    /// <param name="sessionId">The chat session the UI-affecting tool call belongs to.</param>
    /// <param name="frameId">
    /// The SAME identifier emitted on the SSE frame the client renders (e.g. the workspace
    /// tab id on a <c>workspace_open_tab</c> frame). The client's ack MUST echo this value.
    /// </param>
    /// <param name="timeout">How long to wait before returning <see cref="UiActionAckOutcome.TimedOut"/>.</param>
    /// <param name="cancellationToken">Propagates caller cancellation (e.g. client disconnect).</param>
    Task<UiActionAckOutcome> WaitForAckAsync(
        string sessionId,
        string frameId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the pending wait (if any) registered for (<paramref name="sessionId"/>,
    /// <paramref name="frameId"/>). Called by the ack endpoint when the client confirms it
    /// rendered the UI-affecting frame.
    /// </summary>
    /// <returns>
    /// <c>true</c> when a pending waiter was found and resolved; <c>false</c> when no waiter
    /// is pending (already timed out, already acknowledged, or an unknown frame id — all
    /// treated as benign by the caller).
    /// </returns>
    bool TryAcknowledge(string sessionId, string frameId);
}

/// <summary>Request body for <c>POST /api/ai/chat/sessions/{sessionId}/ack</c>.</summary>
/// <param name="FrameId">The frame id echoed from the SSE frame the client just rendered.</param>
public sealed record UiActionAckRequest(
    [property: JsonPropertyName("frameId")] string FrameId);

/// <summary>Response body for <c>POST /api/ai/chat/sessions/{sessionId}/ack</c>.</summary>
/// <param name="Acknowledged">
/// <c>true</c> when a pending tool-result wait was found and resolved by this ack;
/// <c>false</c> when there was nothing pending (already timed out, duplicate ack, or unknown
/// frame id). Either response is a 200 — the client does not need to retry or branch on this.
/// </param>
public sealed record UiActionAckResponse(
    [property: JsonPropertyName("acknowledged")] bool Acknowledged);
