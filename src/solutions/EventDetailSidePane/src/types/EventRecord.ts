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
  /** State (Active 0 / Inactive 1) — paired with statuscode (see EventStatus). */
  statecode?: number;
  /**
   * Status reason — THE event status (D-28, task 066; the second status column is deprecated and no longer read).
   * Values: see EventStatus.
   */
  statuscode?: number;
  /** Priority */
  sprk_priority?: number;
  /** Source */
  sprk_source?: string;

  // ─────────────────────────────────────────────────────────────────────────
  // Lookup fields (Event Type)
  // ─────────────────────────────────────────────────────────────────────────

  /** Event Type lookup (GUID) */
  _sprk_eventtype_ref_value?: string;
  /** Event Type formatted name */
  "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"?: string;

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
 * Event Status = sprk_event.statuscode values (the LIVE option set; Spaarke.Dataverse.EventStatusCode is the
 * server-side home, pinned by the BFF test EventStatusDeprecationTests).
 */
export enum EventStatus {
  Draft = 1,
  Open = 659490001,
  Completed = 659490002,
  Closed = 659490003,
  OnHold = 659490006,
  Reassigned = 659490007,
  NoFurtherAction = 2,
  Cancelled = 659490004,
  Transferred = 659490005,
}

/**
 * @deprecated Use EventStatus instead
 */
export const EventStatusReason = EventStatus;

/**
 * Event Status labels for display
 */
export const EVENT_STATUS_LABELS: Record<number, string> = {
  [EventStatus.Draft]: "Draft",
  [EventStatus.Open]: "Open",
  [EventStatus.Completed]: "Completed",
  [EventStatus.Closed]: "Closed",
  [EventStatus.OnHold]: "On Hold",
  [EventStatus.Reassigned]: "Reassigned",
  [EventStatus.NoFurtherAction]: "No Further Action",
  [EventStatus.Cancelled]: "Cancelled",
  [EventStatus.Transferred]: "Transferred",
};

/**
 * Open-work statuses that allow actions (Complete, Cancel, etc.) — the same set as the BFF's
 * EventStatusCode.IsOpenWork (Draft, Open, On Hold, Reassigned).
 */
export const ACTIVE_EVENT_STATUSES = [
  EventStatus.Draft,
  EventStatus.Open,
  EventStatus.OnHold,
  EventStatus.Reassigned,
];

/**
 * Terminal statuses (event is finished)
 */
export const TERMINAL_EVENT_STATUSES = [
  EventStatus.Completed,
  EventStatus.Closed,
  EventStatus.Cancelled,
  EventStatus.Transferred,
  EventStatus.NoFurtherAction,
];

/**
 * Get status label for an event status value
 */
export function getStatusLabel(status: number): string {
  return EVENT_STATUS_LABELS[status] ?? "Unknown";
}

/**
 * Check if an event is in an active/actionable state
 */
export function isEventActive(status: number): boolean {
  return ACTIVE_EVENT_STATUSES.includes(status);
}

/**
 * Fields to select when loading Event record for side pane header
 */
export const EVENT_HEADER_SELECT_FIELDS = [
  "sprk_eventid",
  "sprk_eventname",
  "statecode", // Keep for backward compatibility / archive detection
  "statuscode", // OOB status reason — used by StatusSection
  "_sprk_eventtype_ref_value",
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
  "statecode", // Keep for backward compatibility / archive detection
  "statuscode", // OOB status reason — used by StatusSection
  "sprk_priority",
  "sprk_source",
  "_sprk_eventtype_ref_value",
  "_sprk_regardingrecordtype_value",
  "sprk_regardingrecordname",
  "sprk_regardingrecordid",
  "sprk_regardingrecordurl",
  "_sprk_relatedevent_value",
  "_ownerid_value",
].join(",");
