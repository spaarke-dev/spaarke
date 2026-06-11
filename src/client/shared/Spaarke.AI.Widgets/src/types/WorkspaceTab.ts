/**
 * @spaarke/ai-widgets — Canonical WorkspaceTab interface (R6 Pillar 6a)
 *
 * The contract shared by:
 *   - Pillar 6a (state model + Redis/Cosmos persistence + GET /api/workspace/state)
 *   - Pillar 6b (chat tools that mutate tabs: send_workspace_artifact,
 *                update_workspace_tab, close_workspace_tab)
 *   - Pillar 6c (workspace events, execution-trace widget, additive `workspace.*`
 *                events: user_selection, tab_edited, tab_focused, tab_provenance_clicked)
 *   - Pillar 7  (memory composition reads workspace state to compose per-turn snapshot)
 *   - Pillar 9  (widget visibility contract — `visibleToAssistant` filter + per-tab
 *                `getAgentVisibleState()` schema-aware serialization)
 *
 * Pillar 6a is the GATE task — drift in this interface breaks four downstream pillars.
 *
 * ## Design Notes
 *
 * ### widgetType vs registry widget type
 * `WorkspaceTab.widgetType` is a closed union of FOUR **agent-visible state categories**
 * (Summary, DocumentViewer, Dashboard, Table) per Pillar 9 / CLAUDE.md §9. This is
 * intentionally DISTINCT from the `WorkspaceWidgetRegistry` widget-type string (which
 * has 16+ concrete entries like `'workspace'`, `'redline-viewer'`, `'document-viewer'`,
 * etc.). The registry string drives lazy component resolution; this categorical union
 * drives:
 *   (a) the agent's view of what KIND of state lives in the tab (Pillar 9 prompt builder), and
 *   (b) per-variant `widgetData` typing via discriminated union (this file).
 *
 * A future registry registration can map any concrete widget to one of these four
 * categories via metadata; the mapping is the bridge between the registry's component
 * dispatch and Pillar 9's agent-visibility contract.
 *
 * ### Discriminated union
 * `widgetData` is narrowed by `widgetType`. An exhaustive `switch (tab.widgetType)` MUST
 * cover all four variants; missing a case is a TS compile error (see acceptance test
 * in task 050 evidence note).
 *
 * ### Q8 conflict resolution
 * `lastUserEditAt` (optional ISO-8601 timestamp) is central to user-wins conflict
 * resolution: Pillar 6b's `update_workspace_tab` chat tool MUST check this before
 * mutating; if the tool's read timestamp is older than `lastUserEditAt`, the tool
 * refuses and asks the agent to re-read state. New tabs may have this field undefined
 * (no user edits yet) — typed optional to support that case.
 *
 * ### Pillar 9 default `visibleToAssistant`
 * The Pillar 9 visibility contract (CLAUDE.md §9 "Privacy default") specifies:
 *   - Agent-created tabs default `visibleToAssistant: true`
 *   - User-created tabs default `visibleToAssistant: false`
 *   - Override via "Add to Assistant" toggle (Pillar 6b user affordance)
 * This field is REQUIRED on the interface so producers MUST decide explicitly.
 *
 * ### Provenance
 * `sourceProvenance` records WHO created the tab + WHEN. Pillar 6c uses this for
 * the execution-trace widget (clicking a tab provenance opens the original
 * playbook/agent context). Per ADR-015 ("AI data governance") the `createdBy` field
 * MUST be a deterministic ID (matterId, scopeId, agentId, userId) — NOT user
 * message text.
 *
 * @see {@link https://...} FR-31 — canonical WorkspaceTab interface (R6 spec)
 * @see Pillar 6a project gate (CLAUDE.md project file §"Per-Pillar Binding Rules")
 * @see ADR-012 — shared library placement (`@spaarke/ai-widgets`)
 * @see ADR-030 — additive event types on `workspace.*` channel (no 5th channel)
 * @see ADR-015 — AI data governance (`createdBy` deterministic IDs only)
 */

// ---------------------------------------------------------------------------
// WidgetType discriminator (Pillar 9 visibility categories)
// ---------------------------------------------------------------------------

