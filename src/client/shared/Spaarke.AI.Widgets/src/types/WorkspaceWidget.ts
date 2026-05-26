/**
 * @spaarke/ai-widgets — WorkspaceWidget interface
 *
 * Defines the contract that every Workspace pane widget MUST implement.
 * The Workspace pane renders the primary AI tool output as a tabbed set of
 * widgets; each tab hosts one WorkspaceWidget instance.
 *
 * Generic parameters:
 * - TData    — the shape of data the widget renders (e.g. RedlineResult).
 * - TActions — a string union of action identifiers the widget exposes
 *              (e.g. `'save' | 'export' | 'apply'`). Use `never` for
 *              read-only widgets that expose no user actions.
 *
 * Task: AIPU2-071
 * FR:   FR-201
 */

import type React from 'react';
import type { WidgetRenderContext, Selection, ActionResult, WidgetState } from './shared';

/**
 * Core contract for every widget rendered inside a Workspace pane tab.
 *
 * Implementors MUST satisfy all required members. Optional callback members
 * (`onSelectionChange`, `onActionComplete`) are wired by the shell when a
 * widget is mounted; do not assume they are called only when defined.
 *
 * @template TData    - Shape of data delivered to `render()`. Defaults to `unknown`
 *                      so the registry can store heterogeneous widget types without
 *                      casting. Concrete widgets SHOULD supply a specific type.
 * @template TActions - String-union of action keys the widget can complete.
 *                      Defaults to `never` (no actions). Use `ActionResult<TActions>`
 *                      to type the `onActionComplete` callback.
 *
 * @example
 * ```ts
 * type RedlineActions = 'accept' | 'reject' | 'export';
 *
 * class RedlineViewerWidget implements WorkspaceWidget<RedlineResult, RedlineActions> {
 *   render(data, context) { ... }
 *   serializeState() { ... }
 *   restoreState(state) { ... }
 * }
 * ```
 */
export interface WorkspaceWidget<TData = unknown, TActions extends string = never> {
  // -------------------------------------------------------------------------
  // Display
  // -------------------------------------------------------------------------

  /**
   * Produce the React element tree for this widget.
   *
   * Called on every render cycle. When `isLoading` is `true`, implementations
   * SHOULD render a Fluent UI `Spinner` or skeleton placeholder rather than
   * the empty/stale data state. The shell passes `isLoading: true` during
   * initial data fetch and after a session restore triggers a data re-fetch.
   *
   * @param data      - The widget's data payload. May be the zero-value of
   *                    `TData` (e.g. empty array, empty object) while loading.
   * @param context   - Ambient session and entity context from the shell.
   * @returns A React element — MUST NOT return `null` (return a loading state instead).
   */
  render(data: TData, context: WidgetRenderContext): React.ReactElement;

  // -------------------------------------------------------------------------
  // Interaction — user actions
  // -------------------------------------------------------------------------

  /**
   * Declared actions the user can take within this widget.
   *
   * The shell uses this to render an action toolbar above the widget tab.
   * Each key is an action identifier; the value is a descriptor the shell
   * uses to label and icon the toolbar button.
   *
   * Omit (or set to `{}`) for read-only widgets that expose no toolbar actions.
   * The type parameter `TActions` constrains which keys are valid.
   *
   * @example
   * actions: {
   *   accept: { label: 'Accept All', icon: 'Checkmark24Regular' },
   *   reject: { label: 'Reject All', icon: 'Dismiss24Regular' },
   * }
   */
  actions?: Partial<Record<TActions, WidgetActionDescriptor>>;

  // -------------------------------------------------------------------------
  // Cross-pane communication
  // -------------------------------------------------------------------------

  /**
   * Called by the shell when the user makes a selection within this widget.
   *
   * Implement to notify the Context pane (citation sync, source highlight) or
   * the Conversation pane (refinement suggestion) of the selected content.
   * The shell wires this callback automatically on mount; widgets MUST NOT
   * invoke it directly — use the pane event bus for outbound events instead.
   *
   * @param selection - Describes what the user selected.
   */
  onSelectionChange?: (selection: Selection) => void;

  /**
   * Called by the shell after a user-triggered action completes.
   *
   * Widgets MUST call this when an action declared in `actions` finishes
   * (success or failure). The shell uses the result to update the toolbar
   * state and optionally show a toast notification.
   *
   * @param result - The outcome of the completed action.
   */
  onActionComplete?: (result: ActionResult<TActions>) => void;

  // -------------------------------------------------------------------------
  // Persistence — work history save / restore
  // -------------------------------------------------------------------------

  /**
   * Serialize the current widget state for Cosmos DB persistence.
   *
   * Called by the shell before navigating away from the session, on session
   * save, and periodically during active use (every ~30 seconds).
   *
   * IMPORTANT (D-08 — data-refreshed restore): Serialize query parameters and
   * layout hints only. Do NOT serialize the fetched data — on restore the
   * widget will re-fetch fresh results using the stored query parameters.
   *
   * @returns A `WidgetState` snapshot containing `widgetType`, `version`,
   *          `queryParams`, optional `layout`, and an ISO 8601 `timestamp`.
   */
  serializeState(): WidgetState<TData>;

  /**
   * Restore widget state from a previously serialized snapshot.
   *
   * Called by the shell when the user resumes a saved session. Implementations
   * SHOULD use `state.queryParams` to re-issue their data fetch and
   * `state.layout` to restore scroll position, active sub-tab, etc.
   *
   * The shell sets `isLoading: true` on the next `render()` call immediately
   * after invoking `restoreState`, so widgets can initiate async fetches here
   * without a flicker.
   *
   * @param state - The snapshot previously returned by `serializeState()`.
   * @returns A Promise that resolves when the widget has initiated its restore
   *          (not necessarily when the data fetch completes).
   */
  restoreState(state: WidgetState<TData>): Promise<void>;
}

// ---------------------------------------------------------------------------
// WidgetActionDescriptor
// ---------------------------------------------------------------------------

/**
 * Metadata for a single action declared in `WorkspaceWidget.actions`.
 * Used by the shell to render toolbar buttons.
 */
export interface WidgetActionDescriptor {
  /**
   * Human-readable label shown on the toolbar button.
   * Keep concise — the shell truncates at ~20 characters in compact layouts.
   */
  label: string;

  /**
   * Optional Fluent UI icon name from the `@fluentui/react-icons` package.
   * If omitted the shell renders a generic action icon.
   * Example: `"Save24Regular"`, `"ArrowExportLtr24Regular"`.
   */
  icon?: string;

  /**
   * When `true`, the shell renders this action as destructive (red tint).
   * Use for irreversible operations such as "Delete" or "Clear All".
   */
  destructive?: boolean;
}
