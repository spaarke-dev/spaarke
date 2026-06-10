/**
 * @spaarke/ai-widgets — Pillar 9 Widget Visibility Contract (R6)
 *
 * Defines the agent-visible serialization shape each workspace widget MAY expose
 * to the LLM prompt builder. The contract has TWO surfaces:
 *
 *   1. `SerializedWidgetState` — discriminated union (4 variants) describing what
 *      a widget chooses to expose. Each variant is the EXACT shape Pillar 9's
 *      prompt builder reads into the per-turn system-prompt snapshot.
 *   2. `GetAgentVisibleState` — the function signature each widget implements to
 *      opt in. Returning `null` (or omitting the method) is the privacy default
 *      per ADR-015 (data minimization) — the widget contributes NOTHING to the
 *      agent prompt for that tab.
 *
 * The four variants align 1:1 with `WorkspaceTabWidgetType` from task 050
 * (`WorkspaceTab.ts`). This file MUST reuse that union (NOT redefine) so a
 * future fifth variant added to `WorkspaceTab` produces a TS compile error here
 * (gate-protection mechanism — see `assertNeverSerializedState` below).
 *
 * ## Consumer Surface (Phase C downstream gates)
 *
 *   - **Task 072** — `WorkspaceWidgetRegistry` extension adds an optional
 *     `getVisibleState?: GetAgentVisibleState` to the registration metadata so
 *     the prompt builder can resolve a widget instance to its visible state.
 *   - **Task 073** — Per-widget implementations of `GetAgentVisibleState` on
 *     Summary, DocumentViewer, Dashboard, and Table widgets (one each).
 *   - **Task 074** — Pillar 9 prompt builder iterates over Assistant-visible
 *     tabs (`visibleToAssistant === true` per task 050) and serializes each via
 *     this contract; the result becomes the per-turn `WorkspaceState` snippet
 *     in `SprkChatAgentFactory.CreateAgentAsync`'s system prompt.
 *
 * Drift in this file breaks all three downstream tasks. Modify only with a
 * cross-task review (FULL rigor, code-review + adr-check gates).
 *
 * ## Privacy Defaults (ADR-015 binding)
 *
 * Per ADR-015 "AI Data Governance" + CLAUDE.md §9 "Privacy default", the
 * project-level rule is: widgets DO NOT expose state to the LLM by default.
 * Opting-in is explicit via:
 *
 *   - Implementing `getAgentVisibleState()` that returns a non-null value, AND
 *   - The parent tab having `visibleToAssistant === true` (per Pillar 6a)
 *
 * BOTH conditions must be true; failing either omits the tab from the prompt.
 *
 * Per-variant content choices (what's exposed vs withheld):
 *
 *   - `Summary`        — exposes agent-derived `summary` + `tldr` (already
 *                        sanitized at generation) + `hasUserEdits` (conflict
 *                        signal per Q8 user-wins). Withholds raw `body` to
 *                        respect token economy (NFR-10) — body would dominate
 *                        the 8K budget on multi-tab sessions.
 *   - `DocumentViewer` — exposes file METADATA (`filename`, `mimeType`,
 *                        `sizeBytes`) and selection STATE (`hasSelection`,
 *                        optional `selectionText` only when the user has an
 *                        active selection AND `visibleToAssistant === true`).
 *                        Withholds the document body — agent must use a chat
 *                        tool (e.g., `summarize-document`) to access content.
 *   - `Dashboard`      — exposes `dashboardName` + `lastViewedSection?`.
 *                        DELIBERATELY OMITS section payloads / chart data per
 *                        Pillar 9 design (token economy + privacy — sections
 *                        often contain PII like matter rosters / financial
 *                        data the user did NOT consent to share with the LLM).
 *   - `Table`          — exposes structural state (`rowCount`, `sortColumn?`,
 *                        `filteredColumns?`, `selectedRows?` as a COUNT).
 *                        Withholds row payloads (matter lists / document grids
 *                        contain sensitive entity data); exposing only counts
 *                        respects ADR-015 data-minimization while letting the
 *                        agent know "the user is looking at 47 matters
 *                        filtered by Status=Open" for context-aware replies.
 *
 * @see FR-55 — `getAgentVisibleState()` returns compact + schema-typed +
 *      nullable representation (R6 spec)
 * @see CLAUDE.md project file §Pillar 9 — per-variant content shapes
 * @see ADR-012 — shared lib placement (`@spaarke/ai-widgets`)
 * @see ADR-015 — AI data governance (data minimization; identifiers + counts
 *      OK; content + payloads withheld unless tagged content like agent-
 *      generated summary or user-selected selectionText)
 * @see WorkspaceTab.ts — discriminator union source (`WorkspaceTabWidgetType`)
 *      AND the `visibleToAssistant` filter that gates whether this contract's
 *      output reaches the LLM prompt
 */

