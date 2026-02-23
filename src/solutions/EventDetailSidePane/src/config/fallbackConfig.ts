/**
 * Fallback Form Config
 *
 * Used when an Event Type has no sprk_fieldconfigjson value (null or empty).
 * Provides a minimal useful form with common fields.
 *
 * Assigned fields (sprk_assignedto, sprk_assignedattorney, etc.) are
 * intentionally excluded — they are visible in the grid view.
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
  ],
};
