/**
 * OData queries for the Smart To Do list (`sprk_todo`) — one copy for every host (#2972).
 *
 * Before #2972 the LegalWorkspace and SmartTodo code pages each kept a copy of these builders in their own
 * `services/queryHelpers.ts`, and the copies had drifted: the code page selected `sprk_priority` and the
 * regarding-record text fields, LegalWorkspace did not; the code page scoped the list to the assignee, LegalWorkspace
 * to the owner. The select list here is the union. The two scopes are both kept, as `TodoListScope`, because each
 * surface's choice is a product decision recorded where it was made (code page: R4 task 031 / OD-2 "Assigned to Me";
 * workspace: the my / all workspace scope).
 */

/**
 * `sprk_todo` columns every Smart To Do surface reads.
 *
 * statuscode is the canonical lifecycle field (task 009):
 *   1 = Open, 659490001 = In Progress, 2 = Completed, 659490002 = Dismissed.
 */
export const TODO_SELECT_FIELDS: readonly string[] = [
  'sprk_todoid',
  'sprk_name',
  'sprk_description',
  'sprk_notes',
  // The raw Priority choice — the card's priority glyph reads it (task 012).
  'sprk_priority',
  'sprk_priorityscore',
  'sprk_effortscore',
  'sprk_duedate',
  'sprk_completedon',
  'sprk_todocolumn',
  'sprk_todopinned',
  'statecode',
  'statuscode',
  '_sprk_assignedto_value', // Lookup → contact (display name via formatted value)
  '_ownerid_value',
  'createdon',
  'modifiedon',
  // Regarding-record resolver text fields (ADR-024) — the search box matches them (DEF-11 Part 3).
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
];

/**
 * Optional parent-record filter for the R4 FR-34 openTodos launch flow. `entityType` is the parent's logical name
 * (e.g. `sprk_matter`), `recordId` its GUID.
 */
export interface ITodoRegardingFilter {
  entityType: string;
  recordId: string;
}

/**
 * Parent entity logical name → sprk_todo's entity-specific regarding lookup (ADR-024 dual-field pattern). Mirrors
 * `@spaarke/ui-components/hooks/toolbarLaunchDefaults.ts SUPPORTED_TODO_PARENTS` — both derive from the sprk_todo
 * schema.
 */
export const TODO_REGARDING_LOOKUP_BY_ENTITY: Readonly<Record<string, string>> = {
  sprk_matter: 'sprk_regardingmatter',
  sprk_project: 'sprk_regardingproject',
  sprk_event: 'sprk_regardingevent',
  sprk_invoice: 'sprk_regardinginvoice',
  sprk_budget: 'sprk_regardingbudget',
  sprk_workassignment: 'sprk_regardingworkassignment',
};

/**
 * Which to-dos a list shows.
 *
 * - `assignedTo` — the SmartTodo code page: to-dos assigned to the user's contact (`sprk_assignedto` is a contact
 *   lookup; pass the contact GUID, not the systemuser GUID), optionally narrowed to one parent record.
 * - `owner` — the LegalWorkspace workspace: to-dos owned by the user (`my`, the default) or by the user's business
 *   unit (`all` with `businessUnitId`).
 */
export type TodoListScope =
  | { kind: 'assignedTo'; contactId: string; regardingFilter?: ITodoRegardingFilter }
  | { kind: 'owner'; userId: string; scope?: 'my' | 'all'; businessUnitId?: string };

/** The `$filter` clause that selects the scope's to-dos. */
export function buildTodoScopeClause(scope: TodoListScope): string {
  if (scope.kind === 'owner') {
    return scope.scope === 'all' && scope.businessUnitId
      ? `_owningbusinessunit_value eq ${scope.businessUnitId}`
      : `_ownerid_value eq ${scope.userId}`;
  }
  const clauses = [`_sprk_assignedto_value eq ${scope.contactId}`];
  const lookup = scope.regardingFilter ? TODO_REGARDING_LOOKUP_BY_ENTITY[scope.regardingFilter.entityType] : undefined;
  if (scope.regardingFilter && lookup) {
    clauses.push(`_${lookup}_value eq ${scope.regardingFilter.recordId}`);
  }
  // An unsupported regarding entity type is ignored (the list stays "assigned to me").
  return clauses.join(' and ');
}

/**
 * The OData query for the active (Kanban-visible) to-dos in `scope`: statecode 0 and statuscode Open or In Progress,
 * sorted by priority score descending, then due date ascending.
 */
export function buildActiveTodosQuery(scope: TodoListScope): string {
  const activeClause = 'statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)';
  const filter = `${buildTodoScopeClause(scope)} and ${activeClause}`;
  return `?$select=${TODO_SELECT_FIELDS.join(',')}&$filter=${filter}&$orderby=sprk_priorityscore desc,sprk_duedate asc`;
}

/** A stable string for `scope`, for React effect dependencies (callers pass a new object each render). */
export function todoListScopeKey(scope: TodoListScope | null | undefined): string {
  if (!scope) return '';
  if (scope.kind === 'owner') {
    return `owner|${scope.userId}|${scope.scope ?? ''}|${scope.businessUnitId ?? ''}`;
  }
  return `assignedTo|${scope.contactId}|${scope.regardingFilter?.entityType ?? ''}|${scope.regardingFilter?.recordId ?? ''}`;
}
