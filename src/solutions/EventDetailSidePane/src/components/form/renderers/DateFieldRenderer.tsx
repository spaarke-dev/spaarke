/**
 * DateFieldRenderer - Renders a date-only picker field
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";

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
  if (!isoString) return null;
  try {
    const date = new Date(isoString);
    return isNaN(date.getTime()) ? null : date;
  } catch {
    return null;
  }
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
        // Store as YYYY-MM-DD for Dataverse date-only fields
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        onChange(config.name, `${year}-${month}-${day}`);
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
