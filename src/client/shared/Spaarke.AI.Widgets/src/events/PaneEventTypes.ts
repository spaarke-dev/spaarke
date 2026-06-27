/**
 * @spaarke/ai-widgets — PaneEventTypes
 *
 * Typed event definitions for the four cross-pane channels.
 *
 * Each channel carries a discriminated union of event payloads. The channel
 * name acts as the first-level discriminant; the event `type` field is the
 * second-level discriminant within a channel.
 *
 * Replaces R1's DOM CustomEvent bus (cross-pane-events.ts) with a pure
 * TypeScript, no-DOM, multi-subscriber design.
 *
 * @see PaneEventBus — runtime implementation
 * @see usePaneEvent — React subscription hook
 * @see useDispatchPaneEvent — React dispatch hook
 */

// ---------------------------------------------------------------------------
// Channel union
// ---------------------------------------------------------------------------

/**
 * The four typed channels of the PaneEventBus.
 *
 * - `workspace`    — widget lifecycle and tab navigation in the workspace pane
 * - `context`      — document context updates and citation highlights in the context pane
 * - `conversation` — user input and playbook changes in the conversation pane
 * - `safety`       — groundedness annotations and confidence signals from the safety layer
 */
export type PaneChannel = 'workspace' | 'context' | 'conversation' | 'safety';

// ---------------------------------------------------------------------------
// Workspace channel
// ---------------------------------------------------------------------------

/**
 * Typed payload for `workspace.widget_load` events dispatched by mount-source
 * panes (Assistant pane W-4 / task 042; Context pane W-5 / task 043).
 *
 * The Assistant or Context pane constructs one of these payloads, embeds it
 * into a `WorkspacePaneEvent` with `type: 'widget_load'`, and dispatches on
 * the `workspace` channel. The Workspace pane's existing subscriber (in
 * `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`)
 * resolves `widgetType` via `WorkspaceWidgetRegistry` and renders the widget
 * as a new tab.
 *
 * This type intentionally narrows the existing `WorkspacePaneEvent` shape
 * (which carries optional fields shared across all discriminants) for the
 * specific subset of fields required when MOUNT-SOURCE panes initiate a
 * widget load. Subscribers (e.g. WorkspacePane) continue to read from the
 * underlying `WorkspacePaneEvent` interface — this type is a convenience
 * contract for DISPATCHERS only. No new event-type discriminant is needed:
 * `widget_load` already exists on the union (R2 origin).
 *
 * Per ADR-030: NO `any` payloads. `widgetData` is typed via a discriminated
 * union per known widget type; unknown widget types use the `unknown` shape
 * with subscriber-side narrowing.
 *
 * Reusable for both W-4 (task 042 — Assistant pane) and W-5 (task 043 —
 * Context pane).
 *
 * @see WorkspacePane.tsx — receiver
 * @see WorkspaceWidgetRegistry.ts — widgetType resolution
 * @see ADR-030 — typed PaneEventBus channels
 */
export interface WorkspaceWidgetLoadEvent {
  /** Always `'widget_load'` — discriminant on the workspace channel. */
  type: 'widget_load';

  /**
   * Registered widget type ID. MUST match a key registered via
   * `registerWorkspaceWidget()` (see register-workspace-widgets.ts). Unknown
   * types fall back to `GenericTextWidget` per the registry contract.
   *
   * Known mount-source-relevant types (extend as new widgets are wired):
   * - `'document-viewer'`        — chat PDF/file upload preview (task 042)
   * - `'AnalysisEditor'`         — R1-origin analysis editor
   * - `'redline-viewer'`         — document comparison
   * - `'create-matter-wizard'`   — embedded create-matter flow
   * - `'document-upload-wizard'` — embedded document upload flow
   * - `'workspace'`              — embedded LegalWorkspaceApp layout
   */
  widgetType: string;

  /**
   * Widget-specific data payload. Shape is widget-dependent — each widget
   * declares its own data interface (e.g. `DocumentViewerWidgetData`,
   * `RedlineViewerData`). Subscribers narrow via `widgetType`.
   *
   * Typed as `unknown` here (NOT `any`) so dispatchers must explicitly cast
   * at the call site to the widget's declared data shape, and subscribers
   * must narrow before use. Per ADR-030: `unknown` is acceptable for genuinely
   * polymorphic fields; `any` is not.
   */
  widgetData?: unknown;

  /**
   * Optional human-readable display name for the new workspace tab. When
   * present, WorkspacePane uses this as the tab label instead of the
   * registry metadata's generic `displayName` (e.g. "Contract.pdf" instead
   * of the registry's "Document Viewer").
   */
  displayName?: string;
}

/**
 * Events emitted on the `workspace` channel.
 *
 * The workspace pane dispatches these when widgets load, update, or trigger
 * actions, and when the active tab changes.
 */
