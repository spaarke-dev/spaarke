/**
 * Host-agnostic types for the SmartTodoWidget.
 *
 * Mirrors the relevant shape of `sprk_todo` carrying only what the widget
 * needs. Solution-specific richer types (LW `ITodo` with score factors,
 * AssignedTo display name, etc.) stay in the host source for now and may be
 * hoisted as a follow-up task.
 *
 * Reference: src/solutions/LegalWorkspace/src/types/entities.ts (ITodo) and
 * src/solutions/LegalWorkspace/src/services/queryHelpers.ts (TODO_SELECT_FIELDS).
 */

/**
 * Minimal subset of `Xrm.WebApi` consumed by the widget.
 *
 * Why this shape (rather than `unknown`): the widget needs to call
 * `retrieveMultipleRecords` and (optionally, in future) `updateRecord`. Exposing
 * the narrow shape lets TypeScript surface obvious wiring mistakes at the
 * shim seam without dragging the full PCF ComponentFramework types into this
 * peer package's typecheck (which has no `@types/powerapps-component-framework`
 * dependency).
 */
export interface IWebApi {
  retrieveMultipleRecords: (
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ) => Promise<{ entities: Array<Record<string, unknown>>; nextLink?: string }>;
  /**
   * R4 task 102 (E-1, 2026-06-18) — the widget now renders the full Kanban
   * with drag-drop + pin persistence (`SmartTodoKanban`). Adding
   * `updateRecord` here lets the widget wrap `webApi` into a minimal
   * `IKanbanDataverseService` adapter for the hoisted hook's persistence
   * path. Optional so hosts that only need read-only widget rendering
   * (pre-102 surfaces) can satisfy the contract with just
   * `retrieveMultipleRecords`.
   */
  updateRecord?: (
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string; entityType: string }>;
  /**
   * R4 task 103 (E-2, 2026-06-18) — quick-add (UAT 7). The widget's inline
   * QuickAdd input (toolbar left slot) calls `createRecord('sprk_todo', { sprk_name })`
   * with just the title. Dataverse defaults populate statecode/statuscode,
   * and the widget refetches on success.
   *
   * On failure (e.g., Dataverse rejects due to other required fields), the
   * widget surfaces a graceful error with a "Open full wizard" link that
   * dispatches the host's full `<CreateTodoWizard>` via `onAddTodo`.
   *
   * Optional so hosts that wire the widget without quick-add support
   * (read-only surfaces, future Direct widget mounts in SpaarkeAi that
   * intentionally omit the field) can satisfy the contract without it.
   * When absent, the quick-add input row is suppressed.
   */
  createRecord?: (
    entityLogicalName: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string; entityType: string }>;
}

/**
 * One row from `sprk_todo` as the widget needs it.
 *
 * Only the fields the widget renders or sorts by are typed here. Additional
 * fields from `TODO_SELECT_FIELDS` (priority/effort scores, etc.) flow through
 * untyped via the index signature — the widget does not need them for the
 * minimal Kanban render. A future hoist can replace this with the richer
 * `ITodo` from `@spaarke/legal-workspace` types.
 */
export interface ITodoRecord {
  sprk_todoid: string;
  sprk_name: string;
  sprk_description?: string;
  sprk_duedate?: string;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  sprk_todocolumn?: string | null;
  sprk_todopinned?: boolean;
  statecode: number;
  statuscode: number;
  createdon?: string;
  modifiedon?: string;
  /** Index signature for extra OData fields the host may consume. */
  [key: string]: unknown;
}

/**
 * Regarding context — the workspace's "what record is this widget filtering by"
 * shape. When the widget mounts inside a LegalWorkspace tab, the workspace's
 * regarding lookup is wired in here.
 *
 * `entityLogicalName` examples: 'sprk_matter', 'sprk_project', 'sprk_invoice',
 * 'sprk_workassignment'. Maps to the matching `_sprk_regarding<X>_value`
 * lookup column on `sprk_todo` (per R3 polymorphic-resolver pattern, ADR-024).
 *
 * When `null`, the widget does NOT apply a regarding filter (it falls back to
 * the owner/business-unit clause only — matching the standalone Code Page path).
 */
export interface IRegardingContext {
  entityLogicalName: string;
  recordId: string;
}

/**
 * Optional FeedTodoSync bridge — wired by the LegalWorkspace shim so the
 * widget can react to cross-block lifecycle events (e.g., a todo dismissed
 * from the Activity Feed should disappear from the widget's list).
 *
 * The widget itself does NOT subscribe to any host-specific context (per R4
 * task 020 user decision — see widget-surface-audit.md §7 OQ-1 option b).
 * The shim subscribes to the host's FeedTodoSyncContext and forwards the
 * lifecycle events through this prop.
 *
 * `notifyChange` is invoked by the widget AFTER its own local mutations
 * (e.g., the user dismisses a card here) so the host context can broadcast
 * to sibling blocks. `subscribe` is invoked by the widget to receive
 * external lifecycle changes — when one fires for a todoId in the current
 * list, the widget refetches.
 */
export interface IFeedSyncBridge {
  /** Producer side — widget tells host of a local mutation. */
  notifyChange: (todoId: string, isActive: boolean) => void;
  /** Consumer side — host tells widget of an external mutation. */
  subscribe: (listener: (todoId: string, isActive: boolean) => void) => () => void;
}
