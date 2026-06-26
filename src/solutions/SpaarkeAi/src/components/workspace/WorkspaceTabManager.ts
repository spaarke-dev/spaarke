/**
 * WorkspaceTabManager.ts — Plain TypeScript tab state manager for WorkspacePane.
 *
 * Manages an ordered array of workspace tabs with a configurable cap on the
 * number of non-Home tabs (`MAX_WORKSPACE_TABS`). When a new non-Home tab is
 * added and the count of non-Home tabs would exceed the cap, the oldest
 * non-Home tab (by insertion order) is evicted first — a FIFO eviction policy.
 *
 * The Home tab (kind === 'home') is exempt from the cap and never evicted
 * by FIFO. It also cannot be closed via closeTab. This supports the
 * SpaarkeAi WorkspacePane model where Home is always present as a non-closable
 * default tab (typically the embedded LegalWorkspace) and widget tabs are
 * added/evicted around it (FR-13, design §C-2 / §F-3).
 *
 * This is a plain class, NOT a React component. WorkspacePane holds an instance
 * in a React ref and drives re-renders by copying managed state into React state.
 *
 * Tab lifecycle:
 *   ensureHomeTab(displayName?, widgetData?, Component?) → returns the Home tab id
 *   addTab(widgetType, data) → returns the new (non-Home) tab id
 *   updateTab(tabId, data)   → updates an existing tab's data payload
 *   closeTab(tabId)          → removes a non-Home tab; returns the id that
 *                              became active (or null). No-op for Home.
 *   setActiveTab(tabId)      → changes the active tab
 *
 * @see WorkspacePane — the React component that owns this manager
 * @see ADR-021 — no hardcoded colors in component layer
 * @see ADR-012 — constants are a single source of truth, exported by name
 * @see FR-13 — `MAX_WORKSPACE_TABS = 8` exempts Home from cap and FIFO
 */

import type React from "react";

// ---------------------------------------------------------------------------
// Constants — exported as the single source of truth
// ---------------------------------------------------------------------------

/**
 * Maximum number of concurrent **non-Home** workspace tabs.
 *
 * When a new non-Home tab is added and `nonHomeTabCount >= MAX_WORKSPACE_TABS`,
 * the oldest non-Home tab (by insertion order) is evicted first (FIFO). The
 * Home tab — if present — is exempt from the cap and never evicted by FIFO.
 *
 * Single source of truth (ADR-012): consumers MUST import this constant by
 * name rather than hardcoding a numeric value.
 *
 * @see FR-13 — Acceptance: 9th non-Home widget evicts oldest; Home preserved.
 */
export const MAX_WORKSPACE_TABS = 8;

// ---------------------------------------------------------------------------
// WorkspaceTab — public tab state record
// ---------------------------------------------------------------------------

/**
 * Discriminant for tab kind.
 *   'home'   — the always-present non-closable, non-evictable tab.
 *   'widget' — a normal widget tab subject to FIFO eviction.
 */
export type WorkspaceTabKind = "home" | "widget";

// ---------------------------------------------------------------------------
// Persistence — NFR-09 tab write-through (task 065)
//
// Workspace tabs are persisted via the BFF `PATCH /api/ai/chat/sessions/
// {sessionId}/tabs` endpoint so non-Home tabs survive a page refresh. The
// Home tab is NOT persisted — it is recreated by ensureHomeTab() on every
// WorkspacePane mount. React component references are NOT persisted either
// — they are re-resolved from `resolveWorkspaceWidget(widgetType)` on
// restore.
// ---------------------------------------------------------------------------

/** Serializable view of a non-Home tab (Component excluded; widgetType drives re-resolution on restore). */
export interface SerializableWorkspaceTab {
  /** Stable tab id; preserved across restore so deep-linking and event tabId carry over. */
  id: string;
  /** Widget type string — re-resolved through resolveWorkspaceWidget() on restore. */
  widgetType: string;
  /** Opaque widget payload — server stores it round-trip; client may shape as needed. */
  widgetData: unknown;
  /** Human-readable display label persisted alongside the tab. */
  displayName: string;
}

/** Persistence snapshot returned by serializeForPersistence(). */
export interface WorkspaceTabPersistenceSnapshot {
  tabs: SerializableWorkspaceTab[];
  /** Active tab id at save time; may be `"home"` or a widget id, or null if no tabs. */
  activeTabId: string | null;
}