/**
 * Closed union of agent-visible widget categories per Pillar 9.
 *
 * Each variant maps to a specific `widgetData` shape AND a specific
 * `getAgentVisibleState()` return-shape (Pillar 9 prompt builder). Adding a
 * fifth variant requires a coordinated update to Pillar 6a/6b/6c/7/9 — DO NOT
 * extend this union without surfacing the cross-pillar impact.
 *
 * Variants:
 *   - `Summary`        — text/markdown summary output (e.g. TL;DR, summarize-document).
 *                        Pillar 9 visible state: `{ widgetType, summary, tldr, hasUserEdits }`.
 *   - `DocumentViewer` — file preview / document body (PDF, DOCX, etc.).
 *                        Pillar 9 visible state: `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }`.
 *   - `Dashboard`      — composable section grid (LegalWorkspaceApp embedded mode).
 *                        Pillar 9 visible state: `{ widgetType, dashboardName, lastViewedSection }`
 *                        (deliberately NOT chart data — payload minimization per NFR-10).
 *   - `Table`          — tabular data with sort/filter (e.g. matter list, document grid).
 *                        Pillar 9 visible state: `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows[] }`.
 *
 * @see FR-31 — interface contract (widgetType + widgetData typed)
 * @see CLAUDE.md project file §9 "Pillar 9 (Widget Visibility Contract)" for per-variant prompt shapes
 */
export type WorkspaceTabWidgetType = 'Summary' | 'DocumentViewer' | 'Dashboard' | 'Table';

// ---------------------------------------------------------------------------
// Per-variant widgetData shapes (discriminated union narrowed by widgetType)
// ---------------------------------------------------------------------------

/**
 * Widget data for a `Summary` tab.
 *
 * The agent-visible state surfaced to Pillar 9's prompt builder is computed
 * from these fields by the widget's `getAgentVisibleState()` implementation;
 * the raw `body` is NOT sent to the LLM directly (token economy).
 *
 * @see FR-31 — discriminated union per widgetType
 * @see Pillar 9 — `getAgentVisibleState()` returns `{ widgetType, summary, tldr, hasUserEdits }`
 */
export interface SummaryTabWidgetData {
  /** Discriminator — must equal the parent tab's `widgetType`. */
  readonly kind: 'Summary';
  /** Optional TL;DR line presented at the top of the tab. */
  tldr?: string;
  /** Full summary body (markdown). Pillar 9 prompt builder may truncate for token budget. */
  body: string;
  /**
   * True when the user has edited the body after agent generation.
   * Surfaced into Pillar 9 visible state so the agent can avoid overwriting user edits.
   * @see Pillar 6a / Q8 conflict resolution
   */
  hasUserEdits?: boolean;
}

/**
 * Widget data for a `DocumentViewer` tab.
 *
 * Covers PDF/DOCX/image preview. The `selectionText` field is populated when
 * the user has a live text selection; Pillar 9 prompt builder includes it only
 * when `visibleToAssistant === true` AND the selection is non-empty.
 *
 * @see FR-31 — discriminated union per widgetType
 * @see Pillar 9 — `getAgentVisibleState()` returns `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }`
 */
export interface DocumentViewerTabWidgetData {
  /** Discriminator — must equal the parent tab's `widgetType`. */
  readonly kind: 'DocumentViewer';
  /** Dataverse document record ID (or transient id for unsaved uploads). */
  documentId: string;
  /** Display filename (e.g. `engagement-letter.docx`). */
  filename: string;
  /** MIME type (e.g. `application/pdf`). */
  mimeType: string;
  /** File size in bytes. May be 0 for stream-only previews. */
  sizeBytes: number;
  /** True when the user has a live non-empty text selection within the viewer. */
  hasSelection?: boolean;
  /**
   * Selection text when `hasSelection` is true. Pillar 9 includes this in agent
   * prompt only when `visibleToAssistant === true`.
   */
  selectionText?: string;
}

/**
 * Widget data for a `Dashboard` tab — typically the embedded LegalWorkspaceApp.
 *
 * Covers any Dataverse-layout-driven tab (Corporate Workspace, Calendar, Daily
 * Briefing, My Work, custom layouts). The agent receives only the dashboard
 * name + last-viewed section — NOT the section payloads (per Pillar 9
 * privacy/economy design).
 *
 * @see FR-31 — discriminated union per widgetType
 * @see SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md §2.1 — Dashboard wrapper pattern
 * @see Pillar 9 — `getAgentVisibleState()` returns `{ widgetType, dashboardName, lastViewedSection }`
 */
export interface DashboardTabWidgetData {
  /** Discriminator — must equal the parent tab's `widgetType`. */
  readonly kind: 'Dashboard';
  /** Dataverse `sprk_workspacelayout` GUID. */
  layoutId: string;
  /** Display name of the layout (e.g. `"Corporate Workspace"`, `"Calendar"`). */
  dashboardName: string;
  /** Section id of the last section the user interacted with — Pillar 9 visible. */
  lastViewedSection?: string;
}

/**
 * Widget data for a `Table` tab — tabular grids with sort/filter/selection.
 *
 * Covers matter lists, document grids, search results, structured AI outputs
 * rendered as rows. Agent receives row counts + sort + filter state — NOT the
 * raw rows (token economy).
 *
 * @see FR-31 — discriminated union per widgetType
 * @see Pillar 9 — `getAgentVisibleState()` returns `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows[] }`
 */