export interface WorkspacePaneEvent {
  /**
   * Discriminant for the specific workspace event.
   *
   * - `widget_load`            — a widget has been mounted and is ready
   * - `widget_update`          — widget data has changed (refresh signal)
   * - `widget_action`          — a user action was performed inside a widget
   * - `tab_change`             — the active workspace tab changed
   * - `tab_count_change`       — the number of open tabs changed (drives Stage 3↔4 transitions)
   * - `selection_changed`      — the user selected (or cleared) text inside a widget
   * - `tabs_clear`             — all workspace tabs should be cleared (exclusive playbook selection)
   * - `wizard_step`            — the ConversationPane requests a wizard step navigation or field update
   * - `entity_resolved`        — Dataverse entity context has been resolved (drives Stage 2→3)
   * - `session_reset`          — the active session was cleared; all panes reset to Stage 1
   * - `active_widget_changed`  — the active workspace tab changed; carries widgetType + widgetData +
   *                              tabId + displayName so downstream panes (Assistant, Context) can
   *                              adapt their view to the new active context (Round 4 Fix 4 signal
   *                              infrastructure — no consumers yet, just the foundation for future
   *                              pane coordination)
   * - `streaming_started`      — a structured-output streaming run has begun; downstream widgets
   *                              (e.g. StructuredOutputStreamWidget, R5 task 017 / D2-07) mount and
   *                              prepare to receive field deltas. Carries `streamId` so multiple
   *                              concurrent streams can be disambiguated (R5 D2-06 / spec NFR-09).
   * - `field_delta`            — a single delta in an in-flight structured-output stream; carries
   *                              `streamId`, `fieldPath` (JSON path of the target field),
   *                              `fieldContent` (the delta content) and monotonic `sequence`.
   *                              Subscribed by StructuredOutputStreamWidget to progressively
   *                              render the output as it streams (R5 D2-06 / spec NFR-09).
   * - `streaming_complete`     — the structured-output stream has finished; carries `streamId`
   *                              and `completionStatus` (`'complete' | 'declined' | 'empty'`) so
   *                              the widget can finalise its rendering or show a terminal state
   *                              (R5 D2-06 / spec NFR-09).
   *
   * ── Phase 5R Wave 5-C composite section events (FR-54 / task 114a + 114b) ──
   *
   * The three discriminants below carry per-section streaming for the composite
   * Output Node pattern (NodeType.DeliverComposite, FR-52 / task 114R). Section
   * events are keyed by section NAME (not schema position) — coordination point
   * count drops from 5 (schema-on-action + schema-aware widget) to 2 (section
   * name + section state).
   *
   * Backward-compat invariant (FR-54): widgets that receive `FieldDelta` events
   * (unmigrated schema-position playbooks) continue to render via the legacy
   * `streaming_started` / `field_delta` / `streaming_complete` path UNCHANGED.
   * A single widget instance MAY receive EITHER set of events but never both
   * for the same stream — the BFF emits one or the other based on whether the
   * playbook contains a `NodeType.DeliverComposite` node (per task 114a guard).
   *
   * - `section_started`        — a composite section has begun streaming; carries
   *                              `sectionName`, `streamId`, optional `displayLabel`,
   *                              `sectionIndex`, `totalSections`. Subscribers create
   *                              section state in 'streaming' status.
   * - `section_data`           — incremental delta for an in-flight section; carries
   *                              `streamId`, `sectionName`, `contentDelta` (text
   *                              fragment to append) or `structuredData` (merge into
   *                              section state). Subscribers append text and/or
   *                              merge structured data per section.
   * - `section_completed`      — a composite section finished streaming; carries
   *                              `streamId`, `sectionName`, optional `finalContent`
   *                              (replaces accumulated text), `finalStructuredData`
   *                              (replaces structured data), `citations` (NFR-A3
   *                              trust model). Subscribers mark section 'completed'.
   *
   * ADR-015 BINDING: section events carry the Action's structured output text/data
   * (already allowed per existing FieldDelta contract). No file binaries.
   * ADR-021: widget consuming these uses Fluent v9 semantic tokens — dark-mode safe.
   * ADR-030 additive-types rule: unknown event types ignored by existing subscribers
   * (workspace channel) — no breakage to FieldDelta consumers when these events
   * appear alongside.
   *
   * ── R6 Pillar 6c reverse-flow events (FR-38 / D-C-13 / task 060) ──────────
   *
   * The four discriminants below complete the Pillar 6c tri-directional model:
   * the workspace pane dispatches them on user-driven tab state changes so the
   * assistant + context pane can observe and react. They are the reverse-flow
   * counterpart to the six `context.*` execution-trace events added by task 059.
   *
   * ADR-015 BINDING (CRITICAL): with ONE INTENTIONAL EXCEPTION
   * (`user_selection.selectionText`), every field on these four discriminants
   * is a deterministic identifier, ISO-8601 timestamp, or enumerated string.
   * The `selectionText` field carries user-visible-by-design content (the
   * text the user explicitly selected to share with the agent) — it is
   * user-content but it is ALLOWED here under FR-38 + Pillar 9 visibility
   * contract because users opt in by selecting. Telemetry events
   * (`context.*` trace channel) MUST NOT log this field's value.
   *
   * - `user_selection`          — the user changed their text selection in a
   *                               workspace widget; carries `tabId`,
   *                               `sessionId`, optional `selectionText` (≤200
   *                               chars, user-visible-by-design), `timestamp`.
   *                               Consumers: assistant (for selection-aware
   *                               prompts), context pane (for "what is the
   *                               user looking at?" cues).
   * - `tab_edited`               — the user edited fields in a workspace tab;
   *                               carries `tabId`, `sessionId`, `editedFields`
   *                               (FIELD NAMES, NOT values), `timestamp`.
   *                               Consumers: assistant (stale-write conflict
   *                               detection per Pillar 6b Q8), context pane
   *                               (mark provenance "user-edited").
   * - `tab_focused`              — the user focused (clicked/keyed into) a
   *                               workspace tab; carries `tabId`, `sessionId`,
   *                               `timestamp`. Consumers: assistant (adapt
   *                               agent context to the currently-focused
   *                               tab), context pane (sync detail card).
   * - `tab_provenance_clicked`   — the user clicked a provenance affordance
   *                               on a workspace tab to navigate back to its
   *                               originator; carries `tabId`, `sessionId`,
   *                               `provenanceType`
   *                               (`'chat-message' | 'playbook-node'`),
   *                               `provenanceId`, `timestamp`. Consumers:
   *                               assistant (scroll to chat message), context
   *                               pane (highlight playbook-node in trace
   *                               widget).
   */
  type:
    | 'widget_load'
    | 'widget_update'
    | 'widget_action'
    | 'tab_change'
    | 'tab_count_change'
    | 'selection_changed'
    | 'tabs_clear'
    | 'wizard_step'
    | 'entity_resolved'
    | 'session_reset'
    | 'active_widget_changed'
    | 'streaming_started'
    | 'field_delta'
    | 'streaming_complete'
    | 'section_started'
    | 'section_data'
    | 'section_completed'
    | 'user_selection'
    | 'tab_edited'
    | 'tab_focused'
    | 'tab_provenance_clicked';

  /** Identifies the widget kind (e.g. `"document-summary"`, `"clause-list"`). */
  widgetType?: string;

  /** Arbitrary payload specific to the widget type. */
  widgetData?: unknown;

  /** Target tab identifier when `type === 'tab_change'`. */
  tabId?: string;

  /** Named action identifier when `type === 'widget_action'`. */
  action?: string;

  /**
   * Widget instance ID that should handle the action.
   * Used to route `widget_action` events when multiple widget instances coexist.
   */
  targetWidgetId?: string;

  /**
   * Number of open workspace tabs after the current operation.
   *
   * Present when `type === 'tab_count_change'`. ShellStageManager reads this to
   * drive Stage 3↔4 (active-chat ↔ review) transitions:
   *   tabCount >= 2 → Stage 4 (review / multi-task)
   *   tabCount === 1 → Stage 3 (active-chat)
   *   tabCount === 0 → reset toward Stage 1 (welcome)
   */
  tabCount?: number;

