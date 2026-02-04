/**
 * Event Record Type Definitions
 *
 * Type definitions for Event records loaded from Dataverse WebAPI.
 * Used by EventDetailSidePane components for type safety.
 *
 * @see design.md - Event Data Model Reference
 */

/**
 * Event record fields from Dataverse sprk_event entity
 */
export interface IEventRecord {
  /** Primary key (GUID) */
  sprk_eventid: string;
  /** Event name */
  sprk_eventname: string;
  /** Event description (multiline) */
  sprk_description?: string;
  /** Due date */
  sprk_duedate?: string;
  /** Base date */
  sprk_basedate?: string;
  /** Final due date */
  sprk_finalduedate?: string;
  /** Completed date */
  sprk_completeddate?: string;
  /** Scheduled start */
  scheduledstart?: string;
  /** Scheduled end */
  scheduledend?: string;
  /** Location */
  sprk_location?: string;
  /** Reminder datetime */
  sprk_remindat?: string;
  /** Status (0 = Active, 1 = Inactive) */
  statecode: number;
  /** Status Reason (1=Draft, 2=Planned, 3=Open, 4=On Hold, 5=Completed, 6=Cancelled) */
  statuscode: number;
  /** Priority */
  sprk_priority?: number;
  /** Source */
  sprk_source?: string;

  // ─────────────────────────────────────────────────────────────────────────
  // Lookup fields (Event Type)
  // ─────────────────────────────────────────────────────────────────────────

  /** Event Type lookup (GUID) */
  _sprk_eventtype_value?: string;
  /** Event Type formatted name */
  "_sprk_eventtype_value@OData.Community.Display.V1.FormattedValue"?: string;

  // ─────────────────────────────────────────────────────────────────────────
  // Regarding fields (Parent Record)
  // ─────────────────────────────────────────────────────────────────────────

  /** Regarding record type lookup */
  _sprk_regardingrecordtype_value?: string;
  /** Regarding record type formatted name */
  "_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"?: string;
  /** Denormalized parent record name */
  sprk_regardingrecordname?: string;
  /** Denormalized parent record ID */
  sprk_regardingrecordid?: string;
  /** Denormalized parent record URL (for navigation) */
  sprk_regardingrecordurl?: string;

  // ─────────────────────────────────────────────────────────────────────────
  // Related Event fields
  // ─────────────────────────────────────────────────────────────────────────

  /** Related Event lookup (GUID) */
  _sprk_relatedevent_value?: string;
  /** Related Event formatted name */
  "_sprk_relatedevent_value@OData.Community.Display.V1.FormattedValue"?: string;

  // ─────────────────────────────────────────────────────────────────────────
  // Owner field
  // ─────────────────────────────────────────────────────────────────────────

  /** Owner lookup (GUID) */
  _ownerid_value?: string;
  /** Owner formatted name */
  "_ownerid_value@OData.Community.Display.V1.FormattedValue"?: string;
}

/**
 * Status reason enum values
 */
export enum EventStatusReason {
  Draft = 1,
  Planned = 2,
  Open = 3,
  OnHold = 4,
  Completed = 5,
  Cancelled = 6,
}

/**
 * Status reason labels for display
 */
export const EVENT_STATUS_LABELS: Record<number, string> = {
  [EventStatusReason.Draft]: "Draft",
  [EventStatusReason.Planned]: "Planned",
  [EventStatusReason.Open]: "Open",
  [EventStatusReason.OnHold]: "On Hold",
  [EventStatusReason.Completed]: "Completed",
  [EventStatusReason.Cancelled]: "Cancelled",
};

/**
 * Get status label for a status code
 */
export function getStatusLabel(statusCode: number): string {
  return EVENT_STATUS_LABELS[statusCode] ?? "Unknown";
}

/**
 * Fields to select when loading Event record for side pane header
 */
export const EVENT_HEADER_SELECT_FIELDS = [
  "sprk_eventid",
  "sprk_eventname",
  "statecode",
  "statuscode",
  "_sprk_eventtype_value",
  "sprk_regardingrecordname",
  "sprk_regardingrecordurl",
].join(",");

/**
 * Fields to select when loading full Event record for side pane
 */
export const EVENT_FULL_SELECT_FIELDS = [
  "sprk_eventid",
  "sprk_eventname",
  "sprk_description",
  "sprk_duedate",
  "sprk_basedate",
  "sprk_finalduedate",
  "sprk_completeddate",
  "scheduledstart",
  "scheduledend",
  "sprk_location",
  "sprk_remindat",
  "statecode",
  "statuscode",
  "sprk_priority",
  "sprk_source",
  "_sprk_eventtype_value",
  "_sprk_regardingrecordtype_value",
  "sprk_regardingrecordname",
  "sprk_regardingrecordid",
  "sprk_regardingrecordurl",
  "_sprk_relatedevent_value",
  "_ownerid_value",
].join(",");