export interface TableTabWidgetData {
  /** Discriminator — must equal the parent tab's `widgetType`. */
  readonly kind: 'Table';
  /** Total row count after filtering. */
  rowCount: number;
  /** Current sort column id (e.g. `"createdOn"`). Undefined if unsorted. */
  sortColumn?: string;
  /** Current sort direction. Undefined if unsorted. */
  sortDirection?: 'asc' | 'desc';
  /** Column ids with active filters applied. */
  filteredColumns: string[];
  /** Row ids currently selected by the user (length == 0 when no selection). */
  selectedRows: string[];
  /** Optional stable id of the data source (e.g. FetchXML query id) for re-fetch on restore. */
  dataSourceId?: string;
}

/**
 * Discriminated union of all per-variant widget-data shapes.
 *
 * Narrowed by the parent tab's `widgetType` (which MUST equal the `kind` field
 * of the chosen variant). Exhaustive switch over `kind` produces a TS compile
 * error if a variant is missed — that compile error is the gate-protection
 * mechanism for Pillar 6b/6c/7/9 consumers.
 *
 * @see FR-31 — typed widgetData per widgetType
 */
export type WorkspaceTabWidgetData =
  | SummaryTabWidgetData
  | DocumentViewerTabWidgetData
  | DashboardTabWidgetData
  | TableTabWidgetData;

// ---------------------------------------------------------------------------
// Provenance + matter-context supporting types
// ---------------------------------------------------------------------------

/**
 * Where a workspace tab came from. Pillar 6c's execution-trace widget renders
 * provenance affordances ("created by playbook X at time Y") and `tab_provenance_clicked`
 * dispatches navigation to the originating context.
 *
 * Per ADR-015 (AI data governance), `createdBy` MUST be a deterministic ID
 * (userId GUID, agentId, playbookId) — NEVER raw user message text.
 *
 * @see FR-31 — sourceProvenance required
 * @see ADR-015 — deterministic IDs only
 * @see Pillar 6c — `workspace.tab_provenance_clicked` additive event
 */
export interface WorkspaceTabSourceProvenance {
  /**
   * Origin role of the entity that created this tab.
   *   - `user`     — created via user affordance (drag/drop, "+ New Workspace", menu pick)
   *   - `agent`    — created by an LLM tool call (e.g. `send_workspace_artifact`)
   *   - `playbook` — created by a playbook node executor (e.g. `DeliverOutput` with `workspace` destination)
   */
  source: 'user' | 'agent' | 'playbook';
  /**
   * Deterministic ID of the creator: userId GUID (when `source==='user'`),
   * agentId (when `source==='agent'`), or playbookId (when `source==='playbook'`).
   * MUST NOT contain user message text or PII. (ADR-015 binding.)
   */
  createdBy: string;
  /** ISO-8601 timestamp of tab creation. */
  createdAt: string;
}

/**
 * Matter context anchoring a workspace tab. Pillar 7 memory composition uses
 * this for matter-scoped recall; Pillar 6b's "Pin to Matter" affordance uses
 * `matterId` to write durable persistence rows.
 *
 * @see FR-31 — matterContext required
 * @see Pillar 7 — matter-scoped memory composition
 */
export interface WorkspaceTabMatterContext {
  /** Dataverse `sprk_matter` GUID. */
  matterId: string;
  /** Human-readable matter name (for tab tooltips and Pillar 9 agent context). */
  matterName: string;
}

// ---------------------------------------------------------------------------
// WorkspaceTab — the canonical interface
// ---------------------------------------------------------------------------

/**
 * Canonical workspace-tab record shared across Pillars 6a/6b/6c/7/9.
 *
 * **PILLAR 6a is the GATE** for this interface — Pillars 6b/6c/7/9 import it.
 * Drift here breaks four downstream pillars; modify only with cross-pillar review.
 *
 * Persistence semantics (Pillar 6a, Q4 hybrid):
 *   - Redis hot tier (24h TTL) — every active-session tab
 *   - Cosmos durable tier     — tabs with `isPinned === true` OR matter-pinned
 *   - Restoration on mount    — via `GET /api/workspace/state`
 *
 * Discriminated union semantics:
 *   - `widgetType` (4-variant literal) is the discriminator
 *   - `widgetData.kind` MUST equal `widgetType`
 *   - Exhaustive `switch (tab.widgetType)` MUST cover all 4 variants
 *
 * @see FR-31 — interface specification
 * @see CLAUDE.md project §"Per-Pillar Binding Rules" Pillar 6a
 * @example
 * ```ts
 * function renderTab(tab: WorkspaceTab): React.ReactNode {
 *   switch (tab.widgetType) {
 *     case 'Summary':        return <SummaryView data={tab.widgetData} />;        // narrows to SummaryTabWidgetData
 *     case 'DocumentViewer': return <DocumentView data={tab.widgetData} />;       // narrows to DocumentViewerTabWidgetData
 *     case 'Dashboard':      return <DashboardView data={tab.widgetData} />;      // narrows to DashboardTabWidgetData
 *     case 'Table':          return <TableView data={tab.widgetData} />;          // narrows to TableTabWidgetData
 *     // Omit a case → TS error (exhaustiveness check) → gate-protection for Pillars 6b/6c/7/9
 *   }
 * }
 * ```
 */
