/**
 * Event Configuration
 *
 * Configuration for Event Type → Form GUID mapping and predefined Views.
 * Used by EventsPage for side pane navigation and ViewSelector dropdown.
 *
 * @see projects/events-workspace-apps-UX-r1/notes/Event-Form-GUID.md
 * @see projects/events-workspace-apps-UX-r1/notes/Events-View-GUIDS.md
 */

// ─────────────────────────────────────────────────────────────────────────────
// Event Type to Form GUID Mapping
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Mapping from Event Type GUID → Side Pane Form GUID.
 * Different event types use different side pane forms for optimal UX.
 */
export interface IEventTypeFormMapping {
  eventTypeGuid: string;
  eventTypeName: string;
  formGuid: string;
  formName: string;
}

/**
 * Complete mapping of Event Types to their Side Pane Forms
 */
export const EVENT_TYPE_FORM_MAPPINGS: IEventTypeFormMapping[] = [
  // Task-style events → Event Task side pane form
  {
    eventTypeGuid: "124f5fc9-98ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Task",
    formGuid: "c4d7c4ee-4502-f111-8406-7c1e525abd8b",
    formName: "Event Task side pane form",
  },
  {
    eventTypeGuid: "e0043c4b-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Deadline",
    formGuid: "c4d7c4ee-4502-f111-8406-7c1e525abd8b",
    formName: "Event Task side pane form",
  },
  {
    eventTypeGuid: "da6bc005-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Reminder",
    formGuid: "c4d7c4ee-4502-f111-8406-7c1e525abd8b",
    formName: "Event Task side pane form",
  },
  // Action-style events → Event Action side pane form
  {
    eventTypeGuid: "b86d712b-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Milestone",
    formGuid: "c4c2a2ba-c302-f111-8407-7c1e520aa4df",
    formName: "Event Action side pane form",
  },
  {
    eventTypeGuid: "5a1c56c3-98ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Action",
    formGuid: "c4c2a2ba-c302-f111-8407-7c1e520aa4df",
    formName: "Event Action side pane form",
  },
  {
    eventTypeGuid: "ebfb62ba-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Filing",
    formGuid: "c4c2a2ba-c302-f111-8407-7c1e520aa4df",
    formName: "Event Action side pane form",
  },
  {
    eventTypeGuid: "748b1a64-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Notification",
    formGuid: "c4c2a2ba-c302-f111-8407-7c1e520aa4df",
    formName: "Event Action side pane form",
  },
  {
    eventTypeGuid: "1f06537c-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Status Change",
    formGuid: "c4c2a2ba-c302-f111-8407-7c1e520aa4df",
    formName: "Event Action side pane form",
  },
  // Specialized forms
  {
    eventTypeGuid: "1ab1c782-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Approval",
    formGuid: "7feaa63d-c502-f111-8407-7ced8d1dc988",
    formName: "Event Approval side pane form",
  },
  {
    eventTypeGuid: "348f8195-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Communication",
    formGuid: "70a2a423-c602-f111-8406-7c1e525abd8b",
    formName: "Event Communication side pane form",
  },
  {
    eventTypeGuid: "8fb9b5a7-99ff-f011-8406-7c1e525abd8b",
    eventTypeName: "Meeting",
    formGuid: "187bef8a-c602-f111-8407-7ced8d1dc988",
    formName: "Event Meeting side pane form",
  },
];

/**
 * Default form GUID when event type is unknown or not mapped.
 * Falls back to the Event Task side pane form.
 */
export const DEFAULT_SIDE_PANE_FORM_ID = "c4d7c4ee-4502-f111-8406-7c1e525abd8b";

/**
 * Get the Side Pane Form GUID for a given Event Type GUID.
 * Returns the default form if not found.
 *
 * @param eventTypeGuid - The Event Type GUID (sprk_eventtype)
 * @returns The Form GUID to use for the side pane
 */
export function getFormGuidForEventType(eventTypeGuid: string | undefined): string {
  if (!eventTypeGuid) {
    return DEFAULT_SIDE_PANE_FORM_ID;
  }

  // Normalize GUID (remove braces, lowercase)
  const normalizedGuid = eventTypeGuid.replace(/[{}]/g, "").toLowerCase();

  const mapping = EVENT_TYPE_FORM_MAPPINGS.find(
    (m) => m.eventTypeGuid.toLowerCase() === normalizedGuid
  );

  return mapping?.formGuid ?? DEFAULT_SIDE_PANE_FORM_ID;
}

// ─────────────────────────────────────────────────────────────────────────────
// Predefined Views Configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Predefined Event views available in the ViewSelector dropdown.
 * These are system views configured in Dataverse.
 */
export interface IEventViewConfig {
  id: string;
  name: string;
  isDefault?: boolean;
}

/**
 * Predefined Event views from Dataverse savedquery entity.
 * These GUIDs correspond to the views in the Events-View-GUIDS.md note.
 */
export const EVENT_VIEWS: IEventViewConfig[] = [
  {
    id: "7690f9d7-9cb1-4837-ac76-d0705e9e1b75",
    name: "Active Events",
    isDefault: true,
  },
  {
    id: "b836398f-6900-f111-8407-7c1e520aa4df",
    name: "All Events",
  },
  {
    id: "32c1041a-ba02-f111-8407-7c1e520aa4df",
    name: "All Tasks",
  },
  {
    id: "e0d27d71-ba02-f111-8407-7c1e520aa4df",
    name: "All Tasks Open",
  },
];

/**
 * Get the default Event view GUID.
 */
export function getDefaultEventViewId(): string {
  const defaultView = EVENT_VIEWS.find((v) => v.isDefault);
  return defaultView?.id ?? EVENT_VIEWS[0].id;
}

// ─────────────────────────────────────────────────────────────────────────────
// Side Pane Configuration
// ─────────────────────────────────────────────────────────────────────────────

/** Side pane ID for event details */
export const EVENT_DETAIL_PANE_ID = "eventDetailPane";

/** Side pane ID for calendar */
export const CALENDAR_PANE_ID = "calendarPane";

/** Side pane width in pixels (Event pane) - matches Power Apps quick create width */
export const PANE_WIDTH = 400;

/** Calendar side pane width in pixels */
export const CALENDAR_PANE_WIDTH = 340;

/** Entity logical name for Events */
export const EVENT_ENTITY_NAME = "sprk_event";

/**
 * Calendar side pane web resource name.
 * This is the deployed HTML web resource for CalendarSidePane.
 */
export const CALENDAR_WEB_RESOURCE_NAME = "sprk_calendarsidepane.html";

/**
 * Event detail side pane web resource name.
 * This is the deployed HTML web resource for EventDetailSidePane.
 * Replaces the OOB entityrecord approach with a React-based side pane.
 */
export const EVENT_DETAIL_WEB_RESOURCE_NAME = "sprk_eventdetailsidepane.html";