  /**
   * Human-readable display name for the affected widget tab.
   *
   * Optional on `widget_load`: when present, the WorkspacePane subscriber uses
   * it as the tab title instead of falling back to the registry metadata's
   * generic `displayName`. This is how `WorkspacePaneMenu` makes a workspace
   * tab show the chosen layout's name (e.g. "Corporate Workspace") rather than
   * the generic "Workspace" registry label.
   *
   * Required on `active_widget_changed`: carries the currently-active tab's
   * display name so downstream panes can show "Active context: Corporate
   * Workspace" without re-querying the tab manager.
   */
  displayName?: string;

  // ── selection_changed fields ──────────────────────────────────────────────

  /**
   * The text the user has selected inside a workspace widget.
   *
   * `null` signals that the selection has been cleared (mousedown / new
   * selection started). Present when `type === 'selection_changed'`.
   */
  selectedText?: string | null;

  /**
   * Short human-readable label describing where the selection came from,
   * used as the chip preview label in ConversationPane.
   *
   * Example: `"Document viewer"`, `"Clause list"`.
   * Present when `type === 'selection_changed'` and `selectedText` is non-null.
   */
  contextLabel?: string;

  // ── wizard_step fields ────────────────────────────────────────────────────

  /**
   * Stable identifier for the wizard instance receiving this event.
   * Matches the `wizardId` prop on the target wizard widget.
   * Required when `type === 'wizard_step'`.
   */
  wizardId?: string;

  /**
   * The action the wizard should perform.
   *
   * - `next`      — advance to the next step (same as clicking Next)
   * - `back`      — retreat to the previous step (same as clicking Back)
   * - `set-field` — programmatically set a named form field value
   *
   * Required when `type === 'wizard_step'`.
   */
  wizardAction?: 'next' | 'back' | 'set-field';

  /**
   * Field name to update when `wizardAction === 'set-field'`.
   * The field name is wizard-specific (e.g. `"matterName"`, `"documentTitle"`).
   */
  fieldName?: string;

  /**
   * New field value when `wizardAction === 'set-field'`.
   * Typed as `unknown` because field values vary by wizard and step.
   */
  fieldValue?: unknown;

  // ── streaming fields ──────────────────────────────────────────────────────
  //
  // Carried by the three R5 structured-output streaming discriminants:
  // `streaming_started`, `field_delta`, `streaming_complete`. All four fields
  // are optional on the base type so existing subscribers (which never touch
  // them) remain type-safe. Subscribers that handle the streaming events MUST
  // narrow on `event.type` before accessing these fields.
  //
  // Added by R5 task 016 (D2-06) per spec NFR-09 + ADR-030 additive-types rule.

  /**
   * Stable identifier correlating the three lifecycle events of a single
   * structured-output stream (`streaming_started` → `field_delta` (N) →
   * `streaming_complete`). Required when
   * `type === 'streaming_started' | 'field_delta' | 'streaming_complete'`.
   * Subscribers use this to disambiguate concurrent streams when more than
   * one structured-output run is in flight in the same session.
   */
  streamId?: string;

  /**
   * JSON path of the field receiving the delta within the structured-output
   * schema (e.g. `"summary"`, `"parties[0].name"`, `"keyTerms"`).
   * Required when `type === 'field_delta'`. Subscribers route the delta to
   * the correct UI element in the progressively-rendered output.
   */
  fieldPath?: string;

  /**
   * The delta content for a `field_delta` event. Strings are appended in
   * `sequence` order to build the final field value. Required when
   * `type === 'field_delta'`.
   *
   * Typed as `string` (NOT `unknown`) because field deltas are token-stream
   * fragments produced by the BFF SSE `FieldDelta` variant of `AnalysisChunk`
   * (R5 task 005 / D1-05). Non-string structured deltas use `widgetData` on
   * a different discriminant, not this field.
   */
  fieldContent?: string;

  /**
   * Monotonically-increasing sequence number for `field_delta` events within
   * a single stream (keyed by `streamId`). Subscribers MUST order deltas by
   * `sequence` before concatenation; out-of-order arrival is possible under
   * heavy load. Required when `type === 'field_delta'`.
   */
  sequence?: number;

  /**
   * Terminal status of a structured-output stream.
   *
   * - `'complete'` — stream finished and all fields received successfully
   * - `'declined'` — the orchestrator declined (e.g. safety perimeter block,
   *                  insufficient grounding) without emitting a full payload
   * - `'empty'`    — stream completed but produced no field deltas (degenerate
   *                  case — e.g. zero-length document); UI should show a
   *                  no-content message
   *
   * Required when `type === 'streaming_complete'`.
   */
  completionStatus?: 'complete' | 'declined' | 'empty';

  // ── Phase 5R Wave 5-C section-keyed streaming fields (FR-54) ──────────────
  //
  // Carried by the three section discriminants: `section_started`,
  // `section_data`, `section_completed`. All fields are optional on the base
  // type so existing subscribers (which never touch them) remain type-safe.
  // Subscribers handling section events MUST narrow on `event.type` before
  // accessing.
  //
  // Added by Phase 5R Wave 5-C task 114b (FE consumer half) mirroring the BFF
  // contract finalised by task 114a (CompositeOutputPayload / CompositeSectionResult
  // from DeliverCompositeNodeExecutor). Reuses `streamId` from the streaming
  // block above so a section stream and a legacy FieldDelta stream can be
  // disambiguated by the same correlation field.
  //
  // ADR-015 BINDING: section name + display label are configuration metadata
  // (Tier 1 safe). Content text + structured data carry the Action's structured
  // output — same Tier-3 status as the existing FieldDelta contract; NOT raw
  // user message content. NO file binaries on any section discriminant.

  /**
   * Stable section identifier for the composite output pattern. Required on all
   * three section discriminants (`section_started`, `section_data`,
   * `section_completed`).
   *
   * Format: short, deterministic, lowercase-camelCase-or-snake_case key declared
   * by the playbook author on the composite Output Node's `sections[*].sectionName`
   * config (e.g. `'summary'`, `'keyTerms'`, `'actionItems'`). Stable across the
   * full lifecycle of a single composite output — section_started → section_data
   * (N) → section_completed all share the same `sectionName` value.
   *
   * Subscribers (StructuredOutputStreamWidget per task 114b) use this as the
   * map key in `sections: Record<sectionName, SectionState>`. The key choice
   * makes the renderer schema-position-agnostic — coordination point count drops
   * from 5 (schema-on-action + schema-aware widget) to 2 (section name + state).
   *
   * ADR-015 binding: section names are configuration identifiers (Tier 1 safe).
   */
  sectionName?: string;

