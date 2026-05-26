/**
 * contextToolPin.ts — Single-pin Context-tool "default on load" persistence
 * (Task 099 — Round 6 operator smoke feedback, 2026-05-22).
 *
 * The Context pane Tools dropdown (`ContextPaneMenu`, task 095) exposes two
 * tools: Quick Start (existing GetStartedCardsWidget) and Semantic Search
 * (SemanticSearchCriteriaTool). Round 6 operator request: allow the user to
 * "pin" their preferred default Context tool so that the pinned tool is the
 * default selection on next session load.
 *
 * Pin semantics (per operator follow-up 2026-05-22):
 *   "Pin = default on load"
 *
 *   - Only ONE tool can be pinned at a time (single-pin). Pinning a new tool
 *     implicitly unpins the previous one.
 *   - The pinned tool drives the initial `selectedTool` value in
 *     `useContextTool` ONLY when no prior `selected-tool` value exists for
 *     the user. If both `selected-tool` AND `pinned-tool` exist,
 *     `selected-tool` wins (user's last-used > pinned default). This keeps the
 *     existing tool-selection persistence behaviour from task 095 intact while
 *     adding the "first-time / cold-reset" default-on-load benefit.
 *   - Toggling the pin does NOT change the active `selectedTool`. The user
 *     keeps using whichever tool they're currently on; the pin only takes
 *     effect on the next cold load (or after a localStorage clear).
 *
 * Storage shape:
 *   Key:   `spaarke:context:pinned-tool`
 *   Value: a JSON string holding a single `ContextToolId` (e.g. `"quick-start"`
 *          or `"semantic-search"`), OR the key is absent when nothing is
 *          pinned. Validated against `VALID_CONTEXT_TOOL_IDS` on every read;
 *          corrupt / unknown values fall back to `null` (no pin).
 *
 * Pattern provenance: this utility mirrors `useContextTool.ts` (task 095)
 * verbatim in posture — try/catch-wrapped accessors, type-safe validation,
 * silent failure on private-browsing / quota-exceeded. The hook level
 * (`useContextTool`) is intentionally not extended with pin state because the
 * pin's persistent value is read once at mount-time as a fallback default;
 * after that, the rest of the tool-selection flow uses `selectedTool` only.
 *
 * @see useContextTool — consumer that uses `getPinnedContextTool()` as the
 *      initial-default fallback when no `selected-tool` exists.
 * @see ContextPaneMenu — UI that calls `pinContextTool` / `unpinContextTool`
 *      from the pin icon click on each MenuItem.
 * @see usePaneCollapse — same localStorage posture (task 094)
 * @see pinnedWorkspaces — same try/catch + JSDoc cross-reference style (task 092)
 */

import type { ContextToolId } from "../hooks/useContextTool";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * localStorage key for the single pinned tool id. Namespaced under the
 * `spaarke:` prefix the rest of the SpaarkeAi solution uses for client-side
 * preferences (matches task 092's `spaarke:workspace:pinned-list`, task 094's
 * `spaarke:panes:collapsed`, and task 095's `spaarke:context:selected-tool`).
 */
const STORAGE_KEY = "spaarke:context:pinned-tool";

/**
 * Whitelist of valid pinned tool ids. Kept in sync with `ContextToolId` in
 * `useContextTool.ts`. Centralised here to keep validation logic colocated
 * with the storage layer.
 */
const VALID_CONTEXT_TOOL_IDS: ReadonlySet<string> = new Set<ContextToolId>([
  "quick-start",
  "semantic-search",
]);

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

/**
 * Returns the currently-pinned Context tool, or `null` when nothing is
 * pinned (the storage key is absent, holds a corrupt JSON value, or holds an
 * unknown tool id). Never throws — private-browsing / quota errors degrade
 * silently to `null`.
 */
export function getPinnedContextTool(): ContextToolId | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return null;
    // Stored as a JSON string (matches the rest of the spaarke: namespace);
    // tolerate raw strings too in case an older / hand-edited value exists.
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      parsed = raw;
    }
    if (typeof parsed === "string" && VALID_CONTEXT_TOOL_IDS.has(parsed)) {
      return parsed as ContextToolId;
    }
    return null;
  } catch {
    // Private browsing, quota exceeded, or corrupt JSON → behave like a
    // fresh user. Never crash on storage failure.
    return null;
  }
}

/**
 * Set the pinned Context tool. Replaces any previously-pinned tool (single-pin
 * semantics). Best-effort persistence — silent on quota / private-mode
 * failures so the calling UI doesn't have to handle storage errors.
 */
export function pinContextTool(id: ContextToolId): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(id));
  } catch {
    // Best-effort persistence — same posture as task 092's
    // pinnedWorkspaces.ts: silent no-op on storage failure.
  }
}

/**
 * Remove the pinned Context tool (no pin → first-mount fallback is the
 * hardcoded `DEFAULT_TOOL` in `useContextTool`). Idempotent — safe to call
 * when nothing is pinned.
 */
export function unpinContextTool(): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Best-effort — silent on failure (mirror of pinContextTool above).
  }
}
