/**
 * Fallback Form Config
 *
 * Used when an Event Type has no sprk_fieldconfigjson value (null or empty).
 * Provides a minimal useful form with common fields.
 */

import type { IFormConfig } from "../types/FormConfig";

/**
 * Generic fallback config for unknown or unconfigured Event Types.
 * Shows a minimal set of fields that apply to most events.
 */
export const FALLBACK_FORM_CONFIG: IFormConfig = {
  version: 1,
  sections: [
    {
      id: "dates",
      title: "Dates",
      fields: [
        { name: "sprk_duedate", type: "date", label: "Due Date" },
        { name: "sprk_completeddate", type: "date", label: "Completed Date" },
        {
          name: "sprk_completedby",
          type: "lookup",
          label: "Completed By",
          targets: ["contact"],
        },
      ],
    },
    {
      id: "assigned",
      title: "Assigned",
      fields: [
        {
          name: "sprk_assignedto",
          type: "lookup",
          label: "Assigned To",
          targets: ["contact"],
        },
        {
          name: "sprk_assignedattorney",
          type: "lookup",
          label: "Assigned Attorney",
          targets: ["contact"],
        },
        {
          name: "sprk_assignedparalegal",
          type: "lookup",
          label: "Assigned Paralegal",
          targets: ["contact"],
        },
        {
          name: "ownerid",
          type: "lookup",
          label: "Owner",
          targets: ["systemuser", "team"],
          readOnly: true,
        },
      ],
    },
  ],
};
