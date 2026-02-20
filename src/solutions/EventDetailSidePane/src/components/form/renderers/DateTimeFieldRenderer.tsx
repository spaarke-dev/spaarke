/**
 * DateTimeFieldRenderer - Renders a date + time picker field
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { Input, makeStyles, shorthands } from "@fluentui/react-components";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import type { IFieldConfig, FieldChangeCallback } from "../../../types/FormConfig";

const useStyles = makeStyles({
  row: {
    display: "flex",
    ...shorthands.gap("8px"),
    alignItems: "flex-start",
  },
  datePicker: {
    flexGrow: 1,
    flexBasis: "60%",
    minWidth: 0,
  },
  timeInput: {
    flexGrow: 0,
    flexBasis: "40%",
    minWidth: "100px",
  },
});

export interface DateTimeFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

function parseISODate(isoString: string | null | undefined): Date | null {
  if (!isoString) return null;
  try {
    const date = new Date(isoString);
    return isNaN(date.getTime()) ? null : date;
  } catch {
    return null;
  }
}

function formatDateForDisplay(date?: Date): string {
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function extractTimeString(date: Date | null): string {
  if (!date) return "";
  const hours = date.getHours().toString().padStart(2, "0");
  const minutes = date.getMinutes().toString().padStart(2, "0");
  return `${hours}:${minutes}`;
}

function combineDateAndTime(date: Date, timeString: string): Date {
  const combined = new Date(date);
  if (timeString) {
    const [hours, minutes] = timeString.split(":").map(Number);
    if (!isNaN(hours) && !isNaN(minutes)) {
      combined.setHours(hours, minutes, 0, 0);
    }
  } else {
    combined.setHours(9, 0, 0, 0);
  }
  return combined;
}

export const DateTimeFieldRenderer: React.FC<DateTimeFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
}) => {
  const styles = useStyles();
  const dateValue = React.useMemo(() => parseISODate(value as string), [value]);
  const [timeValue, setTimeValue] = React.useState(() => extractTimeString(dateValue));

  React.useEffect(() => {
    setTimeValue(extractTimeString(dateValue));
  }, [dateValue]);

  const isDisabled = disabled || config.readOnly;

  const handleDateSelect = React.useCallback(
    (date: Date | null | undefined) => {
      if (date) {
        const combined = combineDateAndTime(date, timeValue);
        onChange(config.name, combined.toISOString());
      } else {
        onChange(config.name, null);
      }
    },
    [config.name, onChange, timeValue]
  );

  const handleTimeChange = React.useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const newTime = event.target.value;
      setTimeValue(newTime);
      if (dateValue) {
        const combined = combineDateAndTime(dateValue, newTime);
        onChange(config.name, combined.toISOString());
      }
    },
    [config.name, onChange, dateValue]
  );

  return (
    <div className={styles.row}>
      <DatePicker
        className={styles.datePicker}
        value={dateValue}
        onSelectDate={handleDateSelect}
        disabled={isDisabled}
        placeholder="Select date..."
        formatDate={formatDateForDisplay}
        aria-label={`${config.label} date`}
      />
      <Input
        className={styles.timeInput}
        type="time"
        value={timeValue}
        onChange={handleTimeChange}
        disabled={isDisabled}
        aria-label={`${config.label} time`}
      />
    </div>
  );
};
