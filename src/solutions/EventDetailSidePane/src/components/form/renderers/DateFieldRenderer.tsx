/**
 * DateFieldRenderer - Renders a date-only picker field
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";
import { formatDateOnly, parseDueDate } from "@spaarke/ui-components";

export interface DateFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

/**
 * Parse ISO date string to Date object
 */
function parseISODate(isoString: string | null | undefined): Date | null {
  // Task 098: a bare "YYYY-MM-DD" (the six sprk_event date columns are Date Only) is that LOCAL calendar day;
  // new Date("YYYY-MM-DD") is UTC midnight — the picker showed the previous day west of UTC.
  return parseDueDate(isoString);
}

/**
 * Format Date object to display string
 */
function formatDateForDisplay(date?: Date): string {
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

export const DateFieldRenderer: React.FC<DateFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
}) => {
  const dateValue = React.useMemo(() => parseISODate(value as string), [value]);

  const handleSelect = React.useCallback(
    (date: Date | null | undefined) => {
      if (date) {
        // Store as YYYY-MM-DD for Dataverse date-only fields (the picked LOCAL day)
        onChange(config.name, formatDateOnly(date));
      } else {
        onChange(config.name, null);
      }
    },
    [config.name, onChange]
  );

  return (
    <DatePicker
      value={dateValue}
      onSelectDate={handleSelect}
      disabled={disabled || config.readOnly}
      placeholder=""
      formatDate={formatDateForDisplay}
      aria-label={config.label}
      style={{ width: "100%" }}
    />
  );
};