/**
 * Optional constructor options for WorkspaceTabManager.
 *
 * `onPersistChange` is invoked AFTER any state-mutating method (addTab,
 * closeTab, setActiveTab, clearAllTabs, and ensureHomeTab calls that create
 * the Home tab for the first time, only when there are non-Home tabs to
 * persist). The callback receives the current persistence snapshot — the
 * caller (WorkspacePane) is responsible for debouncing and dispatching the
 * BFF write-through. The manager itself never performs network I/O.
 */
/**
 * Snapshot of the currently active tab, surfaced via the optional
 * `onActiveTabChange` callback. Foundation for cross-pane coordination
 * (Round 4 Fix 4, 2026-05-21) — the Assistant and Context panes can
 * subscribe to this in a follow-up task to scope themselves to the active
 * workspace context.
 */
export interface ActiveTabSnapshot {
  /** The stable id of the now-active tab (or null if the active tab cleared). */
  tabId: string | null;
  /** Widget type string of the active tab (or null when tabId is null). */
  widgetType: string | null;
  /** Widget payload of the active tab (or null when tabId is null). */
  widgetData: unknown;
  /** Human-readable label of the active tab (or null when tabId is null). */
  displayName: string | null;
  /** Discriminant — distinguishes the Home tab from widget tabs. */
  kind: WorkspaceTabKind | null;
}

export interface WorkspaceTabManagerOptions {
  /** Called after every state-mutating operation with the current snapshot. Optional. */
  onPersistChange?: (snapshot: WorkspaceTabPersistenceSnapshot) => void;
  /**
   * Called after every operation that changes which tab is active
   * (setActiveTab, addTab when the new tab auto-activates, closeTab when the
   * close advances the active id, clearAllTabs when it resets active). Round 4
   * Fix 4 (2026-05-21) — foundation signal for the future Assistant/Context
   * pane coordination contract. Not called during `restoreFromPersistence`
   * (restore is a read of an already-stored state, not a user-initiated
   * activation event).
   */
  onActiveTabChange?: (snapshot: ActiveTabSnapshot) => void;
}

/**
 * State record for a single workspace tab.
 *
 * `Component` is stored as `unknown` here because this is plain TS; the React
 * layer casts it to `React.ComponentType<WorkspaceWidgetProps>` before render.
 */
export interface WorkspaceTab {
  /** Stable unique id for this tab instance (generated by addTab / fixed for Home). */
  id: string;
  /** Discriminant — 'home' for the Home tab, 'widget' for all others. */
  kind: WorkspaceTabKind;
  /** Widget type string as sent by the server (e.g. "document-summary"). */
  widgetType: string;
  /** Widget payload delivered with the widget_load event. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  widgetData: any;
  /** Resolved React component — null while the registry promise is pending. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  Component: React.ComponentType<any> | null;
  /** True while resolveWorkspaceWidget() is still pending. */
  isLoading: boolean;
  /** Human-readable display label (falls back to widgetType if registry lacks displayName). */
  displayName: string;
  /**
   * R6 Pillar 9 visibility contract — when true, the tab's contents appear
   * in the per-turn agent prompt snapshot; when false, the tab is private
   * to the user. Defaults to `false` (user-created tabs); agent-created
   * tabs may default to `true`. Mutated by `setTabVisibility` (UI affordance
   * via AddToAssistantToggle).
   * @see Pillar 9 / FR-31 / task 098
   */
  visibleToAssistant: boolean;
}

// ---------------------------------------------------------------------------
// WorkspaceTabManagerState — snapshot returned to callers
// ---------------------------------------------------------------------------

/**
 * Immutable snapshot of tab manager state, returned by getSnapshot().
 * WorkspacePane spreads this into React state on every mutation.
 *
 * `tabs` includes the Home tab when present (always at index 0 by convention).
 */
export interface WorkspaceTabManagerState {
  tabs: WorkspaceTab[];
  activeTabId: string | null;
}

// ---------------------------------------------------------------------------
// Internal constants
// ---------------------------------------------------------------------------

/** Stable id used for the Home tab. Reserved — addTab() will reject this id. */
const HOME_TAB_ID = "home";