  /**
   * Human-readable label for the section's header in the renderer. Optional on
   * `section_started`. When absent, subscribers may humanize `sectionName` for
   * display (e.g. camelCase → "Camel Case"). Subscribers SHOULD prefer this
   * field over deriving from `sectionName` because the playbook author may have
   * chosen a different on-screen label.
   *
   * ADR-015 binding: display labels are configuration metadata (Tier 1 safe).
   */
  displayLabel?: string;

  /**
   * Zero-based index of this section within the composite playbook's declared
   * `sections[]` array. Optional on `section_started`. Subscribers MAY use this
   * to render sections in declaration order; however, per FR-53 the SSE emit
   * order is COMPLETION order (B may complete before A even if A was declared
   * first), so the renderer should treat `sectionIndex` as a stable sort hint
   * rather than the canonical display order.
   *
   * ADR-015 binding: numeric index metadata (Tier 1 safe).
   */
  sectionIndex?: number;

  /**
   * Total number of sections declared on the composite playbook. Optional on
   * `section_started`. Subscribers MAY render progress indicators (e.g. "2 of
   * 3 sections complete") using this; absent for unmigrated playbooks (which
   * don't emit `section_*` events anyway).
   *
   * ADR-015 binding: numeric count metadata (Tier 1 safe).
   */
  totalSections?: number;

  /**
   * Incremental text content fragment to APPEND to the section's accumulated
   * text. Optional on `section_data`. When present, subscribers concatenate
   * onto `SectionState.accumulatedText`. When absent and `structuredData` is
   * present, only the structured data merges.
   *
   * Distinct from `fieldContent` on the legacy FieldDelta path: `contentDelta`
   * is keyed by section NAME not field path, and applies to an entire section's
   * text body (the Action's `TextContent` output).
   *
   * ADR-015 binding: text content carries Action structured output (Tier 3
   * allowed, same status as FieldDelta). NOT raw user message content.
   */
  contentDelta?: string;

  /**
   * Structured data payload from the Action's `StructuredData` output. Optional
   * on `section_data` and `section_completed`. When present, subscribers MERGE
   * (replace shallow) onto `SectionState.structuredData`. The widget renders
   * this per the section's display contract (e.g. labeled key-value blocks,
   * bulleted lists, etc.).
   *
   * Typed `unknown` (NOT `any`) — subscribers narrow before use. The BFF mirror
   * is `CompositeSectionResult.StructuredData` (JsonElement?), so on the wire
   * this can be any JSON-serializable value. Widget renderers SHOULD treat this
   * defensively (e.g. typeof-check before render).
   *
   * ADR-015 binding: structured data carries Action structured output (Tier 3
   * allowed). NOT a free-form vector for raw user content.
   */
  structuredData?: unknown;

  /**
   * Final accumulated text for the section, emitted with `section_completed`.
   * Optional. When present, REPLACES `SectionState.accumulatedText` entirely
   * (subscribers SHOULD prefer this over the accumulated delta sum, because
   * the server may have applied final post-processing). When absent,
   * subscribers retain the accumulated `contentDelta` sum.
   *
   * ADR-015 binding: same Tier-3 status as `contentDelta`.
   */
  finalContent?: string;

  /**
   * Final structured data for the section, emitted with `section_completed`.
   * Optional. When present, REPLACES `SectionState.structuredData` entirely.
   * When absent, subscribers retain merged-data state from preceding
   * `section_data` events.
   *
   * ADR-015 binding: same Tier-3 status as `structuredData`.
   */
  finalStructuredData?: unknown;

  /**
   * Optional citation list for the section, per NFR-A3 trust model. Each
   * citation is a deterministic identifier referencing a source document
   * passage. Optional on `section_completed`. Subscribers render these below
   * the section content per the existing citation-rendering convention.
   *
   * Typed loosely (array of opaque records) — the citation shape is owned by
   * the BFF `Citation`/`CitedSource` contract; subscribers cross-reference IDs
   * against their own state when needed.
   *
   * ADR-015 binding: citation IDs are deterministic identifiers (Tier 1 safe).
   * Citation BODIES (passage text) are NOT carried on this event; subscribers
   * resolve them via the BFF passage-retrieval path.
   */
  citations?: ReadonlyArray<Record<string, unknown>>;

  // ── R6 Pillar 6c reverse-flow fields ──────────────────────────────────────
  //
  // Four discriminants share this field block (`user_selection`, `tab_edited`,
  // `tab_focused`, `tab_provenance_clicked`), partitioned by required
  // membership (see the `type` JSDoc above for the per-discriminant required
  // field list). All fields are optional on the base type so existing
  // subscribers (which never touch them) remain type-safe. Subscribers that
  // handle these events MUST narrow on `event.type` before accessing.
  //
  // Added by R6 task 060 (D-C-13) per spec FR-38 + ADR-030 additive-types
  // rule (4 channels preserved — no 5th channel introduced) + ADR-015
  // BINDING. The reverse-flow set completes Pillar 6c's tri-directional model
  // alongside task 059's `context.*` execution-trace events.
  //
  // ADR-015 GOVERNANCE NOTE: with ONE intentional exception
  // (`selectionText` — user-visible-by-design per FR-38), every field below
  // is a deterministic identifier (GUID/ULID string), ISO-8601 timestamp
  // string, enumerated short string, or array of field NAMES (NOT values).
  // The `selectionText` field is the only user-content surface on the entire
  // workspace channel — see its JSDoc for the privacy distinction.

  // NOTE: `tabId`, `displayName`, and `targetWidgetId` field declarations
  // already exist higher in this interface and are reused by the four
  // reverse-flow discriminants — no re-declaration needed.

  /**
   * Stable session identifier correlating all events emitted within a single
   * chat session. Required on all four R6 reverse-flow discriminants
   * (`user_selection`, `tab_edited`, `tab_focused`,
   * `tab_provenance_clicked`).
   *
   * Format: GUID/ULID-like opaque string (e.g. `'session-abcd1234-...'`).
   * Matches `ChatSession.SessionId`. Subscribers (assistant + context pane)
   * filter to the active session only.
   *
   * ADR-015 binding: session ID is a deterministic identifier (Tier 1 safe).
   * NOT a user-content surface.
   */
  sessionId?: string;

