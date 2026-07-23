/**
 * chipPreference.ts — client source for the Suggested-Next-Steps chip DISPLAY reorder (D-043-01,
 * option (c): STATED preference overrides, LEARNED usage as the fallback).
 *
 * `chipDisplayOrder.reorderChipsForDisplay` shipped in task 043 but had no client-accessible
 * preference to key on, so every call site passed `undefined` and the reorder was inert (chips always
 * rendered in server-declared order). This module produces the missing `ChipDisplayPreference`:
 *
 *   - LEARNED (fallback, live now): the user's own recent dispatch usage, tracked in localStorage
 *     keyed by `sprk_playbookconsumer` Binding id — most-used-then-most-recent first. This is a
 *     genuine "the Assistant learns what you use" signal that needs NO server projection. It is a
 *     DISPLAY-ONLY sort key (ADR-039 preference≠permission): it never adds, removes, or grants a
 *     capability — it only reorders chips the server already grounded and returned.
 *   - STATED (override seam): an ordered list a caller can supply when a durable, cross-device stated
 *     preference becomes projectable (a structured `sprk_userprofile` field surfaced to the client, or
 *     a session-bootstrap projection). When present + non-empty it WINS over learned usage. Today no
 *     such structured field exists, so callers pass none and learned usage drives the order.
 *
 * `Date.now()` is used deliberately for recency — this is browser code (not a resumable workflow).
 * All localStorage access is defensive (private-mode / disabled storage degrades to "no learned
 * signal" → server order), so the reorder can never throw into the render path.
 */

import type { ChipDisplayPreference } from "./chipDisplayOrder";
import { isLocalChip } from "./localActionChips";

const STORAGE_KEY = "spaarkeai.chipUsage.v1";
/** Cap the tracked set so localStorage can't grow unbounded across a long-lived profile. */
const MAX_TRACKED = 40;

interface UsageEntry {
  count: number;
  last: number;
}
type UsageMap = Record<string, UsageEntry>;

function readUsage(): UsageMap {
  try {
    if (typeof localStorage === "undefined") return {};
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as unknown;
    return parsed && typeof parsed === "object" ? (parsed as UsageMap) : {};
  } catch {
    return {};
  }
}

function writeUsage(map: UsageMap): void {
  try {
    if (typeof localStorage === "undefined") return;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(map));
  } catch {
    /* private mode / quota — learned signal is best-effort, never fatal */
  }
}

/**
 * Record that the user just dispatched a real Binding (a Click/next-step chip). Local `local:*`
 * action chips and empty ids are ignored — they are client bridges, not grounded Bindings, so they
 * must never enter the reorder key. Prunes to the {@link MAX_TRACKED} most-recent bindings.
 */
export function recordChipUsage(bindingId: string): void {
  if (!bindingId || isLocalChip(bindingId)) return;
  const map = readUsage();
  const existing = map[bindingId];
  map[bindingId] = { count: (existing?.count ?? 0) + 1, last: Date.now() };

  const keys = Object.keys(map);
  if (keys.length > MAX_TRACKED) {
    // Drop the least-recently-used entries beyond the cap.
    const keep = keys
      .sort((a, b) => (map[b].last ?? 0) - (map[a].last ?? 0))
      .slice(0, MAX_TRACKED);
    const pruned: UsageMap = {};
    for (const k of keep) pruned[k] = map[k];
    writeUsage(pruned);
    return;
  }
  writeUsage(map);
}

/** The learned Binding order: most-dispatched first, ties broken by most-recent. */
export function getLearnedBindingOrder(): string[] {
  const map = readUsage();
  return Object.entries(map)
    .sort(([, a], [, b]) => b.count - a.count || b.last - a.last)
    .map(([bindingId]) => bindingId);
}

/**
 * Build the `ChipDisplayPreference` for the reorder (option (c)). A non-empty `statedOrder` (a durable
 * stated preference, when one is projectable) WINS; otherwise the learned usage order is used. Returns
 * `{ preferredBindingOrder: [] }` when neither exists — the reorder then deterministically keeps the
 * server-declared order.
 */
export function buildChipPreference(
  statedOrder?: ReadonlyArray<string> | null
): ChipDisplayPreference {
  const stated = (statedOrder ?? []).filter((id) => typeof id === "string" && id.length > 0);
  const preferredBindingOrder = stated.length > 0 ? stated : getLearnedBindingOrder();
  return { preferredBindingOrder };
}
