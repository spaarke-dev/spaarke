/**
 * entities.ts — Host-agnostic domain types for the hoisted rich Smart To Do
 * Kanban subtree (R5 FR-01 / task 002).
 *
 * These are PURE TYPE definitions (zero runtime, zero platform coupling). They
 * mirror the shapes the LegalWorkspace-local SmartToDo subtree consumed
 * (`src/solutions/LegalWorkspace/src/types/entities.ts` `ITodo`,
 * `src/solutions/LegalWorkspace/src/types/enums.ts` `PriorityLevel`/`EffortLevel`,
 * and the former per-host `useUserPreferences.ts` `ITodoKanbanPreferences`, now
 * `../hooks/useTodoKanbanPreferences.ts` — #2972) so the hoisted components stay strongly typed WITHOUT
 * reaching into `src/solutions/...` (ADR-012 / NFR-05).
 *
 * `ITodo` is a structural SUPERSET of the package's `IKanbanTodoLike`
 * (`./kanban.ts`) and `IKanbanCardTodo`, so it flows into `useKanbanColumns`,
 * `computeTodoScore`, and `<KanbanCard>` without transforms.
 *
 * The host (LegalWorkspace shim — task 003) binds the concrete Dataverse data
 * and passes it in as props; nothing here calls a platform API.
 */

/** Priority scoring levels (matches Dataverse sprk_priority choice: 0=Low, 1=Normal, 2=High, 3=Urgent). */
export type PriorityLevel = 'Urgent' | 'High' | 'Normal' | 'Low';

/** Effort scoring levels. */
export type EffortLevel = 'High' | 'Med' | 'Low';

/**
 * Dataverse `sprk_todo` entity — first-class To Do entity (R3 FR-09, FR-11).
 *
 * Host-agnostic mirror of the LegalWorkspace `ITodo` shape. Only the fields the
 * hoisted subtree renders / sorts / mutates are typed here.
 *
 * statuscode semantics (per task 009):
 *   - 1          = Open        (statecode 0 / Active)
 *   - 659490001  = In Progress (statecode 0 / Active)
 *   - 2          = Completed   (statecode 1 / Inactive)
 *   - 659490002  = Dismissed   (statecode 1 / Inactive)
 *
 * sprk_todocolumn (choice): 100000000=Today, 100000001=Tomorrow, 100000002=Future.
 */
export interface ITodo {
  sprk_todoid: string;
  sprk_name: string;
  sprk_description?: string;
  sprk_notes?: string;
  /** 0-100 native priority score on sprk_todo. */
  sprk_priorityscore?: number;
  /** 0-100 native effort score on sprk_todo. */
  sprk_effortscore?: number;
  sprk_duedate?: string;
  /** Formatted sprk_duedate (locale short date) — the search box matches it. */
  dueDateFormatted?: string;
  sprk_completedon?: string;
  /** Choice: 100000000=Today, 100000001=Tomorrow, 100000002=Future. */
  sprk_todocolumn?: number;
  /** Lock item in assigned Kanban column. */
  sprk_todopinned?: boolean;
  /** 0=Active, 1=Inactive. */
  statecode?: number;
  /** 1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed. */
  statuscode?: number;
  /** Display name from statuscode (populated via formatted value mapping). */
  statuscodeName?: string;
  /** Display name from sprk_assignedto systemuser lookup (populated via formatted value mapping). */
  assignedToName?: string;
  _sprk_assignedto_value?: string;
  _ownerid_value?: string;
  createdon?: string;
  modifiedon?: string;
  /** Regarding-record display name (ADR-024 resolver field) — the search box matches it. */
  sprk_regardingrecordname?: string;
  /** Regarding-record number (ADR-024 resolver field) — the search box matches it. */
  sprk_regardingrecordnumber?: string;
}

/**
 * Kanban threshold preferences shape (stored as JSON in sprk_preferencevalue).
 *
 * One envelope for every Smart To Do surface (preference type 100000000),
 * loaded and saved by `useTodoKanbanPreferences`. A save keeps the fields the
 * saving surface does not use (#2972 — the workspace used to drop the code
 * page's orientation and column order).
 */
export interface ITodoKanbanPreferences {
  todayThreshold: number;
  tomorrowThreshold: number;
  /**
   * Legacy list/card view selection (R4 FR-09). The list view is gone; the field is kept so a save does not drop
   * what an older client stored.
   */
  viewMode?: SmartTodoViewMode;
  /** Kanban board orientation (R4 FR-28..FR-30). Absent means horizontal. */
  orientation?: SmartTodoOrientation;
  /** Manual card order per column id ("Today" / "Tomorrow" / "Future") — arrays of sprk_todoid. */
  columnOrders?: Record<string, string[]>;
}

/** Persisted list/card view value (R4 FR-09). card renders the Kanban. */
export type SmartTodoViewMode = 'card' | 'list';

/** Persisted Kanban orientation (R4 FR-28). */
export type SmartTodoOrientation = 'horizontal' | 'vertical';

/** A sprk_userpreference record: one JSON value per user and preference type. */
export interface IUserPreferenceRecord {
  sprk_userpreferenceid: string;
  /** Choice discriminator (100000000 = Smart To Do Kanban). */
  sprk_preferencetype: number;
  /** JSON text. */
  sprk_preferencevalue: string;
  /** Lookup → systemuser. */
  _sprk_user_value?: string;
  createdon: string;
  modifiedon: string;
}

/**
 * Result shape returned by the host-injected mutation callbacks
 * (`onCreateTodo` / `onDismissTodo` / `onRestoreTodo`) that the hoisted
 * `SmartToDo` container awaits.
 *
 * Why this exists (Component Justification, root CLAUDE.md §11):
 *   - Existing: no host-agnostic result type in this package conveys
 *     success + new-id + error message from a persistence call.
 *   - Extension: cannot reuse the LW `IResult<T>` (it lives in
 *     `src/solutions/...` — reach-in forbidden by ADR-012/NFR-05).
 *   - Cost-of-doing-nothing: without it, `SmartToDo.handleAdd` cannot decide
 *     between optimistic-rollback vs. feed-sync-notify after a create, and
 *     `handleDismiss`/`handleRestore` cannot roll back a failed write.
 */
export interface ITodoMutationResult {
  /** True when the Dataverse write succeeded. */
  success: boolean;
  /** New/affected record id (e.g. the created sprk_todoid). */
  id?: string;
  /** Failure detail surfaced to the user. */
  error?: { message?: string };
}
