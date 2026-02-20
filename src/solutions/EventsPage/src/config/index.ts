/**
 * Configuration module exports
 *
 * Centralized configuration for the Events Page including:
 * - Event Type to Form GUID mapping
 * - Predefined Views configuration
 * - Side Pane configuration
 */

export {
  EVENT_TYPE_FORM_MAPPINGS,
  DEFAULT_SIDE_PANE_FORM_ID,
  getFormGuidForEventType,
  EVENT_VIEWS,
  getDefaultEventViewId,
  EVENT_DETAIL_PANE_ID,
  CALENDAR_PANE_ID,
  PANE_WIDTH,
  CALENDAR_PANE_WIDTH,
  EVENT_ENTITY_NAME,
  CALENDAR_WEB_RESOURCE_NAME,
  EVENT_DETAIL_WEB_RESOURCE_NAME,
} from "./eventConfig";

export type { IEventTypeFormMapping, IEventViewConfig } from "./eventConfig";
