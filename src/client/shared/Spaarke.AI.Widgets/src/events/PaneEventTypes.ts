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
   * - `widget_load`   — a widget has been mounted and is ready
   * - `widget_update` — widget data has changed (refresh signal)
   * - `widget_action` — a user action was performed inside a widget
   * - `tab_change`    — the active workspace tab changed
   */
  type: 'widget_load' | 'widget_update' | 'widget_action' | 'tab_change';

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
}

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
 * Events emitted on the `conversation` channel.
 *
 * The conversation pane dispatches these to signal user-initiated input,
 * playbook switches, and refinement requests to other panes.
 */
export interface ConversationPaneEvent {
  /**
   * Discriminant for the specific conversation event.
   *
   * - `suggestion`      — AI suggested a follow-up prompt or action to the user
   * - `playbook_change` — the active AI playbook was switched
   * - `refine_request`  — the user requested refinement of a previous output
   */
  type: 'suggestion' | 'playbook_change' | 'refine_request';

  /** Human-readable suggestion text when `type === 'suggestion'`. */
  suggestionText?: string;

  /**
   * Identifier of the new playbook when `type === 'playbook_change'`.
   * Matches the `playbookId` in the playbook catalog.
   */
  playbookId?: string;

  /** Display name of the new playbook (for UI reflection without catalog lookup). */
  playbookName?: string;

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
