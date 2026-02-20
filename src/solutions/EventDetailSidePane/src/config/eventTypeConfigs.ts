/**
 * Event Type Form Configurations (Approach A)
 *
 * Reference configs for each Event Type's sprk_fieldconfigjson value.
 * These define what sections and fields appear in the side pane form.
 *
 * To deploy: paste the JSON into the sprk_fieldconfigjson field on
 * each sprk_eventtype record in Dataverse.
 *
 * Fixed chrome (NOT in these configs):
 * - Header: sprk_eventname (inline editable), Event Type badge, parent link, action buttons
 * - StatusSection: statuscode (dropdown)
 * - MemoSection: sprk_memo (separate entity)
 * - Footer: Save button, version
 *
 * Assigned fields (sprk_assignedto, sprk_assignedattorney, etc.) are
 * intentionally excluded — they are visible in the grid view.
 *
 * Field name corrections:
 * - sprk_completedby (correct) — OOB form doc had "sprk_compledby" typo
 * - ownerid (correct) — OOB form doc had "owner"
 *
 * @see approach-a-dynamic-form-renderer.md
 * @see sprk_event-json-fields.md (OOB form field reference)
 */

import type { IFormConfig } from "../types/FormConfig";

// ─────────────────────────────────────────────────────────────────────────────
// Task Event Type
// GUID: 124f5fc9-98ff-f011-8406-7c1e525abd8b
// ─────────────────────────────────────────────────────────────────────────────

export const TASK_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "dates",
      title: "Dates",
      fields: [
        { name: "sprk_duedate", type: "date", label: "Due Date" },
        { name: "sprk_finalduedate", type: "date", label: "Final Due Date" },
        { name: "sprk_completeddate", type: "date", label: "Completed Date" },
        {
          name: "sprk_completedby",
          type: "lookup",
          label: "Completed By",
          targets: ["contact"],
          navigationProperty: "sprk_CompletedBy",
        },
      ],
    },
    {
      id: "priority",
      title: "Priority",
      fields: [
        { name: "sprk_priority", type: "choice", label: "Priority" },
        { name: "sprk_effort", type: "choice", label: "Effort" },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Action Event Type
// GUID: 5a1c56c3-98ff-f011-8406-7c1e525abd8b
// ─────────────────────────────────────────────────────────────────────────────

export const ACTION_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "dates",
      title: "Dates",
      fields: [
        { name: "sprk_basedate", type: "date", label: "Base Date" },
        { name: "sprk_completeddate", type: "date", label: "Completed Date" },
        {
          name: "sprk_completedby",
          type: "lookup",
          label: "Completed By",
          targets: ["contact"],
          navigationProperty: "sprk_CompletedBy",
        },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Milestone Event Type
// GUID: b86d712b-99ff-f011-8406-7c1e525abd8b
// ─────────────────────────────────────────────────────────────────────────────

export const MILESTONE_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "dates",
      title: "Dates",
      fields: [
        { name: "sprk_duedate", type: "date", label: "Due Date" },
        { name: "sprk_finalduedate", type: "date", label: "Final Due Date" },
        { name: "sprk_completeddate", type: "date", label: "Completed Date" },
        {
          name: "sprk_completedby",
          type: "lookup",
          label: "Completed By",
          targets: ["contact"],
          navigationProperty: "sprk_CompletedBy",
        },
      ],
    },
    {
      id: "priority",
      title: "Priority",
      fields: [
        { name: "sprk_priority", type: "choice", label: "Priority" },
        { name: "sprk_effort", type: "choice", label: "Effort" },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Meeting Event Type
// GUID: 8fb9b5a7-99ff-f011-8406-7c1e525abd8b
// ─────────────────────────────────────────────────────────────────────────────

export const MEETING_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "details",
      title: "Meeting Details",
      fields: [
        { name: "sprk_meetingtype", type: "choice", label: "Meeting Type" },
        { name: "sprk_meetingdate", type: "date", label: "Meeting Date" },
        { name: "sprk_meetinglink", type: "url", label: "Meeting Link" },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Email Event Type
// GUID: f6e75ae8-ad0d-f111-8342-7ced8d1dc988
// ─────────────────────────────────────────────────────────────────────────────

export const EMAIL_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "details",
      title: "Email Details",
      fields: [
        {
          name: "sprk_regardingemail",
          type: "lookup",
          label: "Regarding Email",
          targets: ["email"],
          navigationProperty: "sprk_RegardingEmail",
        },
        { name: "sprk_emaildate", type: "text", label: "Email Date" },
        { name: "sprk_emailfrom", type: "text", label: "From" },
        { name: "sprk_emailto", type: "text", label: "To" },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Approval Event Type
// GUID: 1ab1c782-99ff-f011-8406-7c1e525abd8b
// ─────────────────────────────────────────────────────────────────────────────

export const APPROVAL_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "details",
      title: "Approval Details",
      fields: [
        { name: "sprk_approveddate", type: "date", label: "Approved Date" },
        {
          name: "sprk_approvedby",
          type: "lookup",
          label: "Approved By",
          targets: ["contact"],
          navigationProperty: "sprk_ApprovedBy",
        },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// Phone Call Event Type
// GUID: 23300069-af0d-f111-8342-7ced8d1dc988
// ─────────────────────────────────────────────────────────────────────────────

export const PHONE_CALL_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "description",
      title: "Description",
      fields: [
        { name: "sprk_description", type: "multiline", label: "Description" },
      ],
    },
    {
      id: "dates",
      title: "Dates",
      fields: [
        { name: "sprk_completeddate", type: "date", label: "Completed Date" },
        {
          name: "sprk_completedby",
          type: "lookup",
          label: "Completed By",
          targets: ["contact"],
          navigationProperty: "sprk_CompletedBy",
        },
      ],
    },
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// All Configs Map (by Event Type GUID)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Map of Event Type GUID → IFormConfig.
 * Used for reference and development testing.
 * In production, these configs live in sprk_fieldconfigjson on the Event Type record.
 */
export const EVENT_TYPE_CONFIGS: Record<string, { name: string; config: IFormConfig }> = {
  "124f5fc9-98ff-f011-8406-7c1e525abd8b": { name: "Task", config: TASK_CONFIG },
  "5a1c56c3-98ff-f011-8406-7c1e525abd8b": { name: "Action", config: ACTION_CONFIG },
  "b86d712b-99ff-f011-8406-7c1e525abd8b": { name: "Milestone", config: MILESTONE_CONFIG },
  "8fb9b5a7-99ff-f011-8406-7c1e525abd8b": { name: "Meeting", config: MEETING_CONFIG },
  "f6e75ae8-ad0d-f111-8342-7ced8d1dc988": { name: "Email", config: EMAIL_CONFIG },
  "1ab1c782-99ff-f011-8406-7c1e525abd8b": { name: "Approval", config: APPROVAL_CONFIG },
  "23300069-af0d-f111-8342-7ced8d1dc988": { name: "Phone Call", config: PHONE_CALL_CONFIG },
};

/**
 * Get the JSON string to paste into sprk_fieldconfigjson for a given Event Type.
 * Useful for seeding Dataverse records.
 */
export function getConfigJson(eventTypeName: string): string {
  const entry = Object.values(EVENT_TYPE_CONFIGS).find(
    (e) => e.name.toLowerCase() === eventTypeName.toLowerCase()
  );
  if (!entry) return "";
  return JSON.stringify(entry.config, null, 2);
}
