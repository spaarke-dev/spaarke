import type { FluentIcon } from "@fluentui/react-icons";
import {
  GavelRegular,
  TaskListSquareLtrRegular,
  PersonAddRegular,
  CheckboxCheckedRegular,
} from "@fluentui/react-icons";

/** Badge type for notification indicators on metric cards. */
export type BadgeType = "new" | "overdue";

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
  countFilter: (userId: string) => string;
  /** Badge type to display when badge count > 0. */
  badgeType?: BadgeType;
  /** Builds an OData $filter for the badge count query. Returns null if no badge. */
  badgeFilter?: (userId: string) => string;
}

/**
 * Ordered list of 4 metric cards for the Quick Summary row.
 *
 * Each card fires a count query via useQuickSummaryCounts and opens
 * the corresponding system view via navigateToEntityList on click.
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
    countFilter: (userId) => `_ownerid_value eq ${userId} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (userId) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `_ownerid_value eq ${userId} and statecode eq 0 and createdon ge ${d.toISOString()}`;
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
    countFilter: (userId) => `_ownerid_value eq ${userId} and statecode eq 0`,
    badgeType: "new",
    badgeFilter: (userId) => {
      const d = new Date();
      d.setDate(d.getDate() - 7);
      return `_ownerid_value eq ${userId} and statecode eq 0 and createdon ge ${d.toISOString()}`;
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
    countFilter: (userId) => `_ownerid_value eq ${userId} and statecode eq 0`,
    badgeType: "overdue",
    badgeFilter: (userId) => {
      const now = new Date().toISOString();
      return `_ownerid_value eq ${userId} and statecode eq 0 and sprk_duedate lt ${now}`;
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
    countFilter: (userId) =>
      `_ownerid_value eq ${userId} and sprk_todoflag eq true and sprk_todostatus ne 100000002`,
    badgeType: "overdue",
    badgeFilter: (userId) => {
      const now = new Date().toISOString();
      return `_ownerid_value eq ${userId} and sprk_todoflag eq true and sprk_todostatus ne 100000002 and sprk_duedate lt ${now}`;
    },
  },
];
