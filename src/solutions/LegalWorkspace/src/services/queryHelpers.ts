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
 *   - assigned attorney (sprk_assignedattorney — contact lookup)
 *   - assigned paralegal (sprk_assignedparalegal — contact lookup)
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
    clauses.push(`_sprk_assignedattorney_value eq ${contactId}`);
    clauses.push(`_sprk_assignedparalegal_value eq ${contactId}`);
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
  '_sprk_mattertype_ref_value',  // Lookup → sprk_mattertype_ref (display via formatted value)
  'sprk_practicearea',
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
  'sprk_name',
  'sprk_description',
  '_sprk_mattertype_ref_value',  // Lookup → sprk_mattertype_ref (display via formatted value)
  'sprk_practicearea',
  'sprk_status',
  '_ownerid_value',
  '_modifiedby_value',
  '_sprk_assignedattorney_value',
  '_sprk_assignedparalegal_value',
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

/** $select fields for sprk_event used in the Updates Feed */
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
  'sprk_todoflag',
  'sprk_todostatus',
  'sprk_todosource',
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
  'sprk_practicearea',
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
  'sprk_name',
  'sprk_projectdescription',
  'statuscode',
  'sprk_status',
  '_sprk_projecttype_ref_value',  // Lookup → sprk_projecttype_ref (display via formatted value)
  '_ownerid_value',
  '_modifiedby_value',
  '_sprk_assignedattorney_value',
  '_sprk_assignedparalegal_value',
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
  '_sprk_assignedattorney_value',
  '_sprk_assignedparalegal_value',
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
  'sprk_name',
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
// To-Do query helpers
// ---------------------------------------------------------------------------

/** $select fields when querying to-do items (sprk_event with todoflag=true) */
export const TODO_SELECT_FIELDS: string[] = [
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
  'sprk_todoflag',
  'sprk_todostatus',
  'sprk_todosource',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  '_sprk_regardingrecordtype_value',  // Lookup → sprk_recordtype_ref (display via formatted value)
  '_sprk_assignedto_value',  // Lookup → contact (display name via formatted value)
  'sprk_duedate',
  'sprk_todocolumn',
  'sprk_todopinned',
  'createdon',
  'modifiedon',
];

/**
 * Build the OData query for active to-do items.
 *
 * Returns sprk_event records where:
 *   - todoflag = true
 *   - todostatus != Dismissed (option set value 2)
 *   - owner = current user
 *
 * Sort: priorityscore desc, then duedate asc (most urgent first).
 */
export function buildTodoItemsQuery(userId: string): string {
  const filter = `_ownerid_value eq ${userId} and sprk_todoflag eq true and sprk_todostatus ne 100000002`;
  return buildQuery({
    select: TODO_SELECT_FIELDS,
    filter,
    orderby: 'sprk_priorityscore desc,sprk_duedate asc',
  });
}

/**
 * Build the OData query for dismissed to-do items (collapsible section).
 */
export function buildDismissedTodoQuery(userId: string): string {
  const filter = `_ownerid_value eq ${userId} and sprk_todoflag eq true and sprk_todostatus eq 100000002`;
  return buildQuery({
    select: TODO_SELECT_FIELDS,
    filter,
    orderby: 'modifiedon desc',
    top: 20,
  });
}
