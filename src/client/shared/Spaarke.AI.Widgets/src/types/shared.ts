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
// WidgetContextType (FR-B1 + FR-C3 — task 020)
// ---------------------------------------------------------------------------

/**
 * CLOSED set of "what kind of workspace surface is this" tags for a
 * registered widget. Exactly these six values — do not widen ad hoc.
 *
 * This is a DISTINCT concept from the free-form `contextType?: string` on
 * `ContextPaneEvent` (`events/PaneEventTypes.ts`, the Context-pane channel's
 * payload classifier, e.g. `"document"` / `"email"` / `"clause"`). That field
 * is per-EVENT and open-ended; `WidgetContextType` is per-WIDGET-TYPE and
 * closed. Do not conflate or reuse one for the other.
 *
 * Feeds FR-B1's proactive-chip scoping (Workstream B): the active workspace
 * tab's declared `contextType` lets chip selection scope itself to "this tab
 * is a document" / "this tab is an email" etc. Per ADR-039, this is DATA
 * carried on the (already-closed) widget-registry catalog — never a
 * classifier, reranker, or second intent-detection mechanism.
 *
 * @see WidgetMetadata.contextType — where this is declared per widget
 * @see register-workspace-widgets.ts — per-widget assignment
 */
export type WidgetContextType = 'email' | 'document' | 'compose-doc' | 'matter-grid' | 'dashboard' | 'calendar';

// ---------------------------------------------------------------------------
// WidgetAssistantContract (R3 spaarkeai-assistant-enhancements task 022,
// FR-08 + FR-15 SHAPE)
// ---------------------------------------------------------------------------

/**
 * Canonical catalog tool name for the FR-06/task-020 parameterized
 * `configId`-driven overview tool — ONE tool reused across every grid plus
 * the Daily Briefing/Calendar dashboard tabs (NFR-06: no per-grid handlers).
 * Every overview-only widget's `assistantContract.overviewTools` references
 * this ONE literal so there is a single place to update if the BFF catalog
 * row (task 020) lands under a different name.
 */
export const OVERVIEW_QUERY_TOOL_NAME = 'overview_query' as const;

/**
 * Where a per-item card's tool output lands once invoked (FR-09/FR-10/FR-11).
 *  - `'chat'`     — the tool answers inline in the conversation transcript
 *                   (e.g. "Summarize the thread" / "Summarize" a document).
 *  - `'composer'` — opens the existing email composer (`SendEmailDialog` via
 *                   `useEmailComposeActions.openComposer`) pre-filled.
 *  - `'compose'`  — opens the Compose (ADR-049) drafting surface.
 */
export type AssistantCardLanding = 'chat' | 'composer' | 'compose';

/**
 * respond | direct | hybrid (FR-13). A DATA field the deterministic
 * follow-on derivation (FR-14, task 041) reads — never a classifier and
 * never model-interpreted prompt prose (ADR-039 invariant: no second
 * intent-detection mechanism).
 *
 *  - `'respond'` — the widget only ever answers IN CHAT. Covers BOTH an
 *    overview/query surface with no per-item action cards of its own (e.g. a
 *    grid — `perItemCards: []`) AND a per-item surface whose cards ALL land
 *    `'chat'` (e.g. `document-viewer` — see task 040/D-8: every card answers
 *    in chat, so the widget is `'respond'` even though it has cards).
 *  - `'direct'`  — the widget's tools always open ANOTHER surface (composer
 *    or Compose) with no standalone chat answer — every `perItemCards` entry
 *    has `landing !== 'chat'`.
 *  - `'hybrid'`  — the widget mixes both: at least one per-item card answers
 *    in chat (e.g. Summarize) AND at least one opens another surface (e.g.
 *    Reply, Draft memo). E.g. `email` — Reply/Reply All/Forward land
 *    `'composer'`, Summarize the thread lands `'chat'`.
 *
 * INVARIANT (task 040, made authoritative + single-sourced): this field MUST
 * agree with the widget's own `perItemCards[].landing` values per the three
 * rules above — it is a derived-but-declared summary of the cards, not an
 * independent guess. `WorkspaceWidgetRegistry.interactionPattern.test.ts`'s
 * "interactionPattern is consistent with perItemCards landings" suite
 * enforces this for every registered widget so the two never drift.
 *
 * task 022 defined this field on the SHAPE and best-effort populated it for
 * the in-scope widgets ahead of tasks 025/026 landing the concrete per-item
 * card implementations. task 040 is the single-sourcing checkpoint that
 * reconciled every value against the shipped card `landing`s (`email` was
 * already correct at `'hybrid'`; `document-viewer` was corrected from its
 * task-022 placeholder `'hybrid'` to `'respond'` once task 026 finalized all
 * three document cards to land `'chat'` — see `register-document-viewer-widget.ts`).
 * Read this field ONLY via `getWidgetInteractionPattern()` /
 * `getWidgetAssistantContract().interactionPattern` (`WorkspaceWidgetRegistry.ts`)
 * — never re-derive or hardcode it per-widget-type at a call site (task 041's
 * follow-on derivation is the first runtime consumer).
 */
export type AssistantInteractionPattern = 'respond' | 'direct' | 'hybrid';