/** Default widgetType marker for the Home tab. */
const HOME_TAB_WIDGET_TYPE = "home";

// ---------------------------------------------------------------------------
// WorkspaceTabManager
// ---------------------------------------------------------------------------

/**
 * Plain TypeScript tab state manager — NO React, NO side effects.
 *
 * WorkspacePane instantiates this once in a ref and calls the mutating methods
 * in event handlers. After each mutation, getSnapshot() is called and the
 * result is spread into React's useState to trigger a re-render.
 */
export class WorkspaceTabManager {
  private _tabs: WorkspaceTab[] = [];
  private _activeTabId: string | null = null;
  private _nextSeq = 0;
  private _options: WorkspaceTabManagerOptions;

  /**
   * Construct a WorkspaceTabManager.
   *
   * @param options - Optional configuration. Use `onPersistChange` to receive a
   *                  persistence snapshot after every state-mutating operation
   *                  (NFR-09 write-through). The manager performs NO network
   *                  I/O — the caller debounces and dispatches.
   */
  constructor(options: WorkspaceTabManagerOptions = {}) {
    this._options = options;
  }

  /**
   * Emit a persistence snapshot to the onPersistChange callback if one is
   * registered. Centralised here so every mutation site has a single line of
   * write-through wiring.
   */
  private _notifyPersistChange(): void {
    if (!this._options.onPersistChange) return;
    this._options.onPersistChange(this.serializeForPersistence());
  }

  /**
   * Build an ActiveTabSnapshot for the current `_activeTabId`. Returns a
   * snapshot with all-null fields (except tabId) if there is no active tab.
   * Round 4 Fix 4 (2026-05-21) — kept private to centralise the snapshot shape
   * so consumers only ever see one canonical builder.
   */
  private _buildActiveTabSnapshot(): ActiveTabSnapshot {
    const id = this._activeTabId;
    if (!id) {
      return {
        tabId: null,
        widgetType: null,
        widgetData: null,
        displayName: null,
        kind: null,
      };
    }
    const tab = this._tabs.find((t) => t.id === id);
    if (!tab) {
      return {
        tabId: id,
        widgetType: null,
        widgetData: null,
        displayName: null,
        kind: null,
      };
    }
    return {
      tabId: tab.id,
      widgetType: tab.widgetType,
      widgetData: tab.widgetData,
      displayName: tab.displayName,
      kind: tab.kind,
    };
  }

  /**
   * Emit the active-tab snapshot to `onActiveTabChange` if a callback is
   * registered. Called by every mutation site that changes which tab is active.
   * Round 4 Fix 4 (2026-05-21).
   */
  private _notifyActiveTabChange(): void {
    if (!this._options.onActiveTabChange) return;
    this._options.onActiveTabChange(this._buildActiveTabSnapshot());
  }

  // -------------------------------------------------------------------------
  // ensureHomeTab — install / update the always-present Home tab
  // -------------------------------------------------------------------------

  /**
   * Install (or update) the always-present Home tab.
   *
   * - On first call, prepends a Home tab at index 0 of the tabs array. The
   *   Home tab is exempt from `MAX_WORKSPACE_TABS` and cannot be closed.
   * - On subsequent calls, updates the existing Home tab's displayName,
   *   widgetData, and resolved Component in place (no eviction, no reordering).
   * - The Home tab does not auto-activate when added; callers may call
   *   setActiveTab(homeTabId) explicitly if desired.
   *
   * @param displayName - Optional display label (defaults to 'Home').
   * @param widgetData  - Optional payload (e.g. layout metadata).
   * @param Component   - Optional resolved React component.
   * @returns The Home tab's stable id.
   */
  ensureHomeTab(
    displayName?: string,
    widgetData: unknown = null,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    Component: React.ComponentType<any> | null = null,
  ): string {
    const existingIdx = this._tabs.findIndex((t) => t.kind === "home");

    if (existingIdx >= 0) {
      const existing = this._tabs[existingIdx];
      const updated: WorkspaceTab = {
        ...existing,
        widgetData,
        Component: Component ?? existing.Component,
        isLoading: Component != null ? false : existing.isLoading,
        displayName: displayName ?? existing.displayName,
      };
      this._tabs = [
        ...this._tabs.slice(0, existingIdx),
        updated,
        ...this._tabs.slice(existingIdx + 1),
      ];
      return existing.id;
    }

    const homeTab: WorkspaceTab = {
      id: HOME_TAB_ID,
      kind: "home",
      widgetType: HOME_TAB_WIDGET_TYPE,
      widgetData,
      Component,
      isLoading: Component == null,
      displayName: displayName ?? "Home",
      // R6 Pillar 9 — Home is a default canvas tab; private by default.
      visibleToAssistant: false,
    };

    // Home always sits at index 0 by convention.
    this._tabs = [homeTab, ...this._tabs];
    return HOME_TAB_ID;
  }

