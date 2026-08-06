/**
 * activeTabFocusStamp.ts — task 010 (FR-A1) pure derivation helper.
 *
 * `ConversationPane`'s existing `usePaneEvent('workspace', …)` subscription (ADR-030) is extended
 * with an `active_widget_changed` branch that stamps the currently-focused Workspace tab into a
 * REF (not state, to avoid re-render churn on every tab switch). The mapping from the bus event to
 * the stored stamp is factored out here as a pure function — mirroring this file's existing
 * `routeSummarizeIntent` / `normalizeReviewDepth` pattern (pure logic in a sibling module, unit
 * tested directly; the component only holds the trivial `ref.current = …` glue) — so the one piece
 * of actual logic (field defaulting) is testable without rendering the full pane or reaching into a
 * private ref.
 *
 * Consumed by FR-A2 (outbound-body decorate, task 011) and, server-side, by FR-A3 (task 012) via the
 * value task 011 threads through.
 *
 * @see ConversationPane.tsx — `activeTabFocusRef` + the `usePaneEvent('workspace', …)` handler
 * @see ADR-030 — PaneEventBus channels + additive event-type discriminants
 */

import type { WorkspacePaneEvent, WidgetContextType } from "@spaarke/ai-widgets";

/**
 * The shape held in `ConversationPane`'s `activeTabFocusRef`, stamped whenever the active Workspace
 * tab changes.
 *
 * - `widgetType` / `tabId` / `displayName` mirror the dispatching `WorkspacePaneEvent` fields
 *   (`WorkspacePane.tsx`'s `broadcastActiveTabChange`).
 * - `contextType` is the FR-B1/FR-C3 closed-set discriminant ({@code email | document | compose-doc |
 *   matter-grid | dashboard | calendar}, task 020) declared on the widget's registry metadata and
 *   resolved server-side (client-side here) by `WorkspacePane.broadcastActiveTabChange`, which
 *   passes it through on the event as `widgetContextType`. `undefined` when the widget declared no
 *   contextType (or the widgetType is unregistered) — this is NOT a guess-and-default; it mirrors
 *   the registry's own "none" state.
 * - `compactState` is the widget's compact visible-state shape (from the event's `widgetData`), if
 *   the dispatching widget supplied one; `undefined` otherwise.
 */
export interface ActiveTabFocusStamp {
  widgetType: string;
  contextType?: WidgetContextType;
  tabId: string;
  displayName: string;
  compactState?: unknown;
}

/**
 * Maps a `workspace` channel event to an {@link ActiveTabFocusStamp}, or `null` when the event is
 * not `active_widget_changed` (the subscriber's existing branches handle every other discriminant —
 * this helper is additive and MUST NOT be invoked for those).
 *
 * Missing `tabId` / `widgetType` default to `""` (the dispatcher — `WorkspacePane.broadcastActiveTabChange`
 * — always supplies both per its own guard, but the wire type declares them optional per ADR-030's
 * additive-payload convention, so this stays defensive rather than asserting non-null). `displayName`
 * falls back to `widgetType` when absent, matching the dispatcher's own `displayName ?? widgetType`
 * fallback. `compactState` passes the event's `widgetData` through verbatim (typed `unknown` — the
 * shape is widget-dependent; downstream consumers narrow by `widgetType`).
 */
export function deriveActiveTabFocusStamp(event: WorkspacePaneEvent): ActiveTabFocusStamp | null {
  if (event.type !== "active_widget_changed") return null;

  return {
    widgetType: event.widgetType ?? "",
    contextType: event.widgetContextType,
    tabId: event.tabId ?? "",
    displayName: event.displayName ?? event.widgetType ?? "",
    compactState: event.widgetData,
  };
}