export interface WorkspaceTab {
  /**
   * Stable tab identity. Generated by Pillar 6a's tab manager; preserved across
   * persistence/restore so PaneEventBus events (`tab_change`, `tab_edited`,
   * `tab_focused`, etc.) carry the same id across the tab's lifetime.
   * @see FR-31
   */
  id: string;

  /**
   * Pillar 9 visibility category — discriminator for the `widgetData` union.
   * Closed union of 4 variants (Summary | DocumentViewer | Dashboard | Table).
   * Drives both per-variant data typing AND `getAgentVisibleState()` shape.
   * @see FR-31, FR-58 (Pillar 9 prompt builder)
   * @see WorkspaceTabWidgetType
   */
  widgetType: WorkspaceTabWidgetType;

  /**
   * Per-variant widget payload. Narrowed by `widgetType` via the discriminated
   * union. Producers MUST set `widgetData.kind === widgetType`.
   * @see FR-31, WorkspaceTabWidgetData
   */
  widgetData: WorkspaceTabWidgetData;

  /**
   * Chat session identity. Tabs are scoped to a session for Redis hot-tier
   * persistence; on `/new-session` (Pillar 8 hard slash) the session reset
   * clears tabs for the prior session.
   * @see FR-31, FR-32 (Redis 24h TTL keyed by sessionId)
   */
  sessionId: string;

  /**
   * Pillar 9 visibility flag — when true the tab's `getAgentVisibleState()` is
   * included in the per-turn agent prompt snapshot.
   * Default semantics per CLAUDE.md §9 "Privacy default":
   *   - Agent-created tabs default `true`
   *   - User-created tabs default `false`
   *   - User override via Pillar 6b "Add to Assistant" toggle
   * REQUIRED on the interface so producers MUST decide explicitly.
   * @see FR-31, FR-58
   */
  visibleToAssistant: boolean;

  /**
   * Where the tab came from (user / agent / playbook) + when. Pillar 6c's
   * execution-trace widget renders provenance affordances; `tab_provenance_clicked`
   * navigates to the originating context.
   * @see FR-31, ADR-015 (deterministic IDs only)
   */
  sourceProvenance: WorkspaceTabSourceProvenance;

  /**
   * Matter context anchoring this tab. Pillar 7 memory composition uses this
   * for matter-scoped recall; "Pin to Matter" affordance uses `matterId` for
   * durable Cosmos persistence.
   * @see FR-31, FR-32
   */
  matterContext: WorkspaceTabMatterContext;

  /**
   * True when the user has pinned this tab — flips Pillar 6a persistence from
   * Redis hot tier (24h TTL) to Cosmos durable tier (Q4 hybrid model).
   * @see FR-31, FR-32 (hybrid persistence)
   */
  isPinned: boolean;

  /**
   * True when the user has edit affordances enabled for this tab. Agent
   * `update_workspace_tab` tool MUST refuse mutation when `canEdit === false`
   * (read-only tabs cannot be agent-edited).
   * @see FR-31, FR-35
   */
  canEdit: boolean;

  /**
   * ISO-8601 timestamp of the most recent USER edit (NOT agent edit).
   * Central to Pillar 6b's `update_workspace_tab` chat tool: the tool reads
   * the tab, then on write checks this field against the read timestamp; if
   * `lastUserEditAt > readTimestamp`, the tool REFUSES and the agent must
   * re-read state before re-attempting (Q8 user-wins conflict resolution).
   *
   * Typed optional because brand-new tabs have no user edits yet.
   * @see FR-31, project decision Q8 (CLAUDE.md project file)
   */
  lastUserEditAt?: string;

  /**
   * ISO-8601 timestamp of tab creation. Mirrors `sourceProvenance.createdAt`
   * for query-friendly access on persistence-tier reads.
   * @see FR-31
   */
  createdAt: string;

  /**
   * ISO-8601 timestamp of the most recent mutation (user OR agent). Used by
   * Redis hot-tier TTL refresh and the Pillar 6c execution-trace ordering.
   * @see FR-31
   */
  updatedAt: string;
}