  // -------------------------------------------------------------------------
  // addTab — add a widget tab (subject to FIFO eviction)
  // -------------------------------------------------------------------------

  /**
   * Add a new widget tab.
   *
   * If the count of existing non-Home tabs is at or above `MAX_WORKSPACE_TABS`,
   * the oldest non-Home tab (by insertion order) is silently evicted first
   * (FIFO). The Home tab — if present — is excluded from eviction candidates
   * and remains untouched.
   *
   * The new tab becomes the active tab.
   *
   * @param widgetType  - Widget type string from the server event.
   * @param widgetData  - Arbitrary payload to pass to the widget component.
   * @param displayName - Optional display label; defaults to widgetType.
   * @returns The new tab's stable id.
   */
  addTab(widgetType: string, widgetData: unknown, displayName?: string): string {
    // Enforce MAX_WORKSPACE_TABS — evict the oldest non-Home tab when at cap.
    const nonHomeIndices = this._tabs
      .map((t, i) => (t.kind === "widget" ? i : -1))
      .filter((i) => i >= 0);

    if (nonHomeIndices.length >= MAX_WORKSPACE_TABS) {
      const oldestNonHomeIdx = nonHomeIndices[0];
      this._tabs = [
        ...this._tabs.slice(0, oldestNonHomeIdx),
        ...this._tabs.slice(oldestNonHomeIdx + 1),
      ];
    }

    const id = `wstab-${++this._nextSeq}-${widgetType}`;

    const newTab: WorkspaceTab = {
      id,
      kind: "widget",
      widgetType,
      widgetData,
      Component: null,
      isLoading: true,
      displayName: displayName ?? widgetType,
      // R6 Pillar 9 default: user-created tabs are private by default.
      // The agent toggles to true via the "Add to Assistant" affordance.
      visibleToAssistant: false,
    };

    this._tabs = [...this._tabs, newTab];
    this._activeTabId = id;

    this._notifyPersistChange();
    // Round 4 Fix 4: addTab always auto-activates the new tab, so emit the
    // active-tab change signal too. Order matters — emit AFTER state is set.
    this._notifyActiveTabChange();
    return id;
  }

  // -------------------------------------------------------------------------
  // prependTab — add a widget tab at the FIRST position (R5 task 038)
  // -------------------------------------------------------------------------

