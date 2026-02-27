/**
 * TodoRecord — Fields needed for the Todo Detail side pane display.
 *
 * Data comes from TWO entities:
 *   - sprk_event: core event fields (description, due date, scores, lookups)
 *   - sprk_eventtodo: to-do extension fields (notes, completed, statuscode)
 */

// ---------------------------------------------------------------------------
// sprk_event fields
// ---------------------------------------------------------------------------

export interface ITodoRecord {
  sprk_eventid: string;
  sprk_eventname: string;
  sprk_description?: string;
  sprk_duedate?: string;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  sprk_todostatus?: number;
  sprk_todocolumn?: number;
  sprk_todopinned?: boolean;
  /** Formatted value from OData for assigned-to lookup. */
  "_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"?: string;
  _sprk_assignedto_value?: string;
  /** Formatted value from OData for event type lookup. */
  "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"?: string;
  _sprk_eventtype_ref_value?: string;
  /** Regarding record (associated matter/project). */
  sprk_regardingrecordid?: string;
  sprk_regardingrecordname?: string;
  /** Formatted value from OData for regarding record type lookup. */
  "_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"?: string;
  _sprk_regardingrecordtype_value?: string;
}

/** OData $select fields for the sprk_event query. */
export const TODO_DETAIL_SELECT = [
  "sprk_eventid",
  "sprk_eventname",
  "sprk_description",
  "sprk_duedate",
  "sprk_priorityscore",
  "sprk_effortscore",
  "sprk_todostatus",
  "sprk_todocolumn",
  "sprk_todopinned",
  "_sprk_assignedto_value",
  "_sprk_eventtype_ref_value",
  "sprk_regardingrecordid",
  "sprk_regardingrecordname",
  "_sprk_regardingrecordtype_value",
].join(",");

// ---------------------------------------------------------------------------
// sprk_eventtodo fields (related entity — to-do extension)
// ---------------------------------------------------------------------------

export interface ITodoExtension {
  sprk_eventtodoid: string;
  /** To Do Notes (multiline text). */
  sprk_todonotes?: string;
  /** Completed flag. */
  sprk_completed?: boolean;
  /** Completed date (ISO string). */
  sprk_completeddate?: string;
  /** Status code — Completed = 2. */
  statuscode?: number;
}

/** OData $select fields for the sprk_eventtodo query. */
export const TODO_EXTENSION_SELECT = [
  "sprk_eventtodoid",
  "sprk_todonotes",
  "sprk_completed",
  "sprk_completeddate",
  "statuscode",
].join(",");
