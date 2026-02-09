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
  /**
   * Event Status (custom field - primary status indicator)
   * Values: 0=Draft, 1=Open, 2=Completed, 3=Closed, 4=On Hold, 5=Cancelled, 6=Reassigned, 7=Archived
   */
  sprk_eventstatus?: number;
  /** @deprecated Use sprk_eventstatus instead. Kept for backward compatibility. */
  statecode?: number;
  /** @deprecated Use sprk_eventstatus instead. Kept for backward compatibility. */
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
 * Event Status values (sprk_eventstatus custom field)
 * Matches values defined in Dataverse optionset
 */
export enum EventStatus {
  Draft = 0,
  Open = 1,
  Completed = 2,
  Closed = 3,
  OnHold = 4,
  Cancelled = 5,
  Reassigned = 6,
  Archived = 7,
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
  [EventStatus.Cancelled]: "Cancelled",
  [EventStatus.Reassigned]: "Reassigned",
  [EventStatus.Archived]: "Archived",
};

/**
 * Active statuses that allow actions (Complete, Cancel, etc.)
 */
export const ACTIVE_EVENT_STATUSES = [
  EventStatus.Draft,
  EventStatus.Open,
  EventStatus.OnHold,
];

/**
 * Terminal statuses (event is finished)
 */
export const TERMINAL_EVENT_STATUSES = [
  EventStatus.Completed,
  EventStatus.Closed,
  EventStatus.Cancelled,
  EventStatus.Reassigned,
  EventStatus.Archived,
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
  "sprk_eventstatus",
  "statecode", // Keep for backward compatibility / archive detection
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
  "sprk_eventstatus",
  "statecode", // Keep for backward compatibility / archive detection
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