  /**
   * Add a new widget tab AT THE FIRST POSITION (after the Home tab if one
   * exists). Used by R5 task 038 to install the always-leftmost "Summary"
   * tab that hosts `StructuredOutputStreamWidget` for chat-driven streaming
   * AI output.
   *
   * Like `addTab`, this enforces `MAX_WORKSPACE_TABS` by FIFO-evicting the
   * OLDEST non-Home tab — the newly prepended tab itself is exempt from the
   * eviction-candidate set. Unlike `addTab`, the new tab does NOT auto-
   * activate. Callers that want it active call `setActiveTab(newId)` after.
   *
   * @param widgetType   - Widget type string (e.g. `"structured-output-stream"`).
   * @param widgetData   - Arbitrary payload to pass to the widget component.
   * @param displayName  - Optional display label; defaults to widgetType.
   * @returns The new tab's stable id.
   */
  prependTab(widgetType: string, widgetData: unknown, displayName?: string): string {
    // Enforce MAX_WORKSPACE_TABS — evict the oldest non-Home tab when at cap.
    // Note: identical policy to addTab(); the new prepended tab is exempt from
    // the eviction candidate set because it is not yet in `_tabs`.
    const nonHomeIndices = this._tabs
      .map((t, i) => (t.kind === "widget" ? i : -1))
      .filter((i) => i >= 0);

    if (nonHomeIndices.length >= MAX_WORKSPACE_TABS) {
      const oldestNonHomeIdx = nonHomeIndices[0];
      this._tabs = [
        ...this._tabs.slice(0, oldestNonHomeIdx),
        ...this._tabs.slice(oldestNonHomeIdx + 1),
      ];
    }

    const id = `wstab-${++this._nextSeq}-${widgetType}`;

    const newTab: WorkspaceTab = {
      id,
      kind: "widget",
      widgetType,
      widgetData,
      Component: null,
      isLoading: true,
      displayName: displayName ?? widgetType,
      // R6 Pillar 9 default: user-created tabs are private by default.
      // The agent toggles to true via the "Add to Assistant" affordance.
      visibleToAssistant: false,
    };

    // Insert AFTER any Home tab (which by convention sits at index 0) so the
    // new tab is the FIRST widget tab. With no Home, it goes to index 0.
    const homeIdx = this._tabs.findIndex((t) => t.kind === "home");
    const insertAt = homeIdx >= 0 ? homeIdx + 1 : 0;
    this._tabs = [
      ...this._tabs.slice(0, insertAt),
      newTab,
      ...this._tabs.slice(insertAt),
    ];

    // Persist the new state. Unlike addTab, do NOT auto-activate — callers
    // (e.g. WorkspacePane's Summary-tab auto-install effect) decide whether
    // the prepended tab should also become active.
    this._notifyPersistChange();
    return id;
  }

  // -------------------------------------------------------------------------
  // resolveTabComponent
  // -------------------------------------------------------------------------

  /**
   * Set the resolved React component for a tab, clearing its loading state.
   *
   * Called by WorkspacePane after the WorkspaceWidgetRegistry promise resolves.
   * A no-op if the tab no longer exists (e.g. user closed it before resolution).
   *
   * @param tabId     - The tab id returned by addTab.
   * @param Component - The resolved React component type.
   * @param displayName - Optional updated display name from registry metadata.
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  resolveTabComponent(tabId: string, Component: React.ComponentType<any>, displayName?: string): void {
    this._tabs = this._tabs.map((t) =>
      t.id === tabId
        ? { ...t, Component, isLoading: false, displayName: displayName ?? t.displayName }
        : t
    );
  }

  // -------------------------------------------------------------------------
  // updateTab
  // -------------------------------------------------------------------------

  /**
   * Update an existing tab's data payload (widget_update event).
   *
   * A no-op if the tab id is not found — callers do not need to guard against
   * stale updates.
   *
   * @param tabId      - The id of the tab to update.
   * @param widgetData - New data payload to pass to the widget component.
   */
  updateTab(tabId: string, widgetData: unknown): void {
    this._tabs = this._tabs.map((t) =>
      t.id === tabId ? { ...t, widgetData } : t
    );
  }

  // -------------------------------------------------------------------------
  // setTabVisibility (R6 Pillar 9 / task 098 — user "Add to Assistant" toggle)
  // -------------------------------------------------------------------------

  /**
   * Flip a tab's `visibleToAssistant` flag. Used by the per-tab
   * `AddToAssistantToggle` UI affordance + by future server-driven
   * visibility updates (workspace state restore).
   *
   * No-op if the tab id is not found — callers do not need to guard against
   * stale updates.
   *
   * @param tabId             - The id of the tab to update.
   * @param visibleToAssistant - New visibility value.
   */
  setTabVisibility(tabId: string, visibleToAssistant: boolean): void {
    this._tabs = this._tabs.map((t) =>
      t.id === tabId ? { ...t, visibleToAssistant } : t
    );
  }

  // -------------------------------------------------------------------------
  // closeTab
  // -------------------------------------------------------------------------

