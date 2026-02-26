/**
 * TodoRecord — Fields needed for the Todo Detail side pane display.
 */

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
  sprk_completed?: boolean;
  sprk_completedate?: string;
  /** Formatted value from OData for assigned-to lookup. */
  "_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"?: string;
  _sprk_assignedto_value?: string;
}

/** OData $select fields for the Todo Detail pane. */
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
  "sprk_completed",
  "sprk_completedate",
  "_sprk_assignedto_value",
].join(",");
