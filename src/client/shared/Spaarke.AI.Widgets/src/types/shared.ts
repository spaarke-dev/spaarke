/**
 * @spaarke/ai-widgets — Shared supporting types
 *
 * These types are used by both WorkspaceWidget and ContextWidget interfaces,
 * as well as the pane orchestration layer.
 *
 * Task: AIPU2-071
 */

import type React from 'react';

// ---------------------------------------------------------------------------
// WidgetRenderContext
// ---------------------------------------------------------------------------

/**
 * Runtime context injected into every widget render call.
 *
 * Carries ambient session and entity information that widgets may need to
 * fetch data or resolve labels without coupling to the shell's React context.
 */
export interface WidgetRenderContext {
  /**
   * The active AI session identifier. Maps to the Cosmos DB session document
   * and the Azure OpenAI thread ID.
   */
  sessionId: string;

  /**
   * The Dataverse entity record that anchors the current workspace session
   * (e.g. Matter, Contract). Shape depends on the active playbook.
   *
   * Widgets MUST treat this as read-only and MUST NOT mutate it.
   */
  entityContext: Record<string, unknown>;

  /**
   * Fluent UI v9 theme tokens in effect for the current session.
   * Widgets MUST use theme tokens for all colors — no hard-coded values (ADR-021).
   */
  theme: 'light' | 'dark' | 'high-contrast';
}

// ---------------------------------------------------------------------------
// Selection
// ---------------------------------------------------------------------------

/**
 * Describes a user selection within a widget.
 *
 * Dispatched via `WorkspaceWidget.onSelectionChange` when the user highlights
 * text or selects a structured item in the workspace. The shell broadcasts
 * this to the Context pane so citations and sources can synchronize.
 */
export interface Selection {
  /**
   * The kind of selection made.
   * - `text` — a contiguous span of rendered text.
   * - `item` — a structured data record (row, card, citation).
   * - `range` — a numeric or date range within a chart or timeline widget.
   */
  selectionType: 'text' | 'item' | 'range';

  /**
   * Identifiers of the selected items.
   * For `text` selections this is the list of paragraph/chunk IDs that fall
   * within the selection. For `item` selections it is the record IDs.
   */
  selectedIds: string[];

  /**
   * Optional free-form metadata about the selection.
   * For `text` selections, include `{ selectedText, startOffset, endOffset }`.
   * Widgets MAY include additional keys relevant to their domain.
   */
  metadata?: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// ActionResult
// ---------------------------------------------------------------------------

/**
 * Result returned to the shell via `WorkspaceWidget.onActionComplete` after a
 * user-initiated action (save, export, apply, etc.) completes.
 *
 * @template TActions - The action-key union for the widget (e.g. `'save' | 'export'`).
 *   When not constrained, defaults to `string`.
 */
export interface ActionResult<TActions extends string = string> {
  /**
   * Whether the action completed successfully.
   * On failure the shell may display a generic error banner unless `errorMessage`
   * provides a user-facing reason.
   */
  success: boolean;

  /**
   * The identifier of the action that completed.
   * MUST match one of the keys declared in the widget's `TActions` type.
   */
  action: TActions;

  /**
   * Optional payload returned by the action.
   * Shape is widget-specific; the shell treats this as opaque data.
   */
  payload?: unknown;

  /**
   * Human-readable error message surfaced in the UI when `success` is false.
   * MUST NOT contain stack traces or internal identifiers.
   */
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// WidgetState
// ---------------------------------------------------------------------------

/**
 * Serialized state snapshot persisted to Cosmos DB as part of the work history.
 *
 * IMPORTANT (D-08 — data-refreshed restore): `WidgetState` stores the *query
 * parameters* needed to re-fetch data, NOT the data itself. On restore the
 * widget calls `restoreState(state)`, re-issues its data fetch, and renders
 * fresh results. Stale data snapshots are never rehydrated directly.
 *
 * @template TData - The data shape the widget fetches. Used only to type
 *   `queryParams` loosely; actual data is never stored here.
 */
export interface WidgetState<TData = unknown> {
  /**
   * Discriminator string that identifies the widget type.
   * MUST match the key used in the WorkspaceWidgetRegistry.
   * Example: `"redline-viewer"`, `"budget-dashboard"`.
   */
  widgetType: string;

  /**
   * Schema version for this state shape. Increment when the `queryParams`
   * structure changes to allow forward-migration on restore.
   */
  version: number;

  /**
   * Query parameters needed to re-fetch data on restore.
   * All values are serialized as strings for Cosmos DB compatibility.
   *
   * Example: `{ documentId: "abc123", comparisonDocumentId: "def456" }`
   */
  queryParams: Record<string, string>;

  /**
   * Optional saved layout hint (e.g. scroll position, active tab index).
   * The shell passes this back in `restoreState` so widgets can restore UX state
   * without re-fetching. MUST be serializable to JSON.
   */
  layout?: Record<string, unknown>;

  /**
   * Phantom field to bind the `TData` generic and enable type inference at
   * call sites. This field is NEVER set at runtime — it is `undefined` always.
   * @internal
   */
  readonly _dataShape?: TData;

  /**
   * ISO 8601 timestamp when this state was serialized.
   * Used by the shell to display "last saved" labels in work history.
   */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// WidgetMetadata
// ---------------------------------------------------------------------------

/**
 * Static metadata about a widget type, stored in the registry alongside the
 * lazy loader. Used by the shell to build tab headers, capability menus, and
 * the workspace layout wizard.
 */
export interface WidgetMetadata {
  /**
   * Human-readable name shown in tab headers and capability menus.
   * Example: `"Redline Viewer"`, `"Budget Dashboard"`.
   */
  displayName: string;

  /**
   * Logical grouping for the capability menu UI.
   * Example: `"Documents"`, `"Finance"`, `"Research"`.
   */
  category: string;

  /**
   * Optional icon name from the Fluent UI icon set.
   * If omitted, the shell renders a generic widget icon.
   * Example: `"DocumentSearch24Regular"`.
   */
  icon?: string;

  /**
   * Whether this widget type may appear in multiple simultaneous tabs.
   * Set to `true` for context-specific widgets (e.g. document viewers where
   * each tab shows a different document). Set to `false` for singletons
   * (e.g. a global budget dashboard).
   */
  allowMultiple: boolean;

  /**
   * Default render order when the shell auto-opens multiple widgets.
   * Lower numbers appear earlier (leftmost tab). Ties are resolved by
   * registration order.
   */
  defaultOrder: number;
}

// ---------------------------------------------------------------------------
// WidgetRegistryEntry
// ---------------------------------------------------------------------------

/**
 * A complete registry entry binding widget metadata to its lazy-loaded
 * React component.
 *
 * Stored in `WorkspaceWidgetRegistry` and `ContextWidgetRegistry`.
 * The `load` factory is called exactly once per widget type; subsequent
 * renders use the resolved component from the module cache.
 */
export interface WidgetRegistryEntry {
  /** Static metadata used for tab headers, capability menus, and layout. */
  metadata: WidgetMetadata;

  /**
   * Lazy loader returning the widget's default-exported React component.
   * MUST use `import()` to enable code-splitting.
   *
   * The component type is intentionally `React.ComponentType<unknown>` here;
   * the registry consumer casts to the appropriate typed props before rendering.
   *
   * @example
   * load: () => import('../workspace-widgets/RedlineViewerWidget')
   */
  load: () => Promise<{ default: React.ComponentType<unknown> }>;
}