  /**
   * ISO-8601 UTC timestamp at which the user-driven event occurred (client
   * clock — workspace pane is the dispatcher). Required on all four R6
   * reverse-flow discriminants.
   *
   * Format: `YYYY-MM-DDTHH:mm:ss.sssZ`. Subscribers may use for ordering
   * (e.g. last-write-wins in conflict resolution per Pillar 6b Q8).
   *
   * ADR-015 binding: timestamps are metadata (Tier 1 safe).
   */
  timestamp?: string;

  /**
   * The text the user has selected inside a workspace widget. Optional when
   * `type === 'user_selection'` (absent / undefined = selection cleared).
   *
   * **CAP: ≤200 characters.** Dispatchers MUST truncate at the source; if
   * the raw selection exceeds 200 chars, dispatchers MUST clip to 200 chars
   * (subscribers SHOULD assert and discard oversized payloads as a
   * defense-in-depth check).
   *
   * ── PRIVACY SEMANTICS (BINDING — DIFFERENT FROM ADR-015 TELEMETRY) ──
   *
   * This field is **user-visible-by-design** per FR-38 + Pillar 9 visibility
   * contract. Users explicitly select text in a workspace widget intending
   * the assistant to act on that selection ("summarize this"; "translate
   * this clause"). The selected text IS the payload the user is sharing —
   * carrying it on this event is the entire reason the event exists.
   *
   * This DIFFERS from ADR-015 telemetry-prohibited content: the `context.*`
   * trace channel (task 059 — `tool_call_started`, `decision_made`, etc.)
   * MUST NOT log this field's value, because trace events feed Tier 1 app
   * logs / metrics dashboards. The workspace-channel `user_selection` event
   * feeds Tier 3 work-history (in-memory + opt-in chat memory) where user
   * content is explicitly allowed under ADR-015 Amendment 2026-05-17.
   *
   * Subscribers that bridge this event to telemetry MUST strip this field
   * before logging. Subscribers that bridge to LLM prompts MAY include it
   * (that is the design intent). Sister field `selectedText` on the legacy
   * `selection_changed` discriminant is functionally identical but
   * semantically narrower (drives ConversationPane chip preview only);
   * `selectionText` is the canonical FR-38 reverse-flow field consumed by
   * assistant + context pane.
   *
   * @see FR-38 — additive `workspace.*` reverse-flow event types
   * @see Pillar 9 — `SerializedDocumentViewerState.selectionText` (same
   *      privacy model: double-gated by parent tab visibility; ~200 char
   *      cap; user-visible-by-design)
   * @see ADR-015 Amendment 2026-05-17 — Tier 3 work-history allowed content
   */
  selectionText?: string;

  /**
   * Array of FIELD NAMES that the user edited in a workspace tab. Required
   * when `type === 'tab_edited'`.
   *
   * Format: string array of stable field keys (e.g.
   * `['matterName', 'practiceArea']`, `['summary', 'tldr']`). Subscribers
   * cross-reference these names against the widget's known field set to
   * decide what changed.
   *
   * **CRITICAL: this field carries field NAMES, NOT field VALUES.** A
   * dispatcher that puts the user's new value here violates ADR-015. The
   * field-name constraint is load-bearing — reviewers should flag any
   * `editedFields` entry that:
   *   - contains spaces or punctuation beyond `.` / `[]` (path separators)
   *   - exceeds ~64 characters (likely a value, not a key)
   *   - is rendered to the user verbatim (should resolve to a label first)
   *
   * Pillar 6b stale-write conflict resolution (Q8) consumes this: the
   * assistant reads `lastUserEditAt` + the set of `editedFields` to decide
   * whether a pending agent write conflicts with user intent.
   *
   * ADR-015 binding: field NAMES are configuration identifiers (Tier 1
   * safe). Field VALUES are user content (Tier 3 only) — NOT carried here.
   */
  editedFields?: string[];

  /**
   * Type of provenance the user clicked to navigate back. Required when
   * `type === 'tab_provenance_clicked'`.
   *
   * - `'chat-message'`   — user clicked a provenance affordance whose
   *                        originator was a chat message (e.g. "the message
   *                        that asked the agent to open this document").
   *                        Consumer: assistant pane scrolls to + flashes
   *                        the originating chat turn.
   * - `'playbook-node'`  — user clicked a provenance affordance whose
   *                        originator was a playbook-node execution (e.g.
   *                        "the DeliverOutput node that opened this
   *                        workspace tab"). Consumer: context pane's
   *                        execution-trace widget (task 061) scrolls to +
   *                        highlights the corresponding trace entry.
   *
   * ADR-015 binding: provenance type is an enumerated identifier (Tier 1
   * safe). The originating content is NOT carried — subscribers resolve
   * `provenanceId` against their own state to surface the originator.
   */
  provenanceType?: 'chat-message' | 'playbook-node';

  /**
   * Stable identifier of the provenance originator. Required when
   * `type === 'tab_provenance_clicked'`.
   *
   * Format:
   *   - When `provenanceType === 'chat-message'`: the chat message's
   *     `messageId` (matches `ChatMessage.MessageId` in conversation
   *     history; GUID/ULID format).
   *   - When `provenanceType === 'playbook-node'`: the playbook-node
   *     execution's `nodeExecutionId` (matches the `correlationId` on the
   *     corresponding `context.playbook_node_executing` / `..._completed`
   *     trace event from task 059).
   *
   * Subscribers cross-reference this ID against their own state to navigate
   * to the originator. The originator's content (message text, node
   * output) is NOT carried here — subscribers resolve it locally.
   *
   * ADR-015 binding: originator IDs are deterministic identifiers (Tier 1
   * safe). NOT a user-content surface.
   */
  provenanceId?: string;
}

// ---------------------------------------------------------------------------
// WizardStepEvent — convenience alias for wizard_step events
// ---------------------------------------------------------------------------

/**
 * A `workspace` channel event that drives wizard step navigation or field
 * updates from the ConversationPane (AI-directed wizard control).
 *
 * Dispatched by the ConversationPane when the AI suggests advancing a step
 * or pre-filling a field. Consumed by embedded wizard widget subscribers.
 *
 * @example
 * dispatch('workspace', {
 *   type: 'wizard_step',
 *   wizardId: 'create-matter',
 *   wizardAction: 'next',
 * });
 *
 * dispatch('workspace', {
 *   type: 'wizard_step',
 *   wizardId: 'create-matter',
 *   wizardAction: 'set-field',
 *   fieldName: 'matterName',
 *   fieldValue: 'Smith v. Jones 2026',
 * });
 */
