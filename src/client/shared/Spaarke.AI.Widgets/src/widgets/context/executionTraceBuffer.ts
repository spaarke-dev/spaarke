/**
 * executionTraceBuffer — module-scoped replay buffer for `tool_chain` context
 * events (G-P3 UAT round-5 R5-D, 2026-07-07).
 *
 * ## Why this exists
 *
 * The ExecutionTraceWidget stays EMPTY in the browser even after tool-invoking
 * turns. Round-5 investigation proved the wire contract (server `tool_chain`
 * context_event frames → SprkChat onContextEvent → useContextEventBridge →
 * PaneEventBus `context` channel → widget subscription) is correct end-to-end —
 * the break is MOUNT LIFECYCLE:
 *
 *   1. The widget mounts only when the user picks "Execution Trace" from the
 *      Context-pane Tools menu (default tool is `quick-start`), so it is
 *      UNMOUNTED during the streaming turn that emits the events.
 *   2. PaneEventBus is fire-and-forget — dispatching to a channel with no
 *      subscribers silently drops the event.
 *   3. The widget's entries start at `[]` with no backfill: the session restore
 *      payload carries no ToolChains and no endpoint exposes the trace ledger.
 *
 * This buffer closes leg 3 for the LIVE page session: the ALWAYS-MOUNTED
 * bridge (useContextEventBridge in the SpaarkeAi conversation pane) records
 * every dispatched `tool_chain` event here, and the widget REPLAYS the buffer
 * on mount before its live subscription takes over. Bounded FIFO — same cap
 * philosophy as the widget's own entry cap.
 *
 * ## Honest limitation (named follow-up, NOT covered here)
 *
 * The buffer is in-memory per page load: a HARD REFRESH still loses prior
 * turns' trace because the server exposes no ToolChain read surface
 * (SessionRestoreResponse has no ToolChains field). Backfilling across reloads
 * requires a server contract change — recorded as an operator-verdict backlog
 * candidate in the round-4/5 findings note.
 *
 * NFR-07: events stored here are the bridge's already-narrowed identifier-only
 * shapes (toolId / argsSummary / counts / durations) — never raw args.
 */

/** Maximum buffered events (each event may carry multiple calls). */
export const MAX_BUFFERED_TRACE_EVENTS = 50;

/**
 * The buffered event shape — structurally the PaneEventBus `context` channel
 * `tool_chain` event the bridge dispatches. Kept loose (unknown-record) so this
 * module has no dependency on the pane-event type unions; the widget re-narrows
 * every field on replay exactly as it does for live events.
 */
export type BufferedTraceEvent = Record<string, unknown> & { type: 'tool_chain' };

const buffer: BufferedTraceEvent[] = [];

/**
 * Records a dispatched `tool_chain` context event for later replay. Called by
 * the always-mounted SSE bridge alongside its PaneEventBus dispatch.
 */
export function recordExecutionTraceEvent(event: BufferedTraceEvent): void {
  if (!event || event.type !== 'tool_chain') return;
  buffer.push(event);
  if (buffer.length > MAX_BUFFERED_TRACE_EVENTS) {
    buffer.splice(0, buffer.length - MAX_BUFFERED_TRACE_EVENTS);
  }
}

/** Snapshot of the buffered events, oldest first (for replay-on-mount). */
export function getExecutionTraceBuffer(): readonly BufferedTraceEvent[] {
  return buffer.slice();
}

/** Clears the buffer — new chat session (the trace belongs to a session) and tests. */
export function clearExecutionTraceBuffer(): void {
  buffer.length = 0;
}