import type { WorkspaceTabWidgetType } from './WorkspaceTab';

// ---------------------------------------------------------------------------
// Per-variant serialized state shapes (discriminated by `widgetType`)
// ---------------------------------------------------------------------------

/**
 * Serialized agent-visible state for a `Summary` widget.
 *
 * Pillar 9's prompt builder receives this verbatim and renders it into the
 * per-turn system-prompt snippet. The fields here are the ONLY surface the
 * agent sees for a Summary tab — the underlying widget data may contain more
 * fields (e.g., `body`, internal layout state) that are intentionally withheld.
 *
 * Field rationale:
 *   - `summary` — agent-derived text the LLM produced earlier; safe to re-feed
 *     because it has already passed through the safety pipeline at generation
 *     time (PromptShield + Groundedness per NFR-13).
 *   - `tldr` — array of TL;DR bullet lines (compact; same provenance as
 *     `summary`). Empty array if not produced.
 *   - `hasUserEdits` — boolean signal from `SummaryTabWidgetData.hasUserEdits`.
 *     Surfaced so the agent can avoid overwriting user edits in subsequent
 *     `update_workspace_tab` calls (Q8 user-wins conflict resolution).
 *
 * @see FR-55 — compact + schema-typed representation
 * @see CLAUDE.md §Pillar 9 — Summary variant shape
 * @see ADR-015 — agent-derived text is acceptable (already governed at
 *      generation time)
 */
export interface SerializedSummaryState {
  /** Discriminator — equal to the parent tab's `widgetType`. */
  readonly widgetType: 'Summary';
  /**
   * Agent-derived summary text (markdown). Already safety-governed at
   * generation time per NFR-13. Pillar 9 prompt builder may truncate for the
   * 8K system-prompt budget (NFR-10).
   * @see FR-55
   */
  summary: string;
  /**
   * TL;DR bullets (compact). Empty array if not produced. Pillar 9 prefers
   * these over `summary` when budget is tight — keep semantically equivalent.
   * @see FR-55
   */
  tldr: string[];
  /**
   * True when the user has edited the summary after agent generation. Mirrors
   * `SummaryTabWidgetData.hasUserEdits`. Surfaced so the agent's
   * `update_workspace_tab` call can detect and respect user changes (Q8
   * user-wins conflict resolution).
   * @see FR-55, Q8 conflict resolution (R6 project decisions)
   */
  hasUserEdits: boolean;
}

/**
 * Serialized agent-visible state for a `DocumentViewer` widget.
 *
 * Withholds the document body (privacy + token economy); the agent must invoke
 * a chat tool (e.g., `summarize-document`, `extract-entities`) to access
 * content. The optional `selectionText` is the ONLY content-bearing field and
 * is gated by user action — populated ONLY when:
 *
 *   1. The user has an active non-empty text selection in the viewer, AND
 *   2. The parent tab has `visibleToAssistant === true` (Pillar 6a)
 *
 * Pillar 9's prompt builder enforces gate (2); the widget enforces gate (1) by
 * setting `hasSelection: false` (and omitting `selectionText`) when no
 * selection is live.
 *
 * @see FR-55 — compact + schema-typed representation
 * @see CLAUDE.md §Pillar 9 — DocumentViewer variant shape
 * @see ADR-015 — file metadata (identifiers + MIME + size) is governed-tier
 *      Class 1 ("Identifiers") + Class 2 ("Derived metadata"); content stays
 *      out (Class 4 "Document content" — withhold unless required)
 */
