/**
 * entities.ts — Host-agnostic domain types for the hoisted rich Smart To Do
 * Kanban subtree (R5 FR-01 / task 002).
 *
 * These are PURE TYPE definitions (zero runtime, zero platform coupling). They
 * mirror the shapes the LegalWorkspace-local SmartToDo subtree consumed
 * (`src/solutions/LegalWorkspace/src/types/entities.ts` `ITodo`,
 * `src/solutions/LegalWorkspace/src/types/enums.ts` `PriorityLevel`/`EffortLevel`,
 * `src/solutions/LegalWorkspace/src/hooks/useUserPreferences.ts`
 * `ITodoKanbanPreferences`) so the hoisted components stay strongly typed WITHOUT
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
}

/**
 * Kanban threshold preferences shape (stored as JSON in sprk_preferencevalue).
 *
 * Host-agnostic mirror of the LegalWorkspace `ITodoKanbanPreferences`. The host
 * shim persists/loads it via its own preferences service; the hoisted
 * `SmartToDo` + `ThresholdSettings` consume it purely as data.
 */
export interface ITodoKanbanPreferences {
  todayThreshold: number;
  tomorrowThreshold: number;
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
