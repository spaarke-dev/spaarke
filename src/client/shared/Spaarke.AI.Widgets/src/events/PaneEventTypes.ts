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
 * Events emitted on the `workspace` channel.
 *
 * The workspace pane dispatches these when widgets load, update, or trigger
 * actions, and when the active tab changes.
 */
export interface WorkspacePaneEvent {
  /**
   * Discriminant for the specific workspace event.
   *
   * - `widget_load`        — a widget has been mounted and is ready
   * - `widget_update`      — widget data has changed (refresh signal)
   * - `widget_action`      — a user action was performed inside a widget
   * - `tab_change`         — the active workspace tab changed
   * - `tab_count_change`   — the number of open tabs changed (drives Stage 3↔4 transitions)
   * - `selection_changed`  — the user selected (or cleared) text inside a widget
   * - `tabs_clear`         — all workspace tabs should be cleared (exclusive playbook selection)
   * - `wizard_step`        — the ConversationPane requests a wizard step navigation or field update
   * - `entity_resolved`    — Dataverse entity context has been resolved (drives Stage 2→3)
   * - `session_reset`      — the active session was cleared; all panes reset to Stage 1
   */
  type: 'widget_load' | 'widget_update' | 'widget_action' | 'tab_change' | 'tab_count_change' | 'selection_changed' | 'tabs_clear' | 'wizard_step' | 'entity_resolved' | 'session_reset';

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
   */
  type: 'context_update' | 'context_highlight' | 'stage_change';

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
