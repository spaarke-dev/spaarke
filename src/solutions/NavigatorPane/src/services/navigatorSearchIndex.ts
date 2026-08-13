/**
 * navigatorSearchIndex — the shared in-memory index `QuickSwitcher.tsx`
 * fuzzy-matches over (spaarke-side-pane-navigation-history-r1 task 070, spec
 * FR-11).
 *
 * A MODULE-SCOPED store (deliberately NOT React state/context) rather than
 * hoisting `RecentTab.tsx`/`PinnedTab.tsx`/`ViewsTab.tsx`'s own `Xrm.WebApi`
 * loads into `NavigatorBody.tsx`. `NavigatorBody.tsx` mounts only ONE tab
 * panel at a time (see its `selectedTab === 'recent' ? <RecentTab/> : ...`
 * render — task 040, unchanged by this task); each tab still owns its single
 * existing load call (task 041/050/060) and, immediately after computing its
 * final (already-security-trimmed where applicable) row/view list, reports
 * that SAME list into this module via `setRecentSearchEntries` /
 * `setPinnedSearchEntries` / `setViewSearchEntries`. This is a pure
 * aggregation layer over data each tab already fetched — no second query is
 * ever issued, and NavigatorBody's existing tab-panel mount/unmount behavior
 * (and its locked-in test assertions) is untouched.
 *
 * Being module-scoped (not component state) also means the aggregated
 * entries SURVIVE a tab's unmount when the user switches away — once a tab
 * has loaded at least once in this pane session, its entries stay locally
 * searchable for the rest of the session.
 *
 * Progressive availability (documented, not a defect): Recent is
 * NavigatorBody's default active tab, so its entries are searchable the
 * moment the pane opens. Pinned/Views entries become locally searchable once
 * the user has visited that tab at least once. Until then, a query that
 * would have matched a not-yet-loaded Pinned/Views entry finds no LOCAL hit
 * and correctly escalates to the live `Xrm.WebApi`/`ViewService` lookup
 * (`liveSearchService.ts`) — which is FR-11's specified fallback path, not a
 * degraded case.
 *
 * Security trim (task 080): this module NEVER queries Dataverse itself and
 * NEVER re-derives a row from a raw/untrimmed source — every entry it ever
 * stores is handed to it by the caller AFTER that caller's own trim
 * (`securityTrimService.ts`'s `classifyTargets`, for Recent/Pinned) has
 * already run. A denied row is never in the array `RecentTab.tsx`/
 * `PinnedTab.tsx` report here, the same way it is never in the array they
 * render.
 *
 * @see QuickSwitcher.tsx — the sole reader, via {@link useNavigatorSearchIndex}
 * @see RecentTab.tsx / PinnedTab.tsx / ViewsTab.tsx — the three reporters
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-070-search.md
 */

import * as React from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Which Navigator surface an entry came from. */
export type SearchEntrySection = 'recent' | 'pinned' | 'view';

/**
 * Where Enter/click on a result navigates. Logical (Dataverse) targets are
 * ALWAYS navigated via `Xrm.Navigation` (project HARD constraint) — only
 * `weblink` opens via `window.open(url, '_blank', 'noopener')`. A `null`
 * target (e.g. an unresolvable `Custom`-pagetype row, mirroring
 * `RecentTab.tsx`/`PinnedTab.tsx`'s OQ-6 handling) is not navigable.
 */
export type SearchEntryTarget =
  | { type: 'entityrecord'; entityLogicalName: string; entityId: string }
  | { type: 'entitylist'; entityLogicalName: string; viewId?: string }
  | { type: 'weblink'; url: string };

/** One entry in the local index OR a live-escalation result (`source` distinguishes them). */
export interface SearchIndexEntry {
  /** Stable, globally-unique key across all three sections + live results. */
  id: string;
  /** The searchable + displayed label. */
  label: string;
  /** Short type chip text (e.g. "Matter", "View", "Link") — mirrors each tab's own chip mapping. */
  chipLabel: string;
  target: SearchEntryTarget | null;
}

interface IndexState {
  recent: SearchIndexEntry[];
  pinned: SearchIndexEntry[];
  view: SearchIndexEntry[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Store
// ─────────────────────────────────────────────────────────────────────────────

const state: IndexState = { recent: [], pinned: [], view: [] };
let cachedAll: SearchIndexEntry[] = [];
const listeners = new Set<() => void>();

function recompute(): void {
  cachedAll = [...state.recent, ...state.pinned, ...state.view];
  for (const listener of listeners) listener();
}

/** Called by `RecentTab.tsx` after its Viewed-history load + 080 trim completes. */
export function setRecentSearchEntries(entries: SearchIndexEntry[]): void {
  state.recent = entries;
  recompute();
}

/** Called by `PinnedTab.tsx` after its pin (Records + Bookmarks) load + 080 trim completes. */
export function setPinnedSearchEntries(entries: SearchIndexEntry[]): void {
  state.pinned = entries;
  recompute();
}

/** Called by `ViewsTab.tsx` after its `getAllUserQueries()` load completes. */
export function setViewSearchEntries(entries: SearchIndexEntry[]): void {
  state.view = entries;
  recompute();
}

/** Snapshot of every locally-indexed entry across all three sections. Stable reference until the next report. */
export function getAllSearchEntries(): SearchIndexEntry[] {
  return cachedAll;
}

export function subscribeSearchIndex(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Test-only reset. Module state otherwise persists for the life of the page
 * (by design — see module docblock "Progressive availability").
 */
export function __resetSearchIndexForTests(): void {
  state.recent = [];
  state.pinned = [];
  state.view = [];
  cachedAll = [];
  listeners.clear();
}

/** React hook — re-renders the caller whenever any tab reports new entries. */
export function useNavigatorSearchIndex(): SearchIndexEntry[] {
  return React.useSyncExternalStore(subscribeSearchIndex, getAllSearchEntries, getAllSearchEntries);
}
