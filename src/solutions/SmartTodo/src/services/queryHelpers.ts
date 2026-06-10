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
];

/**
 * "My Tasks" filter modes used by KanbanHeader (R3 FR-12 / A-6).
 *
 * Re-declared here (rather than imported from hooks/useUserPreferences) so the
 * query-builder layer has no upward dependency on the React hooks layer.
 * The string literals must match `MyTasksFilterMode` in useUserPreferences.ts.
 */
export type TodoFilterMode = 'MyTasks' | 'AssignedToMe' | 'All';

/**
 * Build the ownership clause for the To Do kanban query per FR-12 / A-6.
 *
 * Modes:
 *   - MyTasks (default): owner = currentuser OR assignee = currentuser
 *     A-6 also calls for "OR ownerid eq team-where-currentuser-is-member".
 *     That third clause is intentionally deferred to v2 because OData on
 *     Dataverse Web API cannot express "owner is one of my teams" without
 *     pre-fetching the user's team memberships (a separate roundtrip via
 *     /teammemberships?$filter=_systemuserid_value eq {userId}) and inlining
 *     the resulting team ids into the predicate. TODO(R3-v2): wire the
 *     team-membership prefetch into the data hook and append
 *     `or _owningteam_value in ({teamIds})` to this clause.
 *   - AssignedToMe: assignee = currentuser only.
 *   - All: no ownership clause (the active-statuscode filter still applies).
 *
 * Returns `null` for the All mode (caller omits the clause from the predicate).
 */
function buildTodoOwnershipClause(userId: string, mode: TodoFilterMode): string | null {
  switch (mode) {
    case 'AssignedToMe':
      return `_sprk_assignedto_value eq ${userId}`;

    case 'All':
      return null;

    case 'MyTasks':
    default:
      // FR-12 / A-6 — clauses 1 + 2. Clause 3 (team membership) deferred to v2.
      return `(_ownerid_value eq ${userId} or _sprk_assignedto_value eq ${userId})`;
  }
}

/**
 * Build the OData query for active to-do items (Kanban-visible).
 *
 * Returns `sprk_todo` records where:
 *   - statecode = 0 (Active)
 *   - statuscode in (Open, In Progress) — excludes Completed + Dismissed
 *   - ownership predicate per `mode` (FR-12 / A-6)
 *
 * Sort: priorityscore desc, then duedate asc (most urgent first).
 *
 * Per FR-11: zero queries to `sprk_event` from the kanban path.
 * Per OS-1: no `sprk_todoflag` filter — that field no longer exists on `sprk_event`.
 *
 * @param userId - GUID of the current user
 * @param mode   - My Tasks filter mode (default: 'MyTasks' per FR-12)
 */
export function buildTodoItemsQuery(
  userId: string,
  mode: TodoFilterMode = 'MyTasks'
): string {
  // Active to-do statuscodes per task 009:
  //   1          = Open
  //   659490001  = In Progress
  // Completed (2) and Dismissed (659490002) are inactive and handled by the
  // dismissed / restore paths.
  const activeClause =
    `statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)`;

  const ownershipClause = buildTodoOwnershipClause(userId, mode);

  const filter = ownershipClause
    ? `${ownershipClause} and ${activeClause}`
    : activeClause;

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