  /**
   * Remove a tab by id.
   *
   * The Home tab cannot be closed — callers passing the Home id receive the
   * current active tab id and no state change occurs.
   *
   * If the closed tab was the active one, the manager selects the next tab to
   * the right, or the previous tab if there is no right neighbour, or null if
   * the tab list becomes empty.
   *
   * @param tabId - The id of the tab to close.
   * @returns The id of the tab that became active after closing, or null.
   */
  closeTab(tabId: string): string | null {
    const idx = this._tabs.findIndex((t) => t.id === tabId);
    if (idx === -1) return this._activeTabId;

    // Home is non-closable.
    if (this._tabs[idx].kind === "home") return this._activeTabId;

    const wasActive = this._activeTabId === tabId;
    this._tabs = this._tabs.filter((t) => t.id !== tabId);

    if (!wasActive) {
      this._notifyPersistChange();
      return this._activeTabId;
    }

    // Select successor: prefer right neighbour, then left, then null.
    if (this._tabs.length === 0) {
      this._activeTabId = null;
    } else if (idx < this._tabs.length) {
      this._activeTabId = this._tabs[idx].id;
    } else {
      this._activeTabId = this._tabs[this._tabs.length - 1].id;
    }

    this._notifyPersistChange();
    // Round 4 Fix 4: closing the active tab always changes which tab is
    // active (to a successor or null), so emit the active-tab change signal.
    this._notifyActiveTabChange();
    return this._activeTabId;
  }

  // -------------------------------------------------------------------------
  // setActiveTab
  // -------------------------------------------------------------------------

  /**
   * Change the active tab.
   *
   * A no-op if the tab id is not in the current tab list.
   *
   * @param tabId - The id of the tab to activate.
   */
  setActiveTab(tabId: string): void {
    if (this._tabs.some((t) => t.id === tabId)) {
      const changed = this._activeTabId !== tabId;
      this._activeTabId = tabId;
      this._notifyPersistChange();
      // Round 4 Fix 4: only emit when the active id actually changed —
      // setActiveTab(currentActiveId) is a no-op for cross-pane subscribers.
      if (changed) this._notifyActiveTabChange();
    }
  }

  // -------------------------------------------------------------------------
  // getActiveTab
  // -------------------------------------------------------------------------

  /**
   * Return the currently active tab record, or null if no tabs exist.
   */
  getActiveTab(): WorkspaceTab | null {
    return this._tabs.find((t) => t.id === this._activeTabId) ?? null;
  }

  // -------------------------------------------------------------------------
  // clearAllTabs (AIPU2-102 — exclusive playbook selection)
  // -------------------------------------------------------------------------

  /**
   * Remove all **non-Home** tabs and reset active tab.
   *
   * Called by WorkspacePane when a `playbook-selected` event arrives with
   * `isExclusive === true`. Exclusive playbooks enforce a clean slate — the
   * workspace's widget tabs are reset before seeding the playbook's
   * defaultWidgets.
   *
   * The Home tab — if present — is preserved. If the Home tab was active
   * before this call, it remains active; otherwise activeTabId becomes the
   * Home tab id (if Home is present) or null.
   *
   * @returns The number of (non-Home) tabs that were removed.
   */
  clearAllTabs(): number {
    const homeTab = this._tabs.find((t) => t.kind === "home") ?? null;
    const removedCount = this._tabs.filter((t) => t.kind === "widget").length;
    const previousActiveId = this._activeTabId;

    this._tabs = homeTab ? [homeTab] : [];
    this._activeTabId = homeTab ? homeTab.id : null;

    this._notifyPersistChange();
    // Round 4 Fix 4: emit only when the active id actually changed (e.g.
    // clearing while a widget tab was active drops back to Home / null).
    if (previousActiveId !== this._activeTabId) {
      this._notifyActiveTabChange();
    }
    return removedCount;
  }

  // -------------------------------------------------------------------------
  // serializeForPersistence — NFR-09 write-through (task 065)
  // -------------------------------------------------------------------------

  /**
   * Returns a serializable snapshot of NON-HOME tabs only.
   *
   * - The Home tab is excluded — it is recreated by ensureHomeTab() on every
   *   WorkspacePane mount and never round-trips through the persistence layer.
   * - React Component references are excluded — they are re-resolved through
   *   `resolveWorkspaceWidget(widgetType)` on restore.
   * - `activeTabId` passes through unchanged — it may be the Home tab id, a
   *   widget id, or null. On restore, WorkspacePane decides whether to honor
   *   it (it does only if the id matches one of the restored widget tabs).
   *
   * No side effects; safe to call from inside React render or effects.
   */
  serializeForPersistence(): WorkspaceTabPersistenceSnapshot {
    const tabs: SerializableWorkspaceTab[] = this._tabs
      .filter((t) => t.kind === "widget")
      .map((t) => ({
        id: t.id,
        widgetType: t.widgetType,
        widgetData: t.widgetData,
        displayName: t.displayName,
      }));

    return {
      tabs,
      activeTabId: this._activeTabId,
    };
  }

