/** Matter health status derived from metrics */
export type MatterStatus = 'Critical' | 'Warning' | 'OnTrack';

/** Letter grades for matter scoring dimensions */
export type GradeLevel = 'A' | 'B' | 'C' | 'D' | 'F';

/** Priority scoring levels (matches Dataverse sprk_priority choice: 0=Low, 1=Normal, 2=High, 3=Urgent) */
export type PriorityLevel = 'Urgent' | 'High' | 'Normal' | 'Low';

/** Effort scoring levels */
export type EffortLevel = 'High' | 'Med' | 'Low';

/** To-Do item status */
export type TodoStatus = 'Open' | 'Completed' | 'Dismissed';

/** To-Do item source (matches Dataverse sprk_todosource choice: 100000000=System, 100000001=User, 100000002=AI) */
export type TodoSource = 'System' | 'User' | 'AI';

/** Kanban column assignment for To Do items */
export type TodoColumn = 'Today' | 'Tomorrow' | 'Future';

/** Event types matching Dataverse option set */
export type EventType =
  | 'Email'
  | 'DocumentReview'
  | 'Task'
  | 'Invoice'
  | 'Meeting'
  | 'Analysis'
  | 'AlertResponse';

/** Notification categories */
export type NotificationCategory = 'Documents' | 'Invoices' | 'Status' | 'Analysis';

/** Portfolio tab options */
export type PortfolioTab = 'matters' | 'projects' | 'documents';

/** Theme mode */
export type ThemeMode = 'light' | 'dark' | 'high-contrast';

/**
 * Event feed filter categories (Block 3 filter bar).
 *
 * Each value corresponds to a pill filter in the Updates Feed.
 * Maps to OData $filter predicates in queryHelpers.buildEventCategoryFilter().
 */
export enum EventFilterCategory {
  All = 'All',
  HighPriority = 'HighPriority',
  Overdue = 'Overdue',
  Alerts = 'Alerts',
  Emails = 'Emails',
  Documents = 'Documents',
  Invoices = 'Invoices',
  Tasks = 'Tasks',
}

/** Helper: Derive matter status from metrics */
export function deriveMatterStatus(
  overdueeventcount: number,
  utilizationpercent: number
): MatterStatus {
  if (overdueeventcount > 0 || utilizationpercent > 85) return 'Critical';
  if (utilizationpercent > 65) return 'Warning';
  return 'OnTrack';
}

/** Helper: Check if a grade is below C (D or F) */
export function isGradeBelowC(grade: string | undefined): boolean {
  if (!grade) return false;
  return grade === 'D' || grade === 'F';
}