export type WizardStepEvent = WorkspacePaneEvent & {
  type: 'wizard_step';
  wizardId: string;
  wizardAction: 'next' | 'back' | 'set-field';
};

// ---------------------------------------------------------------------------
// Context channel
// ---------------------------------------------------------------------------

/**
 * Events emitted on the `context` channel.
 *
 * The context pane dispatches these when the active document context changes,
 * when a citation is activated, or when the extraction stage advances.
 */
export interface ContextPaneEvent {
  /**
   * Discriminant for the specific context event.
   *
   * - `context_update`    — the primary context document or data changed
   * - `context_highlight` — a citation or selection should be highlighted in-document
   * - `stage_change`      — the AI extraction / analysis stage advanced
   * - `files_staged`      — one or more files were staged into the active chat session
   *                         (e.g. user uploaded files in the Conversation pane); carries
   *                         `stagedFileIds`. Subscribed by FilePreviewContextWidget
   *                         (R5 task 018 / D2-09) to surface a preview affordance for the
   *                         newly-available files (R5 D2-06 / spec NFR-09).
   * - `file_selected`     — the user selected a single staged file for preview / focused
   *                         action (e.g. clicked a chip or per-file "Summarize this only"
   *                         affordance, R5 task 021 / D2-12); carries `selectedFileId`.
   *                         Subscribed by FilePreviewContextWidget to switch its preview
   *                         to the chosen file (R5 D2-06 / spec NFR-09).
   *
   * ── R6 Pillar 6c execution-trace events (FR-37 / D-C-12 / task 059) ───────
   *
   * The six discriminants below feed the Pillar 6c execution-trace widget in
   * the Context pane (task 061), a Claude-Code-like ordered timeline of the
   * agent's deterministic activity (tool calls, knowledge retrievals,
   * playbook-node execution, decisions). They are dispatched by the emission
   * sites wired in task 063 (BFF SSE → PaneEventBus).
   *
   * ADR-015 BINDING (CRITICAL): trace events log tool name + decision +
   * timestamp ONLY. They MUST NOT carry user message text, document content,
   * extracted text, or retrieved knowledge bodies. Payload typing is
   * structurally constrained to deterministic IDs, numeric metrics, booleans,
   * enum-like short strings, and ISO-8601 timestamps. The type system makes
   * user-content smuggling IMPOSSIBLE by construction — there is no
   * `unknown` / `any` / `object` field on any new discriminant. See
   * `notes/task-059-context-pane-events-evidence.md` for the audit trace.
   *
   * - `tool_call_started`        — chat agent has invoked a registered tool;
   *                                carries `toolName`, `timestamp`,
   *                                `sessionId`, and optional `correlationId`.
   * - `tool_call_completed`      — tool invocation has returned; carries
   *                                `toolName`, `durationMs`, `success`,
   *                                `timestamp`, `sessionId`,
   *                                `correlationId?`. NO result body.
   * - `knowledge_retrieved`      — knowledge retrieval (e.g. RAG hit) has
   *                                produced one result; carries
   *                                `knowledgeSourceId`, `relevanceScore`,
   *                                `timestamp`, `sessionId`,
   *                                `correlationId?`. NO retrieved content
   *                                body — the source ID is enough for the
   *                                trace widget to render a link.
   * - `playbook_node_executing`  — a playbook node has started executing;
   *                                carries `playbookId`, `nodeId`,
   *                                `timestamp`, `sessionId`,
   *                                `correlationId?`.
   * - `playbook_node_completed`  — a playbook node has finished; carries
   *                                `playbookId`, `nodeId`, `durationMs`,
   *                                `success`, `timestamp`, `sessionId`,
   *                                `correlationId?`.
   * - `decision_made`            — the agent made an enumerated decision
   *                                (e.g. `'route:summarize'`,
   *                                `'safety-block'`); carries `decision`
   *                                (short enum-like string), optional
   *                                `decisionReason` (machine summary, NOT
   *                                user text), `timestamp`, `sessionId`,
   *                                `correlationId?`.
   */
  type:
    | 'context_update'
    | 'context_highlight'
    | 'stage_change'
    | 'files_staged'
    | 'file_selected'
    | 'tool_call_started'
    | 'tool_call_completed'
    | 'knowledge_retrieved'
    | 'playbook_node_executing'
    | 'playbook_node_completed'
    | 'decision_made';

  /** Classifies the context payload (e.g. `"document"`, `"email"`, `"clause"`). */
  contextType?: string;

  /** Structured payload for `context_update` events. */
  contextData?: unknown;

  /**
   * Citation identifier for `context_highlight` events.
   * Matches the `citationId` produced by the output pane and consumed by the
   * source document viewer to scroll to / highlight the referenced range.
   */
  citationId?: string;

  /**
   * Opaque selection reference for `context_highlight` events.
   * Format is source-widget-specific (e.g. `"char:1024-1200"`).
   */
  selectionRef?: string;

  // ── files_staged / file_selected fields ──────────────────────────────────
  //
  // Carried by the two R5 chat-attachment context discriminants:
  // `files_staged` and `file_selected`. All three fields are optional on the
  // base type so existing subscribers (which never touch them) remain
  // type-safe. Subscribers that handle these events MUST narrow on
  // `event.type` before accessing these fields.
  //
  // Added by R5 task 016 (D2-06) per spec NFR-09 + ADR-030 additive-types
  // rule. File IDs are session-scoped identifiers from
  // `ChatSession.UploadedFiles[]` (R5 NFR-02; max 20 files per session).

  /**
   * Identifiers of the files staged into the current chat session. Required
   * when `type === 'files_staged'`. Subscribers cross-reference these IDs
   * against `ChatSession.UploadedFiles[]` to retrieve file metadata (name,
   * size, MIME type) for display.
   */
  stagedFileIds?: string[];

  /**
   * Identifier of the single staged file the user selected for preview or a
   * focused per-file action (e.g. "Summarize this only" affordance, R5 task
   * 021 / D2-12). Required when `type === 'file_selected'`. The ID
   * cross-references `ChatSession.UploadedFiles[]` for full metadata.
   */
  selectedFileId?: string;

  /**
   * Optional UX hint describing how the user expressed the file selection.
   * Lets the receiving widget tailor its response (e.g. focus the preview
   * pane vs. open a focused-action toolbar).
   *
   * - `'chip'`         — user clicked a file chip in the Conversation pane
   * - `'context-card'` — user clicked a file row in the Context pane card
   * - `'preview'`      — user activated a per-file preview affordance
   *
   * Optional on `type === 'file_selected'` (subscribers default to chip-like
   * behaviour when absent).
   */
  selectionSource?: 'chip' | 'context-card' | 'preview';