  // -------------------------------------------------------------------------
  // restoreFromPersistence — NFR-09 restore on mount (task 065)
  // -------------------------------------------------------------------------

  /**
   * Replace non-Home tabs from a persisted snapshot.
   *
   * Semantics:
   *   - No-op if the manager already has at least one non-Home tab (don't
   *     clobber an in-flight active session).
   *   - Each snapshot tab's `widgetType` is resolved via the provided
   *     `resolveWidget` factory. Tabs whose widget cannot be resolved
   *     (e.g. widget unregistered, lazy-import error) are skipped — restore
   *     degrades gracefully rather than throwing.
   *   - Restored tabs are added in insertion order via direct splice into
   *     `_tabs` (bypassing addTab so the FIFO eviction logic does NOT fire
   *     and so the onPersistChange callback does NOT re-emit during restore).
   *   - `activeTabId` is set only if it matches one of the restored tab ids
   *     (or the existing Home tab) — otherwise the current active tab is
   *     preserved (or null if no Home tab exists either).
   *   - This method does NOT call onPersistChange — restore is a read of an
   *     already-persisted snapshot; calling write-through would create a
   *     spurious save on mount.
   *
   * @param snapshot     The persisted snapshot, typically returned by GET
   *                     `/api/ai/chat/sessions/{sessionId}/tabs`.
   * @param resolveWidget Async factory that returns the React component for
   *                      a widgetType, or null if not registered. Typically
   *                      `resolveWorkspaceWidget` from `@spaarke/ai-widgets`.
   */
  async restoreFromPersistence(
    snapshot: WorkspaceTabPersistenceSnapshot,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    resolveWidget: (widgetType: string) => Promise<React.ComponentType<any> | null>,
  ): Promise<void> {
    // Guard: don't clobber an active session.
    const hasNonHomeTab = this._tabs.some((t) => t.kind === "widget");
    if (hasNonHomeTab) return;

    if (!snapshot || !Array.isArray(snapshot.tabs)) return;

    // Resolve all widget components in parallel; preserve original order in result.
    const resolutions = await Promise.all(
      snapshot.tabs.map(async (t) => {
        try {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const Component = await resolveWidget(t.widgetType);
          return { t, Component };
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
        } catch (_err) {
          // Unresolvable widget — skip gracefully (see method JSDoc).
          return { t, Component: null };
        }
      }),
    );

    const restoredTabs: WorkspaceTab[] = [];
    for (const { t, Component } of resolutions) {
      if (Component == null) continue; // skip unresolvable widgets
      // Bump _nextSeq so any subsequent addTab() generates a non-colliding id.
      this._nextSeq++;
      restoredTabs.push({
        id: t.id,
        kind: "widget",
        widgetType: t.widgetType,
        widgetData: t.widgetData,
        Component,
        isLoading: false,
        displayName: t.displayName,
        // R6 Pillar 9 default: restored tabs default to private. Server-side
        // visibility state is reconciled by the workspace-state fetch path.
        visibleToAssistant: false,
      });
    }

    // Splice restored tabs in AFTER any existing Home tab so Home stays at idx 0.
    this._tabs = [...this._tabs, ...restoredTabs];

    // Honor snapshot.activeTabId only if it matches a tab now in the manager.
    if (
      snapshot.activeTabId != null &&
      this._tabs.some((t) => t.id === snapshot.activeTabId)
    ) {
      this._activeTabId = snapshot.activeTabId;
    }
    // NOTE: intentionally NO _notifyPersistChange() — restore is a read.
  }

  // -------------------------------------------------------------------------
  // getSnapshot
  // -------------------------------------------------------------------------

  /**
   * Return an immutable state snapshot.
   *
   * WorkspacePane calls this after every mutation and spreads the result into
   * React state to trigger a re-render. The snapshot is a shallow copy —
   * mutating the returned arrays does not affect the manager's internal state.
   */
  getSnapshot(): WorkspaceTabManagerState {
    return {
      tabs: [...this._tabs],
      activeTabId: this._activeTabId,
    };
  }
}