/**
 * One per-item action card declared on a widget's Assistant contract.
 *
 * Per ASSISTANT-UI-ELEMENT-CRITERIA.md, per-item actions on an active item
 * are CARDS (never chips) — this array enumerates card actions only.
 *
 * NEVER carries item content (ADR-015 — identity + capability metadata
 * only): a human-facing label, a catalog tool NAME, and a landing tag.
 */
export interface AssistantContractCard {
  /**
   * Human-facing card label shown in the workspace pane, e.g. `"Reply"`,
   * `"Summarize the thread"`. MUST NOT contain item content (a subject line,
   * a body excerpt, etc.) — labels are static per-widget-type strings.
   */
  readonly label: string;
  /**
   * A stable identifier for what this card does, in one of two forms
   * (task 040 reconciliation — both are legitimate, honest shapes; a reader
   * MUST check which one a given widget uses before assuming catalog-tool
   * semantics):
   *
   *  1. An ADR-039 closed-catalog TOOL NAME the LLM can invoke (e.g.
   *     `'draft_reply'`, `'summarize_thread'` on `email` — real
   *     `EmailDraftToolHandler` catalog rows, task 023). The LLM never
   *     invokes an unlisted tool — for these widgets this string MUST match
   *     a registered catalog Tool.
   *  2. A stable CLIENT-SIDE KEY with no catalog-tool backing (e.g.
   *     `'summarize_document'`, `'draft_document_response'`,
   *     `'draft_document_memo'` on `document-viewer` — documents have no
   *     deterministic REST/tool entry point). `DocumentPerItemCards.tsx`
   *     maps each key to a fixed chat instruction sent through the existing
   *     `pendingOutboundMessage` seam with the document's id armed
   *     one-shot (ADR-015) — see that file's docblock. This form never
   *     appears as an invocable name in the tool catalog.
   */
  readonly tool: string;
  /** Where the tool's output lands once invoked. */
  readonly landing: AssistantCardLanding;
}

/**
 * The Assistant-contract metadata SHAPE (FR-08 + FR-15). Declared per widget
 * as the OPTIONAL `WidgetMetadata.assistantContract` field, additive
 * alongside the existing `WidgetMetadata.contextType` (task 020):
 *
 *   1. context-type             — `WidgetMetadata.contextType` (existing;
 *                                  NOT duplicated inside this object — read
 *                                  the two fields together).
 *   2. overview tool(s)         — `overviewTools`.
 *   3. per-item cards + landing — `perItemCards`.
 *   4. interaction pattern      — `interactionPattern`.
 *
 * OPTIONAL at this stage: task 022 DEFINES the shape and POPULATES it for
 * the in-scope widgets named in spec FR-08/FR-09/FR-11 (grids, Daily
 * Briefing, Calendar, Email, Documents). task 050 makes this field
 * structurally REQUIRED and adds the registry enforcement guard (FR-15) so
 * no widget can ship without a contract. This task does NOT implement that
 * enforcement — see FR-15 / task 050.
 *
 * Every field here is identity/capability metadata (a label, a tool NAME, a
 * landing tag) — NEVER item content (ADR-015).
 *
 * @see WidgetMetadata.contextType — the existing closed context-type union
 *      (task 020) this contract is read alongside.
 * @see WidgetMetadata.assistantContract — where this is attached per widget.
 */
export interface WidgetAssistantContract {
  /**
   * Overview/query tool name(s) that answer chat questions for this surface
   * from its own data lane (FR-06/FR-07). Empty array = this widget has no
   * overview tool of its own (e.g. a per-item-only surface whose overview
   * lives on a sibling grid widget — see `email`/`document-viewer` below).
   *
   * `readonly` — this is static catalog-shaped declarative data (ADR-039),
   * often a SHARED object reused by reference across multiple registrations
   * (e.g. every overview-only grid); it is never mutated after registration.
   */
  readonly overviewTools: readonly string[];
  /**
   * Per-item action cards for the widget's active item (FR-09/FR-10/FR-11).
   * Empty array = an overview-only surface with no per-item cards.
   *
   * `readonly` — see `overviewTools` above.
   */
  readonly perItemCards: readonly AssistantContractCard[];
  /** respond | direct | hybrid (FR-13) — see `AssistantInteractionPattern`. */
  readonly interactionPattern: AssistantInteractionPattern;
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

  /**
   * OPTIONAL closed-set tag (FR-B1 + FR-C3, task 020) classifying what kind
   * of workspace surface this widget represents. Omit when the widget has no
   * honest fit among the six values — `undefined` here means "none", not an
   * authoring gap.
   *
   * @see WidgetContextType for the closed set + rationale.
   */
  contextType?: WidgetContextType;

  /**
   * OPTIONAL (R3 task 022, FR-08 + FR-15 SHAPE) Assistant-contract metadata:
   * the overview tool(s), per-item cards + landing target, and interaction
   * pattern this widget declares to the Assistant. Additive sibling to
   * `contextType` above (context-type stays that existing field — not
   * duplicated inside this object). Optional here; task 050 (FR-15
   * enforcement) makes it REQUIRED and adds a registry guard so no widget
   * can ship without a contract.
   *
   * @see WidgetAssistantContract for the full shape + field-by-field rationale.
   */
  assistantContract?: WidgetAssistantContract;
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