export interface SerializedDocumentViewerState {
  /** Discriminator — equal to the parent tab's `widgetType`. */
  readonly widgetType: 'DocumentViewer';
  /**
   * Display filename. Class 2 derived metadata per ADR-015.
   * @see FR-55
   */
  filename: string;
  /**
   * MIME type (e.g. `application/pdf`). Class 2 derived metadata per ADR-015.
   * @see FR-55
   */
  mimeType: string;
  /**
   * File size in bytes. Pillar 9 may surface this to the agent as a
   * size-awareness hint (e.g., "this is a 250-page document — would you like
   * a summary or to search a specific section?").
   * @see FR-55, ADR-015 (sizes OK in logs + prompts)
   */
  sizeBytes: number;
  /**
   * True when the user has a live non-empty text selection in the viewer.
   * Mirrors `DocumentViewerTabWidgetData.hasSelection`. When false,
   * `selectionText` MUST be omitted.
   * @see FR-55
   */
  hasSelection: boolean;
  /**
   * The selected text, when `hasSelection === true`. The ONLY content-bearing
   * field in this variant; gated by user action (live selection) AND parent
   * tab `visibleToAssistant === true` (Pillar 6a). Withheld when either gate
   * fails (privacy default per ADR-015).
   * @see FR-55, ADR-015 (content allowed only when user has explicitly
   *      opted in via selection + visibility toggle)
   */
  selectionText?: string;
}

/**
 * Serialized agent-visible state for a `Dashboard` widget.
 *
 * Deliberately withholds section payloads and chart data (per Pillar 9 design).
 * Dashboards (embedded LegalWorkspaceApp layouts: Corporate Workspace, Calendar,
 * Daily Briefing, My Work, custom layouts) often render sensitive aggregates
 * (matter rosters, financial summaries, calendar events with attendee PII)
 * that the user did NOT consent to share with the LLM by mounting the layout
 * in a workspace tab. Pillar 9 exposes only the dashboard NAME + last-viewed
 * SECTION ID so the agent has navigational context without seeing payload.
 *
 * To pull section data INTO the prompt, the user must either:
 *
 *   1. Drag a specific section into the Assistant pane (creates a new tab with
 *      a tightly-scoped widgetType — Summary / Table / DocumentViewer), OR
 *   2. Invoke a chat tool that explicitly reads dashboard sections (none exist
 *      in R6; would require an opt-in Pillar 6b tool authored later).
 *
 * @see FR-55 — compact + schema-typed representation
 * @see CLAUDE.md §Pillar 9 — Dashboard variant shape (NOT chart data)
 * @see SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md §2.1 — Dashboard wrapper pattern
 * @see ADR-015 — withhold Class 4 (Document content) and section payloads;
 *      surface only Class 1 (Identifiers — section id) + Class 2 (Derived
 *      metadata — layout name)
 */
export interface SerializedDashboardState {
  /** Discriminator — equal to the parent tab's `widgetType`. */
  readonly widgetType: 'Dashboard';
  /**
   * Display name of the layout (e.g. `"Corporate Workspace"`, `"Calendar"`).
   * Mirrors `DashboardTabWidgetData.dashboardName`. Class 2 derived metadata
   * per ADR-015.
   * @see FR-55
   */
  dashboardName: string;
  /**
   * Section id of the last section the user interacted with. Class 1
   * identifier per ADR-015 — deterministic id, NOT section content. Optional
   * because the user may not have interacted with any section yet (e.g., just
   * opened the layout).
   * @see FR-55, ADR-015 (deterministic IDs only)
   */
  lastViewedSection?: string;
}