  // ── R6 Pillar 6c execution-trace fields ──────────────────────────────────
  //
  // Six discriminants share this field block, partitioned by required
  // membership (see the `type` JSDoc above for the per-discriminant required
  // field list). All fields are optional on the base type so existing
  // subscribers (which never touch them) remain type-safe. Subscribers that
  // handle these events MUST narrow on `event.type` before accessing.
  //
  // Added by R6 task 059 (D-C-12) per spec FR-37 + ADR-030 additive-types
  // rule + ADR-015 BINDING governance. Consumed by task 061 (execution-trace
  // widget in Context pane). Emitted by task 063 (BFF SSE → PaneEventBus
  // dispatch wiring).
  //
  // ADR-015 GOVERNANCE NOTE: No field below admits user-content payload.
  // All fields are: deterministic IDs (string), numeric metrics (number),
  // booleans, enum-like short strings, or ISO-8601 timestamp strings. There
  // is intentionally NO `unknown` / `any` / `object` / `Record<...>` field.

  /**
   * ISO-8601 UTC timestamp at which the traced event occurred (server clock
   * preferred). Required on all six R6 trace discriminants
   * (`tool_call_started`, `tool_call_completed`, `knowledge_retrieved`,
   * `playbook_node_executing`, `playbook_node_completed`, `decision_made`).
   *
   * Format: `YYYY-MM-DDTHH:mm:ss.sssZ`. Always present so the trace widget
   * can render an ordered timeline. Subscribers ignore on legacy
   * (non-trace) discriminants.
   *
   * ADR-015 binding: timestamps are metadata (Tier 1 safe).
   */
  timestamp?: string;

  /**
   * Stable session identifier correlating all events emitted within a
   * single chat session. Required on all six R6 trace discriminants.
   *
   * Format: GUID/ULID-like opaque string (e.g.
   * `'session-abcd1234-...'`). Matches `ChatSession.SessionId`. Subscribers
   * use this to filter the trace widget to the active session only.
   *
   * ADR-015 binding: session ID is a deterministic identifier (Tier 1
   * safe). NOT a user-content surface.
   */
  sessionId?: string;

  /**
   * Optional cross-system correlation identifier (e.g. the BFF's
   * `HttpContext.TraceIdentifier`) for linking the trace event back to the
   * originating HTTP / playbook execution / Service Bus job. Optional on
   * all six R6 trace discriminants — emitters include when available;
   * subscribers tolerate absence.
   *
   * ADR-015 binding: correlation IDs are metadata (Tier 1 safe).
   */
  correlationId?: string;

  /**
   * Name of the tool that was invoked. Required when
   * `type === 'tool_call_started'` or `type === 'tool_call_completed'`.
   *
   * Format: the registered tool's `Name` field from `sprk_analysistool`
   * (e.g. `'DocumentSearch'`, `'invoke_playbook'`). Short, enumerated
   * identifier — NOT a free-form description, NOT the user's prompt that
   * triggered the call.
   *
   * ADR-015 binding: tool names are configuration identifiers (Tier 1
   * safe). The 2026-05-17 amendment explicitly lists "tool names" as
   * acceptable Tier 2 audit content; they are equally safe in Tier 1
   * trace events.
   */
  toolName?: string;

  /**
   * Wall-clock duration of the completed operation in milliseconds.
   * Required when `type === 'tool_call_completed'` or
   * `type === 'playbook_node_completed'`.
   *
   * Integer ≥ 0. Trace widget renders as e.g. "12 ms" / "1.2 s".
   *
   * ADR-015 binding: timings are explicitly allowed in logs ("sizes and
   * counts, error codes and timings").
   */
  durationMs?: number;

  /**
   * Outcome flag of the completed operation. Required when
   * `type === 'tool_call_completed'` or `type === 'playbook_node_completed'`.
   *
   * `true` = succeeded; `false` = failed (no error detail field — the
   * trace widget can fetch detail from `correlationId` via the audit log
   * if needed).
   *
   * ADR-015 binding: outcome booleans are metadata (Tier 1 safe).
   */
  success?: boolean;

  /**
   * Identifier of the knowledge source that produced a retrieval hit.
   * Required when `type === 'knowledge_retrieved'`.
   *
   * Format: the registered source's stable ID from `sprk_knowledgesource`
   * (e.g. `'kb-corp-policy-en'`, `'azure-search:legal-precedents'`). NOT
   * the retrieved content, NOT the retrieval query, NOT the user prompt.
   *
   * ADR-015 binding: source identifiers are configuration metadata (Tier 1
   * safe). The trace widget can link to the source by ID; the retrieved
   * content body is intentionally not carried on this event.
   */
  knowledgeSourceId?: string;

  /**
   * Relevance / similarity score for a knowledge retrieval hit. Required
   * when `type === 'knowledge_retrieved'`.
   *
   * Range: [0.0, 1.0] (normalized cosine similarity / re-ranker score).
   * Trace widget renders as e.g. "0.87" or a filled bar.
   *
   * ADR-015 binding: scores are numeric metrics (Tier 1 safe). Does not
   * leak content.
   */
  relevanceScore?: number;

  /**
   * Identifier of the playbook the executing node belongs to. Required
   * when `type === 'playbook_node_executing'` or
   * `type === 'playbook_node_completed'`.
   *
   * Format: stable ID from `sprk_aiplaybook` (e.g.
   * `'summarize-document-for-chat@v1'`).
   *
   * ADR-015 binding: playbook IDs are configuration metadata (Tier 1
   * safe).
   */
  playbookId?: string;

  /**
   * Identifier of the playbook node being executed. Required when
   * `type === 'playbook_node_executing'` or
   * `type === 'playbook_node_completed'`.
   *
   * Format: stable node key from the playbook definition (e.g.
   * `'extract-entities'`, `'deliver-output'`). NOT the node's output, NOT
   * the input.
   *
   * ADR-015 binding: node IDs are configuration metadata (Tier 1 safe).
   */
  nodeId?: string;

