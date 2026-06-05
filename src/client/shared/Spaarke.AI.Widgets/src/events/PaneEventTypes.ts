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
    | 'streaming_complete';

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
   */
  type: 'context_update' | 'context_highlight' | 'stage_change' | 'files_staged' | 'file_selected';

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