/**
 * Serialized agent-visible state for a `Table` widget.
 *
 * Exposes STRUCTURAL state (counts + sort + filter + selection cardinality)
 * but NEVER row payloads. Tables (matter lists, document grids, search results,
 * structured AI outputs as rows) contain entity data the agent should access
 * via a typed chat tool (e.g., `find-similar`, `query-matters`) — NOT by
 * scraping a prompt snapshot.
 *
 * `selectedRows` is intentionally a COUNT (number), NOT the row id list — see
 * the privacy rationale in this file's header comment. The agent can know
 * "the user selected 3 rows" without knowing which 3.
 *
 * Optional fields convey "no current state" rather than "withheld" — e.g.,
 * `sortColumn === undefined` means the table is unsorted; `filteredColumns`
 * being undefined means no filters are applied.
 *
 * @see FR-55 — compact + schema-typed representation
 * @see CLAUDE.md §Pillar 9 — Table variant shape (counts + structural state)
 * @see ADR-015 — surface Class 1 (Identifiers — column ids) + Class 2 (Derived
 *      metadata — counts, sort dir); withhold Class 3 (User prompts — N/A
 *      here) + Class 4 (Document content — N/A here) + entity row payloads
 */
export interface SerializedTableState {
  /** Discriminator — equal to the parent tab's `widgetType`. */
  readonly widgetType: 'Table';
  /**
   * Total row count after filtering. Mirrors `TableTabWidgetData.rowCount`.
   * Class 2 derived metadata per ADR-015.
   * @see FR-55
   */
  rowCount: number;
  /**
   * Current sort column id (e.g. `"createdOn"`). Class 1 identifier per
   * ADR-015 — column id, NOT column values. Omitted when the table is
   * unsorted (matches `TableTabWidgetData.sortColumn` semantics).
   * @see FR-55, ADR-015
   */
  sortColumn?: string;
  /**
   * Column ids with active filters applied. Class 1 identifiers per ADR-015.
   * Omitted when no filters are applied (vs `[]` empty array — both
   * acceptable but `undefined` signals "no filter state to convey").
   * @see FR-55, ADR-015
   */
  filteredColumns?: string[];
  /**
   * COUNT of rows currently selected by the user — NOT the row ids.
   * Surfacing the count lets the agent reason about the user's working set
   * size ("you have 3 documents selected — would you like to summarize all 3?")
   * without exposing the row identities (some matter ids / document ids
   * encode case context the user did not consent to share). Omitted when no
   * rows are selected (selection cardinality of 0).
   * @see FR-55, ADR-015 (data minimization — counts OK, identities withheld)
   */
  selectedRows?: number;
}

// ---------------------------------------------------------------------------
// Discriminated union + opt-in function signature
// ---------------------------------------------------------------------------

/**
 * Discriminated union of all per-variant serialized state shapes.
 *
 * Narrowed by the `widgetType` discriminator (which MUST equal the parent
 * tab's `widgetType` from `WorkspaceTab.ts`). Pillar 9 prompt builder
 * (task 074) consumes this union via an exhaustive switch — adding a fifth
 * variant requires a coordinated update to the prompt builder + registry
 * extension + per-widget implementations.
 *
 * **Exhaustiveness gate**: `assertNeverSerializedState` below converts a
 * missing-case bug into a TS compile error at the consumer site. See its
 * JSDoc for the usage pattern.
 *
 * @see FR-55 — schema-typed representation
 * @see WorkspaceTab.ts `WorkspaceTabWidgetType` — discriminator alignment
 */
export type SerializedWidgetState =
  | SerializedSummaryState
  | SerializedDocumentViewerState
  | SerializedDashboardState
  | SerializedTableState;

