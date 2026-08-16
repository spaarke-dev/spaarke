/**
 * OData query helper utilities for Xrm.WebApi (ComponentFramework.WebApi) calls.
 *
 * These builders produce the query options strings consumed by:
 *   webApi.retrieveMultipleRecords(entityName, queryOptions, maxPageSize)
 *
 * All helpers return plain strings suitable for direct use in the `options`
 * parameter, which is appended to the entity collection URL as-is.
 */

import { EventFilterCategory } from '../types/enums';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Parameters for building a retrieveMultiple OData query string */
export interface IQueryOptions {
  select?: string[];
  filter?: string;
  orderby?: string;
  top?: number;
  expand?: string;
}

// ---------------------------------------------------------------------------
// Core builder
// ---------------------------------------------------------------------------

/**
 * Build an OData query string from structured options.
 *
 * Example:
 *   buildQuery({ select: ['sprk_name', 'sprk_status'], top: 5 })
 *   // returns "?$select=sprk_name,sprk_status&$top=5"
 */
export function buildQuery(opts: IQueryOptions): string {
  const parts: string[] = [];

  if (opts.select && opts.select.length > 0) {
    parts.push(`$select=${opts.select.join(',')}`);
  }

  if (opts.filter && opts.filter.trim().length > 0) {
    parts.push(`$filter=${opts.filter.trim()}`);
  }

  if (opts.orderby && opts.orderby.trim().length > 0) {
    parts.push(`$orderby=${opts.orderby.trim()}`);
  }

  if (opts.expand && opts.expand.trim().length > 0) {
    parts.push(`$expand=${opts.expand.trim()}`);
  }

  if (opts.top !== undefined && opts.top > 0) {
    parts.push(`$top=${opts.top}`);
  }

  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

// ---------------------------------------------------------------------------
// Broad owner filter (Matters/Projects/Invoices tabs)
// ---------------------------------------------------------------------------

/**
 * Build an OData $filter predicate that matches records where the user is:
 *   - owner (ownerid)
 *   - last modifier (modifiedby)
 *   - assigned attorney (sprk_assignedattorney1 — contact lookup)
 *   - assigned paralegal (sprk_assignedparalegal1 — contact lookup)
 *
 * The attorney/paralegal fields are contact lookups, so `contactId` is the
 * user's linked contact record resolved from `systemuser._contactid_value`.
 * When contactId is null the attorney/paralegal clauses are omitted.
 */
export function buildBroadOwnerFilter(userId: string, contactId: string | null): string {
  const clauses = [
    `_ownerid_value eq ${userId}`,
    `_modifiedby_value eq ${userId}`,
  ];

  if (contactId) {
    clauses.push(`_sprk_assignedattorney1_value eq ${contactId}`);
    clauses.push(`_sprk_assignedparalegal1_value eq ${contactId}`);
  }

  return clauses.join(' or ');
}

// ---------------------------------------------------------------------------
// Matter query helpers
// ---------------------------------------------------------------------------

/** $select fields for sprk_matter used in the My Portfolio widget */
export const MATTER_SELECT_FIELDS: string[] = [
  'sprk_matterid',
  'sprk_name',
  '_sprk_mattertype_value',  // Lookup → sprk_mattertype_ref (display via formatted value)
  '_sprk_practicearea_value',  // Lookup → sprk_practicearea_ref (display via formatted value)
  'sprk_totalbudget',
  'sprk_totalspend',
  'sprk_utilizationpercent',
  'sprk_budgetcontrols_grade',
  'sprk_guidelinescompliance_grade',
  'sprk_outcomessuccess_grade',
  'sprk_overdueeventcount',
  'sprk_status',
  '_sprk_organization_value',
  '_sprk_leadattorney_value',
  'createdon',
  'modifiedon',
];

/**
 * Build the OData $filter predicate for querying matters owned by a user.
 * Targets records where the OData ownerid equals the provided userId GUID.
 */
export function buildMatterOwnerFilter(userId: string): string {
  return `_ownerid_value eq ${userId}`;
}

/**
 * Build the full OData query string for getMattersByUser.
 *
 * Default $top is 5 (My Portfolio widget shows 5 matters).
 * Override with options.top for "View All" scenarios.
 */
export function buildMattersQuery(userId: string, top: number = 5): string {
  return buildQuery({
    select: MATTER_SELECT_FIELDS,
    filter: buildMatterOwnerFilter(userId),
    orderby: 'sprk_name asc',
    top,
  });
}

/** $select fields for sprk_matter used in the Matters tab (broader than My Portfolio) */
export const MATTER_TAB_SELECT_FIELDS: string[] = [
  'sprk_matterid',
  'sprk_matternumber',
  'sprk_mattername',
  'sprk_matterdescription',
  '_sprk_mattertype_value',  // Lookup → sprk_mattertype_ref (display via formatted value)
  '_sprk_practicearea_value',  // Lookup → sprk_practicearea_ref (display via formatted value)
  'statuscode',
  '_ownerid_value',
  '_modifiedby_value',
  '_sprk_assignedattorney1_value',
  '_sprk_assignedparalegal1_value',
  'createdon',
  'modifiedon',
];

/**
 * Build the full OData query string for the Matters tab.
 * Uses the broad owner filter (owner/modifier/attorney/paralegal).
 */
export function buildMattersTabQuery(userId: string, contactId: string | null, top: number = 50): string {
  return buildQuery({
    select: MATTER_TAB_SELECT_FIELDS,
    filter: buildBroadOwnerFilter(userId, contactId),
    orderby: 'modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// Event feed query helpers
// ---------------------------------------------------------------------------

/**
 * $select fields for sprk_event used in the Updates Feed.
 *
 * Per R3 FR-29 / OS-1, the four legacy event-todo fields (`sprk_todoflag`,
 * `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned`) are removed from
 * `sprk_event` in Phase 1 and no longer included here.
 */
export const EVENT_SELECT_FIELDS: string[] = [
  'sprk_eventid',
  'sprk_eventname',
  '_sprk_eventtype_ref_value',  // Lookup → sprk_eventtype_ref (display via formatted value)
  'sprk_description',
  'sprk_priority',
  'sprk_priorityscore',
  'sprk_effortscore',
  'sprk_estimatedminutes',
  'sprk_priorityreason',
  'sprk_effortreason',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  '_sprk_regardingrecordtype_value',  // Lookup → sprk_recordtype_ref (display via formatted value)
  '_sprk_assignedto_value',  // Lookup → contact (display name via formatted value)
  'sprk_duedate',
  'createdon',
  'modifiedon',
];

/**
 * Build the OData $filter predicate for a specific EventFilterCategory.
 *
 * Non-type-based filters (HighPriority, Overdue) are applied server-side.
 * Type-based filters (Alerts, Emails, Documents, Invoices, Tasks) return null
 * because sprk_eventtype is a lookup field — client-side filtering in
 * ActivityFeed.applyClientFilter handles category matching using the
 * formatted display value from Xrm.WebApi.
 *
 * The base user filter is always AND'd on by the caller.
 */
export function buildEventCategoryFilter(category: EventFilterCategory): string | null {
  switch (category) {
    case EventFilterCategory.All:
      return null;

    case EventFilterCategory.HighPriority:
      return 'sprk_priorityscore gt 70';

    case EventFilterCategory.Overdue: {
      const today = new Date();
      today.setUTCHours(0, 0, 0, 0);
      const todayIso = today.toISOString();
      return `sprk_duedate lt ${todayIso} and statuscode eq 1`;
    }

    // Type-based categories — filtered client-side using lookup display name
    case EventFilterCategory.Alerts:
    case EventFilterCategory.Emails:
    case EventFilterCategory.Documents:
    case EventFilterCategory.Invoices:
    case EventFilterCategory.Tasks:
      return null;

    default:
      return null;
  }
}

/**
 * Build the full OData query string for getEventsFeed.
 *
 * Sort order: priorityscore desc, then modifiedon desc (Critical items first).
 * The userId filter ensures only events belonging to the current user are returned.
 */
export function buildEventsFeedQuery(
  userId: string,
  category: EventFilterCategory = EventFilterCategory.All,
  top: number = 50
): string {
  const baseFilter = `_ownerid_value eq ${userId}`;
  const categoryFilter = buildEventCategoryFilter(category);
  const combinedFilter = categoryFilter
    ? `(${baseFilter}) and (${categoryFilter})`
    : baseFilter;

  return buildQuery({
    select: EVENT_SELECT_FIELDS,
    filter: combinedFilter,
    orderby: 'sprk_priorityscore desc,modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// Project query helpers
// ---------------------------------------------------------------------------

/** $select fields for sprk_project used in My Portfolio */
export const PROJECT_SELECT_FIELDS: string[] = [
  'sprk_projectid',
  'sprk_name',
  '_sprk_projecttype_ref_value',  // Lookup → sprk_projecttype_ref (display via formatted value)
  '_sprk_practicearea_value',  // Lookup → sprk_practicearea_ref (display via formatted value)
  '_sprk_owner_value',
  'sprk_status',
  'sprk_budgetused',
  'createdon',
  'modifiedon',
];

/**
 * Build the full OData query string for getProjectsByUser.
 * Default $top is 5 (My Portfolio widget).
 */
export function buildProjectsQuery(userId: string, top: number = 5): string {
  return buildQuery({
    select: PROJECT_SELECT_FIELDS,
    filter: `_ownerid_value eq ${userId}`,
    orderby: 'modifiedon desc',
    top,
  });
}

/** $select fields for sprk_project used in the Projects tab */
export const PROJECT_TAB_SELECT_FIELDS: string[] = [
  'sprk_projectid',
  'sprk_projectnumber',
  'sprk_projectname',
  'sprk_projectdescription',
  'statuscode',
  '_sprk_projecttype_ref_value',  // Lookup → sprk_projecttype_ref (display via formatted value)
  '_ownerid_value',
  '_modifiedby_value',
  '_sprk_assignedattorney1_value',
  '_sprk_assignedparalegal1_value',
  'createdon',
  'modifiedon',
];

/**
 * Build the full OData query string for the Projects tab.
 * Uses the broad owner filter (owner/modifier/attorney/paralegal).
 */
export function buildProjectsTabQuery(userId: string, contactId: string | null, top: number = 50): string {
  return buildQuery({
    select: PROJECT_TAB_SELECT_FIELDS,
    filter: buildBroadOwnerFilter(userId, contactId),
    orderby: 'modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// Invoice query helpers
// ---------------------------------------------------------------------------

/** $select fields for sprk_invoice used in the Invoices tab */
export const INVOICE_SELECT_FIELDS: string[] = [
  'sprk_invoiceid',
  'sprk_invoicenumber',
  'sprk_name',
  'sprk_invoicedate',
  'sprk_vendororg',
  'sprk_description',
  'statuscode',
  '_ownerid_value',
  '_modifiedby_value',
  '_sprk_assignedattorney1_value',
  '_sprk_assignedparalegal1_value',
  'createdon',
  'modifiedon',
];

/**
 * Build the full OData query string for the Invoices tab.
 * Uses the broad owner filter (owner/modifier/attorney/paralegal).
 */
export function buildInvoicesQuery(userId: string, contactId: string | null, top: number = 50): string {
  return buildQuery({
    select: INVOICE_SELECT_FIELDS,
    filter: buildBroadOwnerFilter(userId, contactId),
    orderby: 'sprk_invoicedate desc,modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// Document query helpers
// ---------------------------------------------------------------------------

/** $select fields for sprk_document used in My Portfolio */
export const DOCUMENT_SELECT_FIELDS: string[] = [
  'sprk_documentid',
  'sprk_documentname',
  'sprk_documenttype',  // Choice field (display via formatted value)
  'sprk_description',
  '_sprk_matter_value',
  'modifiedon',
];

/**
 * Build the full OData query string for getDocumentsByMatter.
 * Default $top is 5 (My Portfolio widget).
 */
export function buildDocumentsByMatterQuery(matterId: string, top: number = 5): string {
  return buildQuery({
    select: DOCUMENT_SELECT_FIELDS,
    filter: `_sprk_matter_value eq ${matterId}`,
    orderby: 'modifiedon desc',
    top,
  });
}

/**
 * Build the full OData query string for documents owned by a user (Documents tab).
 * Joins via matter ownership rather than direct document ownership.
 */
export function buildDocumentsByUserQuery(userId: string, top: number = 5): string {
  // Documents don't have a direct ownerid — filter via matter lookup's owner
  // We fall back to filtering by the user's matters using an expand or
  // a simple filter on _ownerid_value if that field exists on sprk_document.
  // Dataverse typically propagates ownerid to child records — use it here.
  return buildQuery({
    select: DOCUMENT_SELECT_FIELDS,
    filter: `_ownerid_value eq ${userId}`,
    orderby: 'modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// Documents tab query helpers (Activity section — broader filter)
// ---------------------------------------------------------------------------

/** $select fields for sprk_document used in the Documents tab */
export const DOCUMENT_TAB_SELECT_FIELDS: string[] = [
  'sprk_documentid',
  'sprk_documentname',
  'sprk_documentdescription',
  'sprk_documenttype',             // Choice field (display via formatted value)
  'sprk_filetype',
  'sprk_workspaceflag',
  'sprk_filesummary',
  'sprk_filetldr',
  '_sprk_checkedoutby_value',      // Contact lookup
  'statuscode',
  '_ownerid_value',
  '_createdby_value',
  '_modifiedby_value',
  '_sprk_matter_value',
  'createdon',
  'modifiedon',
];

/**
 * Build the OData $filter predicate for the Documents tab.
 *
 * Returns documents where the user is:
 *   - Owner
 *   - Creator (within last 30 days)
 *   - Modifier (within last 30 days)
 *   - Has workspace flag set to true
 *   - Checked out by the user (statuscode = 421500001)
 */
export function buildDocumentsTabFilter(userId: string): string {
  const thirtyDaysAgo = new Date(Date.now() - 30 * 86400000).toISOString();

  const clauses = [
    `_ownerid_value eq ${userId}`,
    `(_createdby_value eq ${userId} and createdon ge ${thirtyDaysAgo})`,
    `(_modifiedby_value eq ${userId} and modifiedon ge ${thirtyDaysAgo})`,
    `sprk_workspaceflag eq true`,
    `(statuscode eq 421500001 and _sprk_checkedoutby_value eq ${userId})`,
  ];

  return clauses.join(' or ');
}

/**
 * Build the full OData query string for the Documents tab.
 * Uses the complex filter (owner/creator/modifier/workspace/checked-out).
 */
export function buildDocumentsTabQuery(userId: string, top: number = 50): string {
  return buildQuery({
    select: DOCUMENT_TAB_SELECT_FIELDS,
    filter: buildDocumentsTabFilter(userId),
    orderby: 'modifiedon desc',
    top,
  });
}

// ---------------------------------------------------------------------------
// To-Do query helpers (sprk_todo — first-class entity per R3 FR-09 / FR-11)
// ---------------------------------------------------------------------------

/**
 * $select fields when querying to-do items from `sprk_todo`.
 *
 * Replaces the legacy `sprk_event`-based select that filtered on `sprk_todoflag`.
 * Per R3 FR-29 / OS-1, the legacy event-todo fields are removed; the native
 * `sprk_todo` entity carries `sprk_todocolumn`, `sprk_todopinned`,
 * `sprk_priorityscore`, `sprk_effortscore` as first-class fields.
 *
 * statuscode is the canonical lifecycle field (task 009):
 *   1 = Open, 659490001 = In Progress, 2 = Completed, 659490002 = Dismissed.
 */
export const TODO_SELECT_FIELDS: string[] = [
  'sprk_todoid',
  'sprk_name',
  'sprk_description',
  'sprk_notes',
  // task 021 (FR-06) — sprk_priority is now filterable via the Filter pane's
  // Priority category; selecting it here also closes task 012's documented
  // follow-up gap (KanbanCard reads `sprk_priority` via a structural cast,
  // but this select list never fetched it, so the glyph never had real data).
  'sprk_priority',
  'sprk_priorityscore',
  'sprk_effortscore',
  'sprk_duedate',
  'sprk_completedon',
  'sprk_todocolumn',
  'sprk_todopinned',
  'statecode',
  'statuscode',
  '_sprk_assignedto_value',  // Lookup → systemuser (display name via formatted value)
  '_ownerid_value',
  'createdon',
  'modifiedon',
  // DEF-11 Part 3 (2026-07-04, record-header-and-notepad-r1) — regarding-record
  // resolver text fields (ADR-024) so the Kanban Filter can match against parent
  // display name (e.g., "Smith v Jones") AND record number (e.g., "MAT-2026-01234")
  // in the substring filter. Both fields are populated on sprk_todo by the
  // PolymorphicResolverService when the todo is created regarding a parent.
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
];

/**
 * Optional parent-record filter for the R4 FR-34 openTodos launch flow.
 * `entityType` is the parent's Dataverse logical name (e.g., `sprk_matter`);
 * `recordId` is the parent GUID. When present, `buildTodoItemsQuery` scopes
 * results to `sprk_todo` rows whose corresponding entity-specific regarding
 * lookup equals the given GUID (e.g., `_sprk_regardingmatter_value eq <guid>`).
 *
 * Set by `SmartToDo.tsx` from `useLaunchContext()` when action=openTodos.
 * Added 2026-07-04 (record-header-and-notepad-r1 DEF-11 Part 2 — finishes the
 * R4 FR-34 consumer side that `useLaunchContext.ts` parses but nothing wired).
 */
export interface ITodoRegardingFilter {
  entityType: string;
  recordId: string;
}

/**
 * Map parent entity logical name → sprk_todo's entity-specific regarding
 * lookup attribute (ADR-024 dual-field pattern). Mirrors the same map in
 * `@spaarke/ui-components/hooks/toolbarLaunchDefaults.ts SUPPORTED_TODO_PARENTS`
 * — the two lists MUST stay in sync (both derived from the sprk_todo schema).
 */
const TODO_REGARDING_LOOKUP_BY_ENTITY: Record<string, string> = {
  sprk_matter: 'sprk_regardingmatter',
  sprk_project: 'sprk_regardingproject',
  sprk_event: 'sprk_regardingevent',
  sprk_invoice: 'sprk_regardinginvoice',
  sprk_budget: 'sprk_regardingbudget',
  sprk_workassignment: 'sprk_regardingworkassignment',
};

// ---------------------------------------------------------------------------
// Filter pane types + helpers (task 021, FR-06 / F-3)
// ---------------------------------------------------------------------------

/**
 * Status values selectable in the Filter pane's Status category (multi-select).
 * Maps 1:1 onto `sprk_todo.statuscode` (Open=1, InProgress=659490001,
 * Completed=2 — see the statuscode legend above `TODO_SELECT_FIELDS`).
 * Dismissed (659490002) is intentionally NOT selectable here — that lane is
 * exclusively `buildDismissedTodoQuery`'s collapsible section, untouched by
 * this filter category.
 */
export type TodoStatusFilterValue = 'Open' | 'InProgress' | 'Completed';

/** Maps a `TodoStatusFilterValue` to its raw `sprk_todo.statuscode` integer. */
export const TODO_STATUS_FILTER_STATUSCODE: Record<TodoStatusFilterValue, number> = {
  Open: 1,
  InProgress: 659490001,
  Completed: 2,
};

/**
 * Named due-date range buckets for the Filter pane's Due-date category
 * (single-select). Ranges are computed relative to "today" (UTC midnight) at
 * query-build time — see `buildDueDateRangeClause`.
 */
export type TodoDueDateCategory = 'Today' | 'Tomorrow' | 'ThisWeek' | 'Overdue';

/**
 * `sprk_todo.sprk_priority` Choice values (task 010 / FR-02): Urgent=100000000,
 * High=100000001, Medium=100000002, Low=100000003. Exported so `FilterPane.tsx`
 * can build its `TagFilter` option list without duplicating the raw integers.
 */
export const TODO_PRIORITY_CHOICE_VALUES: ReadonlyArray<{ value: number; label: string }> = [
  { value: 100000000, label: 'Urgent' },
  { value: 100000001, label: 'High' },
  { value: 100000002, label: 'Medium' },
  { value: 100000003, label: 'Low' },
];

/**
 * Filter-pane predicate (task 021, FR-06 / F-3). Fully controlled, owned by
 * `SmartTodoApp.tsx`, threaded through `useTodoItems` → `DataverseService
 * .getActiveTodos` → `buildTodoItemsQuery` (below).
 *
 * `assignedToContactId`, when set, OVERRIDES the default "assigned to me"
 * ownership clause (`_sprk_assignedto_value eq <contactId>`) with the chosen
 * contact instead — the Assigned-To category RE-SCOPES ownership rather than
 * adding an extra AND clause on top of it.
 */
export interface ITodoFilterState {
  /** `sprk_priority` choice values to include. Empty array = unfiltered (all priorities). */
  priorityValues: number[];
  /** Status values to include (multi-select). Empty array matches nothing — the UI (Clear-all) never leaves this empty for more than a keystroke. */
  statusValues: TodoStatusFilterValue[];
  /** Single-select due-date bucket. `undefined` = unfiltered (all due dates, including none set). */
  dueDateCategory?: TodoDueDateCategory;
  /** Contact GUID to scope results to. `undefined` = default "assigned to me" scope. */
  assignedToContactId?: string;
}

/**
 * Default filter state on first load (FR-06 acceptance): Status =
 * {Open, In Progress}; everything else unfiltered. "Clear all" resets to
 * exactly this value (not an all-clear/empty state).
 */
export const DEFAULT_TODO_FILTER: ITodoFilterState = {
  priorityValues: [],
  statusValues: ['Open', 'InProgress'],
  dueDateCategory: undefined,
  assignedToContactId: undefined,
};

/**
 * Build the OData $filter clause for a single Due-date category bucket.
 * Mirrors the UTC-midnight-truncation pattern already used by
 * `buildEventCategoryFilter`'s Overdue case (above) — reused as the model
 * per task 021 step 4.
 *
 *   Today    — [todayStart, todayStart + 1d)
 *   Tomorrow — [todayStart + 1d, todayStart + 2d)
 *   ThisWeek — [todayStart, todayStart + 7d)  (rolling 7-day window from today)
 *   Overdue  — sprk_duedate < todayStart
 */
function buildDueDateRangeClause(category: TodoDueDateCategory): string {
  const ONE_DAY_MS = 86400000;
  const today = new Date();
  today.setUTCHours(0, 0, 0, 0);
  const todayStart = today.getTime();

  switch (category) {
    case 'Today': {
      const start = new Date(todayStart).toISOString();
      const end = new Date(todayStart + ONE_DAY_MS).toISOString();
      return `(sprk_duedate ge ${start} and sprk_duedate lt ${end})`;
    }
    case 'Tomorrow': {
      const start = new Date(todayStart + ONE_DAY_MS).toISOString();
      const end = new Date(todayStart + 2 * ONE_DAY_MS).toISOString();
      return `(sprk_duedate ge ${start} and sprk_duedate lt ${end})`;
    }
    case 'ThisWeek': {
      const start = new Date(todayStart).toISOString();
      const end = new Date(todayStart + 7 * ONE_DAY_MS).toISOString();
      return `(sprk_duedate ge ${start} and sprk_duedate lt ${end})`;
    }
    case 'Overdue': {
      const start = new Date(todayStart).toISOString();
      return `sprk_duedate lt ${start}`;
    }
    default:
      return '';
  }
}

/**
 * Build the OData query for active to-do items (Kanban-visible).
 *
 * Returns `sprk_todo` records where:
 *   - statecode = 0 (Active) and statuscode in (Open, In Progress) — the
 *     always-included baseline
 *   - PLUS, when `includeCompleted` is true (FR-07 / F-2 Show-Completed
 *     toggle, smart-todo-r5 task 022): statuscode = 2 (Completed) items are
 *     OR'd in as well. Dismissed (659490002) items are NEVER included here —
 *     that lane is exclusively `buildDismissedTodoQuery` below and is
 *     untouched by this parameter.
 *   - assignee = current user's CONTACT ("Assigned to Me" — R4 task 031 / FR-07 / OD-2)
 *   - IF regardingFilter is provided (R4 FR-34 openTodos launch — record-header-
 *     and-notepad-r1 DEF-11 Part 2): AND `_sprk_regarding<X>_value eq <guid>`.
 *     Kanban ownership scope is preserved — this scopes "MY todos" to a specific
 *     parent record (e.g., "MY todos for THIS matter"), which matches the drill-
 *     through UX intent. If entityType isn't in the supported map, the filter is
 *     silently ignored (safe fallback — matches unsupported handling in the
 *     memo repository).
 *   - IF `filterState` is provided (task 021, FR-06 / F-3 filter pane): it
 *     TAKES OVER statuscode inclusion (superseding `includeCompleted`
 *     entirely — `statusValues` is the fuller multi-select generalization of
 *     that boolean), AND adds `sprk_priority` inclusion, a due-date range
 *     clause, and an Assigned-To ownership override. When `filterState` is
 *     omitted, behavior is 100% unchanged from before this task (verified by
 *     `queryHelpers.test.ts`'s backward-compat case) — every pre-existing
 *     caller (and `includeCompleted`) keeps working exactly as before.
 *
 * Sort: priorityscore desc, then duedate asc (most urgent first). This is
 * unchanged when Completed items are included — they sort into the same
 * order and are then bucketed by `useKanbanColumns`'s score/due-date logic
 * exactly like an Open/In-Progress item (no special-case exclusion exists
 * in the bucketing hook — see `useKanbanColumns.ts`).
 *
 * UAT 2026-06-19: `sprk_assignedto` migrated from systemuser → sprk_contact
 * lookup. Caller must resolve the current systemuser → sprk_contact (via
 * useCurrentContactId) and pass that contactId here. Passing a systemuser
 * GUID would return zero matches.
 *
 * Per FR-11: zero queries to `sprk_event` from the kanban path.
 * Per OS-1: no `sprk_todoflag` filter — that field no longer exists on `sprk_event`.
 *
 * @param contactId - GUID of the current user's sprk_contact record
 * @param regardingFilter - Optional parent-record filter (openTodos launch)
 * @param includeCompleted - FR-07 Show-Completed toggle state (legacy path
 *   only — ignored when `filterState` is provided). Defaults to `false`.
 * @param filterState - Full Filter-pane predicate (task 021). When provided,
 *   it is authoritative for status/priority/due-date/assignee — see above.
 */
export function buildTodoItemsQuery(
  contactId: string,
  regardingFilter?: ITodoRegardingFilter,
  includeCompleted: boolean = false,
  filterState?: ITodoFilterState,
): string {
  const openInProgressClause = `statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)`;

  let ownershipClause: string;
  let activeClause: string;
  let priorityClause: string | null = null;
  let dueDateClause: string | null = null;

  if (filterState) {
    // task 021 (FR-06) — the Filter pane's Status multi-select fully
    // determines statuscode inclusion. Empty selection defensively matches
    // nothing (statuscode eq -1 never matches a real record) rather than
    // silently falling back to "all statuses" — the UI (Clear-all) always
    // restores a non-empty default, so this is a belt-and-braces guard, not
    // a reachable steady state.
    const selectedCodes = filterState.statusValues
      .map((v) => TODO_STATUS_FILTER_STATUSCODE[v])
      .filter((code): code is number => code !== undefined);
    activeClause =
      selectedCodes.length === 0
        ? 'statuscode eq -1'
        : selectedCodes.length === 1
          ? `statuscode eq ${selectedCodes[0]}`
          : `(${selectedCodes.map((c) => `statuscode eq ${c}`).join(' or ')})`;

    if (filterState.priorityValues.length > 0) {
      priorityClause =
        filterState.priorityValues.length === 1
          ? `sprk_priority eq ${filterState.priorityValues[0]}`
          : `(${filterState.priorityValues.map((v) => `sprk_priority eq ${v}`).join(' or ')})`;
    }

    if (filterState.dueDateCategory) {
      dueDateClause = buildDueDateRangeClause(filterState.dueDateCategory);
    }

    // Assigned-To override — RE-SCOPES ownership rather than AND-ing on top
    // of the default "assigned to me" clause.
    ownershipClause = `_sprk_assignedto_value eq ${filterState.assignedToContactId ?? contactId}`;
  } else {
    // Legacy path — byte-identical to pre-task-021 behavior (task 022's
    // includeCompleted boolean still works for any caller that doesn't pass
    // filterState).
    ownershipClause = `_sprk_assignedto_value eq ${contactId}`;
    activeClause = includeCompleted
      ? `(${openInProgressClause} or statuscode eq 2)`
      : openInProgressClause;
  }

  const clauses: string[] = [ownershipClause, activeClause];
  if (priorityClause) clauses.push(priorityClause);
  if (dueDateClause) clauses.push(dueDateClause);

  if (regardingFilter) {
    const lookup = TODO_REGARDING_LOOKUP_BY_ENTITY[regardingFilter.entityType];
    if (lookup) {
      clauses.push(`_${lookup}_value eq ${regardingFilter.recordId}`);
    }
    // else: unsupported entityType — silently skip the filter (safe fallback).
  }

  const filter = clauses.join(' and ');

  return buildQuery({
    select: TODO_SELECT_FIELDS,
    filter,
    orderby: 'sprk_priorityscore desc,sprk_duedate asc',
  });
}

/**
 * Build the OData query for dismissed to-do items (collapsible section).
 *
 * Returns `sprk_todo` records where statuscode = 659490002 (Dismissed).
 */
export function buildDismissedTodoQuery(userId: string): string {
  const filter =
    `_ownerid_value eq ${userId}` +
    ` and statuscode eq 659490002`;
  return buildQuery({
    select: TODO_SELECT_FIELDS,
    filter,
    orderby: 'modifiedon desc',
    top: 20,
  });
}
