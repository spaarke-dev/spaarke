/**
 * TodoDetail types — Shared type definitions for the Todo Detail component.
 *
 * Data comes from TWO entities:
 *   - sprk_event: core event fields (description, due date, scores, lookups)
 *   - sprk_eventtodo: to-do extension fields (notes, completed, statuscode)
 *
 * Extracted from TodoDetailSidePane for reuse across solutions (ADR-012).
 */
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
/** OData $select fields for the sprk_eventtodo query. */
export const TODO_EXTENSION_SELECT = [
    "sprk_eventtodoid",
    "sprk_todonotes",
    "sprk_completed",
    "sprk_completeddate",
    "statecode",
    "statuscode",
].join(",");
//# sourceMappingURL=types.js.map