/**
 * Function signature each workspace widget implements to opt into Pillar 9
 * visibility. The widget returns either:
 *
 *   - A `SerializedWidgetState` variant matching its category → the prompt
 *     builder includes the state in the per-turn system-prompt snippet (if
 *     the parent tab has `visibleToAssistant === true`).
 *   - `null` → the widget explicitly opts out for this invocation (e.g., not
 *     yet hydrated, no meaningful state to expose, content currently
 *     redacted). Equivalent to omitting the method entirely.
 *
 * **Privacy default**: widgets that do NOT implement this function are
 * treated as `() => null` — Pillar 9 contributes NOTHING to the prompt for
 * that tab. Opting in is an explicit author decision per ADR-015.
 *
 * Implementations MUST:
 *
 *   - Return a variant whose `widgetType` matches the parent tab's
 *     `widgetType` from `WorkspaceTab.ts` (mismatch is undefined behavior;
 *     the prompt builder may discard the entry or throw in dev mode).
 *   - Be PURE and SYNCHRONOUS — the prompt builder calls this on every chat
 *     turn; async work or side effects would block the user-perceived
 *     latency (NFR target: chat turn < 2s end-to-end).
 *   - Stay within the per-tab token budget hinted by Pillar 9 (~200 tokens
 *     per tab; truncate `summary` / `tldr` if longer). Pillar 9 enforces a
 *     hard cap at the prompt-builder level but widgets should self-limit.
 *
 * @see FR-55 — nullable opt-out + compact representation
 * @see ADR-015 — privacy default (don't expose unless explicitly chosen)
 * @see CLAUDE.md §Pillar 9 — `getAgentVisibleState()` opt-in contract
 *
 * @example
 * ```ts
 * // Summary widget implementation
 * const getAgentVisibleState: GetAgentVisibleState = () => ({
 *   widgetType: 'Summary',
 *   summary: state.body.slice(0, 500),  // self-limit per Pillar 9 budget
 *   tldr: state.tldr ? [state.tldr] : [],
 *   hasUserEdits: state.hasUserEdits ?? false,
 * });
 * ```
 *
 * @example
 * ```ts
 * // Opt-out (explicit, equivalent to omitting the method)
 * const getAgentVisibleState: GetAgentVisibleState = () => null;
 * ```
 */
export type GetAgentVisibleState = () => SerializedWidgetState | null;

// ---------------------------------------------------------------------------
// Exhaustiveness check + discriminator alignment guard
// ---------------------------------------------------------------------------

/**
 * Exhaustiveness helper for `SerializedWidgetState` consumers (Pillar 9 prompt
 * builder + tests). Use in the default branch of a `switch (state.widgetType)`
 * to convert a missing-case bug into a TS compile error.
 *
 * @example
 * ```ts
 * function renderForPrompt(state: SerializedWidgetState): string {
 *   switch (state.widgetType) {
 *     case 'Summary':        return renderSummary(state);
 *     case 'DocumentViewer': return renderDocumentViewer(state);
 *     case 'Dashboard':      return renderDashboard(state);
 *     case 'Table':          return renderTable(state);
 *     default:               return assertNeverSerializedState(state);
 *     //                              ^^^^^ TS error if a variant is missed
 *   }
 * }
 * ```
 *
 * If runtime ever reaches this function (it shouldn't — TS enforces
 * exhaustiveness), throws to surface the misconfiguration loudly.
 *
 * @see FR-55 — schema-typed representation (exhaustiveness gate)
 */
export function assertNeverSerializedState(state: never): never {
  throw new Error(
    `SerializedWidgetState: unhandled variant — exhaustive switch missed a case. Got: ${JSON.stringify(state)}`
  );
}

/**
 * Compile-time alignment guard: verifies every literal in
 * `WorkspaceTabWidgetType` (task 050) has a corresponding variant in
 * `SerializedWidgetState`. If task 050's union ever grows a fifth variant
 * without this file being updated, the type below resolves to `never` and any
 * downstream usage (task 072/073/074) breaks at compile time.
 *
 * This is a TYPE-LEVEL assertion — it generates no runtime code. The name is
 * exported so a quick `import { _DiscriminatorAlignment } from '...'` in test
 * files can verify the alignment if drift is ever suspected.
 *
 * @see FR-55, WorkspaceTab.ts `WorkspaceTabWidgetType`
 * @internal — type-level only; not used at runtime
 */
export type _DiscriminatorAlignment =
  SerializedWidgetState['widgetType'] extends WorkspaceTabWidgetType
    ? WorkspaceTabWidgetType extends SerializedWidgetState['widgetType']
      ? true
      : never
    : never;
