import type { FluentIcon } from "@fluentui/react-icons";
import {
  GavelRegular,
  TaskListSquareLtrRegular,
  PersonAddRegular,
  CheckboxCheckedRegular,
  MailRegular,
  ReceiptRegular,
} from "@fluentui/react-icons";

import { buildOwnerFilter } from "../../services/queryHelpers";
import type { IOwnershipContext } from "../../services/queryHelpers";

/** Badge type for notification indicators on metric cards. */
export type BadgeType = "new" | "overdue";

/** Re-export for consumers that import from this file. */
export type IFilterContext = IOwnershipContext;
export { buildOwnerFilter };

/** Configuration for a single Quick Summary metric card. */
export interface IQuickSummaryCardConfig {
  /** Stable identifier used as React key. */
  id: string;
  /** Display title shown below the count. */
  title: string;
  /** Fluent v9 icon component (FluentIcon). */
  icon: FluentIcon;
  /** Aria-label for the card button. */
  ariaLabel: string;
  /** Dataverse entity logical name. */
  entityName: string;
  /** Primary key field for the entity. */
  primaryKey: string;
  /** GUID of the system view to open on click. */
  viewId: string;
  /** Builds an OData $filter string for the count query. */
  countFilter: (ctx: IFilterContext) => string;
  /** Badge type to display when badge count > 0. */
  badgeType?: BadgeType;
  /** Builds an OData $filter for the badge count query. Returns null if no badge. */
  badgeFilter?: (ctx: IFilterContext) => string;
}

/**
 * Ordered list of 6 metric cards for the Quick Summary row.
 *
 * Each card fires a count query via useQuickSummaryCounts and opens
 * the corresponding system view via navigateToEntityList on click.
 *
 * Cards 1-4 (My Matters, My Projects, Assign Work, Open Tasks) shipped
 * in Round 6. Cards 5-6 (Communications, Invoices) were added in Round 8
 * Wave 3 (task 110, 2026-05-22) per the operator's "My Work" system
 * workspace request — these reuse the QuickSummary section verbatim and
 * surface 6 cards in a 2x3 grid (see QuickSummaryRow grid CSS).
 */
export const QUICK_SUMMARY_CARDS: IQuickSummaryCardConfig[] = [
  {
    id: "my-matters",
    title: "My Matters",
    icon: GavelRegular,
    ariaLabel: "View my matters",
    entityName: "sprk_matter",
    primaryKey: "sprk_matterid",
    viewId: "6c3c5d88-2617-f111-8343-7c1e520aa4df",
    countFilter: (ctx) => `${buildOwnerFilter(ctx)} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (ctx) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `${buildOwnerFilter(ctx)} and statecode eq 0 and createdon ge ${d.toISOString()}`;
    },
  },
  {
    id: "my-projects",
    title: "My Projects",
    icon: TaskListSquareLtrRegular,
    ariaLabel: "View my projects",
    entityName: "sprk_project",
    primaryKey: "sprk_projectid",
    viewId: "0e36d0a4-2617-f111-8343-7ced8d1dc988",
    countFilter: (ctx) => `${buildOwnerFilter(ctx)} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (ctx) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `${buildOwnerFilter(ctx)} and statecode eq 0 and createdon ge ${d.toISOString()}`;
    },
  },
  {
    id: "assign-work",
    title: "Assign Work",
    icon: PersonAddRegular,
    ariaLabel: "View work assignments",
    entityName: "sprk_workassignment",
    primaryKey: "sprk_workassignmentid",
    viewId: "b7cf5593-2517-f111-8343-7ced8d1dc988",
    countFilter: (ctx) => `${buildOwnerFilter(ctx)} and statecode eq 0`,
    badgeType: "overdue",
    badgeFilter: (ctx) => {
      const now = new Date().toISOString();
      return `${buildOwnerFilter(ctx)} and statecode eq 0 and sprk_responseduedate lt ${now}`;
    },
  },
  {
    id: "open-tasks",
    title: "Open Tasks",
    icon: CheckboxCheckedRegular,
    ariaLabel: "View open tasks",
    entityName: "sprk_event",
    primaryKey: "sprk_eventid",
    viewId: "12a510e4-2517-f111-8343-7ced8d1dc988",
    countFilter: (ctx) =>
      `${buildOwnerFilter(ctx)} and sprk_todoflag eq true and sprk_todostatus ne 100000002`,
    badgeType: "overdue",
    badgeFilter: (ctx) => {
      const now = new Date().toISOString();
      return `${buildOwnerFilter(ctx)} and sprk_todoflag eq true and sprk_todostatus ne 100000002 and sprk_duedate lt ${now}`;
    },
  },
  {
    // Round 8 Wave 3 (task 110, 2026-05-22).
    // View: "Active Communications" (default system view, querytype=0).
    id: "communications",
    title: "Communications",
    icon: MailRegular,
    ariaLabel: "View communications",
    entityName: "sprk_communication",
    primaryKey: "sprk_communicationid",
    viewId: "2bf1c5a5-0eca-4f37-92df-2e3c386dee98",
    countFilter: (ctx) => `${buildOwnerFilter(ctx)} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (ctx) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `${buildOwnerFilter(ctx)} and statecode eq 0 and createdon ge ${d.toISOString()}`;
    },
  },
  {
    // Round 8 Wave 3 (task 110, 2026-05-22).
    // View: "Active Invoices" (default system view, isdefault=true).
    id: "invoices",
    title: "Invoices",
    icon: ReceiptRegular,
    ariaLabel: "View invoices",
    entityName: "sprk_invoice",
    primaryKey: "sprk_invoiceid",
    viewId: "2220a3bc-330e-4405-b441-3605df961878",
    countFilter: (ctx) => `${buildOwnerFilter(ctx)} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (ctx) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `${buildOwnerFilter(ctx)} and statecode eq 0 and createdon ge ${d.toISOString()}`;
    },
  },
];
