/**
 * ComposeTraceHost.tsx — Context-pane HOST for the core D-F4 decision-traceability
 * view (FR-32 / spaarkeai-compose-r2 task 064).
 *
 * ## What this is (and is NOT)
 *
 * This is the thin HOST that binds the core's published, host-embeddable
 * `ExecutionTraceWidget` (`@spaarke/ai-widgets` — ADR-040 ledger trace, D-F4) to
 * the Compose surface's Context pane. It does NOT render a Compose-local trace:
 * per charter §3.4 Compose consumes the core widget AS-IS and never invents its
 * own "LLM reasoning trace" rendering or a local `TraceEvent` variant. All this
 * host supplies is the two host-embeddable inputs the widget's own docblock asks
 * for:
 *
 *   1. `sessionId`    — the active chat session id (from `useAiSession`), so the
 *                       trace is scoped to THIS session.
 *   2. `restoreTrace` — the host's server read function. The widget calls it on
 *                       mount to rehydrate the durable decision trace from the
 *                       ADR-040 session ledger, so dispatched-action provenance
 *                       (which Action/tool ran, its disposition/gate, ledger
 *                       refs) survives a hard refresh instead of being limited
 *                       to the same-page-load in-memory buffer. This is the
 *                       D-F4 "host-embeddable in an arbitrary container" seam
 *                       (AIR2-038 / FR-A1-09) — it takes ONLY a session id, so
 *                       the widget has no dependency on the chat surface.
 *
 * ## Audit-only invariant (project CLAUDE.md — binding)
 *
 * The Context pane is AUDIT-ONLY. This host renders the read-only trace widget
 * and NOTHING that captures a decision or accepts input — no textbox, no submit,
 * no action button. The trace is provenance/audit surfacing, never an input
 * surface.
 *
 * ## Why a separate component (not inline in ContextPaneController)
 *
 * `useAiSession()` THROWS outside an `AiSessionProvider`. Isolating the hook in
 * this child — mounted ONLY when the `execution-trace` tool is selected — keeps
 * ContextPaneController free of a top-level session dependency, so the pane's
 * other tools (and the pane's provider-less unit tests) are unaffected.
 *
 * @see ExecutionTraceWidget — the core widget hosted here (consumed AS-IS)
 * @see ISessionTraceReader / GET /api/ai/chat/sessions/{id}/trace — the BFF read surface
 * @see ADR-028 — `@spaarke/auth` `authenticatedFetch` (no raw Bearer)
 * @see ADR-040 — session ledger is the single source of truth; trace is a projection
 * @see ADR-021 — Fluent v9 + dark mode (owned by the hosted widget)
 */

import * as React from "react";
import {
  ExecutionTraceWidget,
  useAiSession,
  type ExecutionTraceData,
} from "@spaarke/ai-widgets";

/**
 * ComposeTraceHost — binds the core ExecutionTraceWidget to the Compose Context
 * pane with the D-F4 host-embeddable server read surface.
 *
 * Must be rendered inside an `AiSessionProvider` (it calls `useAiSession`). In
 * the SpaarkeAi shell that is always true — the whole three-pane tree is wrapped
 * by `AiSessionProvider` (see ThreePaneShell). It is mounted lazily (only for the
 * `execution-trace` context tool) so the dependency never reaches other tools.
 */
export function ComposeTraceHost(): React.JSX.Element {
  const { chatSessionId, authenticatedFetch, bffBaseUrl } = useAiSession();

  // Server read function (D-F4). Reads the ADR-040 ledger projection as the
  // TraceEvent v1 stream via `@spaarke/auth` (ADR-028 — token never crosses the
  // boundary). Fully soft-fail: any non-OK / parse failure yields an empty
  // stream so the widget silently falls back to its in-memory live buffer — the
  // read surface is a convenience, never a gate (matches the widget's own
  // backfill contract).
  const restoreTrace = React.useCallback(
    async (sessionId: string): Promise<readonly unknown[]> => {
      if (!bffBaseUrl || !sessionId) return [];
      try {
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/trace`;
        const resp = await authenticatedFetch(url, { method: "GET" });
        if (!resp.ok) return [];
        const data = (await resp.json()) as unknown;
        return Array.isArray(data) ? data : [];
      } catch {
        // Non-fatal — the widget degrades to the live in-memory buffer.
        return [];
      }
    },
    [authenticatedFetch, bffBaseUrl]
  );

  // The host-embeddable data contract the widget consumes. When there is no
  // active chat session yet, `sessionId` is '' and the widget skips the server
  // backfill (its own guard) and shows the empty/live state — never a crash.
  const data: ExecutionTraceData = React.useMemo(
    () => ({
      sessionId: chatSessionId ?? "",
      restoreTrace: restoreTrace as ExecutionTraceData["restoreTrace"],
    }),
    [chatSessionId, restoreTrace]
  );

  return <ExecutionTraceWidget data={data} widgetType="execution-trace" />;
}

ComposeTraceHost.displayName = "ComposeTraceHost";

export default ComposeTraceHost;