  /**
   * Short enum-like identifier of the agent decision. Required when
   * `type === 'decision_made'`.
   *
   * MUST be a stable, machine-enumerated value (e.g.
   * `'route:summarize'`, `'route:invoke_playbook'`, `'safety-block'`,
   * `'guardrail-redirect'`). MUST NOT be a free-form natural-language
   * sentence. The trace widget enumerates known values and renders them
   * with intent-aware icons.
   *
   * ADR-015 binding: decision codes are enumerated identifiers (Tier 1
   * safe). The "short enum-like" wording is load-bearing — emitters that
   * pass a paragraph of user prompt text here are violating ADR-015.
   * Reviewers should flag any `decision` value > ~64 characters or
   * containing spaces beyond a colon separator as suspect.
   */
  decision?: string;

  /**
   * Optional MACHINE summary of WHY the decision was made (e.g.
   * `'capability-router:summarize-intent-matched'`,
   * `'safety-pipeline:groundedness<0.5'`). Optional when
   * `type === 'decision_made'`.
   *
   * NOT user message text. NOT a verbatim quote of any chat input. NOT a
   * formatted natural-language sentence intended for end-user display.
   * The trace widget exposes this to the user as a debug-style annotation;
   * the field is intentionally constrained to deterministic emitter-side
   * formatting so reviewers can audit emission sites for ADR-015
   * compliance.
   *
   * ADR-015 binding: this is the ONLY new field on the entire trace
   * surface whose name suggests a possible free-text vector. The JSDoc
   * contract above + the "no `unknown` / `any` / `object`" rule on the
   * base type together constrain its emitter responsibility. Emitters
   * MUST NOT pass user text here.
   */
  decisionReason?: string;
}

// ---------------------------------------------------------------------------
// Conversation channel
// ---------------------------------------------------------------------------

/**
 * Default widget configuration delivered in a `playbook-selected` event.
 *
 * Describes a workspace widget that should be pre-loaded when a playbook is
 * selected. WorkspacePane uses this list to seed its initial tab set.
 */
export interface PlaybookWidgetConfig {
  /** Widget type key (matches WorkspaceWidgetRegistry entry). */
  widgetType: string;
  /** Optional initial data payload for the widget. */
  widgetData?: unknown;
  /** Optional display name override for the tab. */
  displayName?: string;
}

/**
 * Events emitted on the `conversation` channel.
 *
 * The conversation pane dispatches these to signal user-initiated input,
 * playbook switches, and refinement requests to other panes.
 */
export interface ConversationPaneEvent {
  /**
   * Discriminant for the specific conversation event.
   *
   * - `suggestion`        — AI suggested a follow-up prompt or action to the user
   * - `playbook_change`   — the active AI playbook was switched (legacy / in-chat switch)
   * - `playbook-selected` — user selected a playbook from the PlaybookGalleryWidget
   *                         (Stage 1 → Stage 2 transition; carries full playbook config)
   * - `refine_request`    — the user requested refinement of a previous output
   * - `first_message`     — user sent or selected their first message (Welcome → Stage 2)
   *                         Dispatched by ConversationPane on prompt button click.
   *                         ShellStageManager marks hasSession=true on receipt.
   */
  type: 'suggestion' | 'playbook_change' | 'playbook-selected' | 'refine_request' | 'first_message';

  /** Human-readable suggestion text when `type === 'suggestion'`. */
  suggestionText?: string;

  /**
   * Identifier of the new playbook when `type === 'playbook_change'` or `'playbook-selected'`.
   * Matches the `playbookId` in the playbook catalog.
   */
  playbookId?: string;

  /** Display name of the new playbook (for UI reflection without catalog lookup). */
  playbookName?: string;

  /**
   * Default workspace widgets for the selected playbook.
   *
   * Present when `type === 'playbook-selected'`. If non-empty, WorkspacePane
   * seeds its tabs with these widgets on playbook selection. If empty or absent,
   * the workspace retains its current tabs.
   */
  defaultWidgets?: PlaybookWidgetConfig[];

  /**
   * Whether this playbook is exclusive (guardrail mode).
   *
   * Present when `type === 'playbook-selected'`. When `true`, WorkspacePane
   * clears all existing tabs before seeding the playbook's defaultWidgets.
   * When `false` (or absent), existing tabs are preserved.
   */
  isExclusive?: boolean;

  /**
   * Reference ID of the prior AI turn being refined when `type === 'refine_request'`.
   * Consumers use this to locate the original output and show a diff.
   */
  targetTurnId?: string;

  /** Structured refinement instruction payload for `refine_request`. */
  refineData?: unknown;
}

// ---------------------------------------------------------------------------
// Safety channel
// ---------------------------------------------------------------------------

/**
 * Events emitted on the `safety` channel.
 *
 * The safety layer dispatches these after retroactive groundedness annotation
 * or when capability availability changes.
 */
export interface SafetyPaneEvent {
  /**
   * Discriminant for the specific safety event.
   *
   * - `safety_annotation`  — groundedness annotation for an AI output is ready
   * - `capability_change`  — one or more AI capabilities changed availability
   */
  type: 'safety_annotation' | 'capability_change';

  /**
   * Structured groundedness result when `type === 'safety_annotation'`.
   * Schema defined by the safety perimeter service.
   */
  groundedness?: object;

  /**
   * Citation evidence map when `type === 'safety_annotation'`.
   * Keys are claim IDs; values are citation objects.
   */
  citations?: object;

  /**
   * Confidence tier when `type === 'safety_annotation'`.
   * One of: `"high"` | `"medium"` | `"low"` | `"unverified"`.
   */
  confidence?: string;

  /**
   * Map of capability identifiers to their new availability status
   * when `type === 'capability_change'`.
   */
  capabilities?: Record<string, boolean>;
}

// ---------------------------------------------------------------------------
// Channel → event type map
// ---------------------------------------------------------------------------

/**
 * Maps each PaneChannel to its corresponding event payload type.
 *
 * Used by PaneEventBus to enforce that dispatch and subscribe calls receive
 * the correct payload type for the given channel — no `any` casts needed.
 *
 * @example
 * function dispatch<C extends PaneChannel>(channel: C, event: PaneChannelEventMap[C]): void { ... }
 */
export interface PaneChannelEventMap {
  workspace: WorkspacePaneEvent;
  context: ContextPaneEvent;
  conversation: ConversationPaneEvent;
  safety: SafetyPaneEvent;
}

// ---------------------------------------------------------------------------
// Handler type alias
// ---------------------------------------------------------------------------

/**
 * A typed event handler for a given PaneChannel.
 *
 * @example
 * const handler: PaneEventHandler<'context'> = (event) => {
 *   if (event.type === 'context_highlight') { ... }
 * };
 */
export type PaneEventHandler<C extends PaneChannel> = (event: PaneChannelEventMap[C]) => void;
