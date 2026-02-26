/**
 * DateRangeFilter component
 *
 * Date range picker with quick-select presets matching the PCF SemanticSearchControl.
 * The "Date Range" label sits above a Dropdown for quick presets (Off, Last 30 days, etc.).
 * Selecting a preset populates the from/to date fields below.
 *
 * Layout:
 *   - "Date Range" bold label
 *   - Dropdown (quick presets: Off, Last 30 days, Last 90 days, This year, Last year)
 *   - From date input (populated by preset, editable) — shown when preset is not Off
 *   - To date input (populated by preset, editable) — shown when preset is not Off
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import { useState, useCallback, useMemo, useEffect } from "react";
import {
    makeStyles,
    tokens,
    Input,
    Label,
    Dropdown,
    Option,
    useId,
} from "@fluentui/react-components";
import type { DateRange } from "../types";

export interface DateRangeFilterProps {
    /** Label text for the filter (defaults to "Date Range") */
    label?: string;
    /** Current date range value */
    value: DateRange;
    /** Callback when range changes */
    onChange: (value: DateRange) => void;
}

// =============================================
// Quick-select presets
// =============================================

interface DatePreset {
    value: string;
    label: string;
    days: number; // positive = N days ago, -1 = this year, -2 = last year, 0 = off
}

const DATE_PRESETS: DatePreset[] = [
    { value: "off", label: "Off", days: 0 },
    { value: "last30", label: "Last 30 days", days: 30 },
    { value: "last90", label: "Last 90 days", days: 90 },
    { value: "thisYear", label: "This year", days: -1 },
    { value: "lastYear", label: "Last year", days: -2 },
];

/**
 * Get date string in YYYY-MM-DD format
 */
function formatDateForInput(date: Date): string {
    return date.toISOString().split("T")[0];
}

/**
 * Calculate preset date range
 */
function calculatePresetRange(days: number): DateRange {
    if (days === 0) {
        return { from: null, to: null };
    }

    const today = new Date();

    if (days === -1) {
        // This Year: from = Jan 1, to = today
        const startOfYear = new Date(today.getFullYear(), 0, 1);
        return { from: formatDateForInput(startOfYear), to: formatDateForInput(today) };
    }

    if (days === -2) {
        // Last Year: from = Jan 1 prev year, to = Dec 31 prev year
        const lastYearStart = new Date(today.getFullYear() - 1, 0, 1);
        const lastYearEnd = new Date(today.getFullYear() - 1, 11, 31);
        return {
            from: formatDateForInput(lastYearStart),
            to: formatDateForInput(lastYearEnd),
        };
    }

    // Days ago: from = today - N days, to = today
    const from = new Date(today);
    from.setDate(from.getDate() - days);
    return { from: formatDateForInput(from), to: formatDateForInput(today) };
}

/**
 * Determine which preset matches the current date range (if any).
 */
function detectActivePreset(value: DateRange): string {
    if (!value.from && !value.to) return "off";
    for (const preset of DATE_PRESETS) {
        if (preset.days === 0) continue;
        const range = calculatePresetRange(preset.days);
        if (range.from === value.from && range.to === value.to) return preset.value;
    }
    // Dates set but don't match a preset — custom range
    return "custom";
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    label: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    dropdown: {
        width: "100%",
    },
    dateFields: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    fieldLabel: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    dateInput: {
        width: "100%",
    },
    // Hide native date placeholder text when input is empty (Chromium/Edge)
    dateInputEmpty: {
        width: "100%",
        "& input[type='date']::-webkit-datetime-edit": {
            color: tokens.colorTransparentStroke,
        },
    },
    validationError: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: tokens.fontSizeBase200,
    },
});

// =============================================
// Component
// =============================================

export const DateRangeFilter: React.FC<DateRangeFilterProps> = ({
    label = "Date Range",
    value,
    onChange,
}) => {
    const styles = useStyles();
    const fromId = useId("date-from");
    const toId = useId("date-to");

    // Local state for individual date inputs
    const [fromDate, setFromDate] = useState<string>(value.from ?? "");
    const [toDate, setToDate] = useState<string>(value.to ?? "");
    const [validationError, setValidationError] = useState<string | null>(null);

    // Track which preset is active
    const activePreset = useMemo(() => detectActivePreset(value), [value]);

    // Sync local state with prop changes
    useEffect(() => {
        setFromDate(value.from ?? "");
        setToDate(value.to ?? "");
    }, [value]);

    // Validate and update range
    const updateRange = useCallback(
        (from: string, to: string) => {
            setValidationError(null);
            if (from && to && from > to) {
                setValidationError("From date must be before To date");
                return;
            }
            onChange({ from: from || null, to: to || null });
        },
        [onChange],
    );

    // Handle from date change
    const handleFromChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            const newFrom = ev.target.value;
            setFromDate(newFrom);
            updateRange(newFrom, toDate);
        },
        [toDate, updateRange],
    );

    // Handle to date change
    const handleToChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            const newTo = ev.target.value;
            setToDate(newTo);
            updateRange(fromDate, newTo);
        },
        [fromDate, updateRange],
    );

    // Handle preset selection from dropdown
    const handlePresetSelect = useCallback(
        (_event: unknown, data: { optionValue?: string }) => {
            if (!data.optionValue) return;
            const preset = DATE_PRESETS.find((p) => p.value === data.optionValue);
            if (!preset) return;
            const range = calculatePresetRange(preset.days);
            setFromDate(range.from ?? "");
            setToDate(range.to ?? "");
            onChange(range);
            setValidationError(null);
        },
        [onChange],
    );

    // Show date fields when a non-off preset is selected or dates have values
    const showDateFields = activePreset !== "off";

    return (
        <div className={styles.container}>
            <Label className={styles.label}>{label}</Label>
            <Dropdown
                className={styles.dropdown}
                size="small"
                value={
                    activePreset === "custom"
                        ? "Custom"
                        : DATE_PRESETS.find((p) => p.value === activePreset)?.label ?? "Off"
                }
                selectedOptions={[activePreset]}
                onOptionSelect={handlePresetSelect}
                aria-label="Date range preset"
            >
                {DATE_PRESETS.map((preset) => (
                    <Option key={preset.value} value={preset.value}>
                        {preset.label}
                    </Option>
                ))}
            </Dropdown>

            {showDateFields && (
                <div className={styles.dateFields}>
                    <Label htmlFor={fromId} className={styles.fieldLabel} size="small">
                        From
                    </Label>
                    <Input
                        id={fromId}
                        className={fromDate ? styles.dateInput : styles.dateInputEmpty}
                        type="date"
                        value={fromDate}
                        onChange={handleFromChange}
                        aria-label="From date"
                    />
                    <Label htmlFor={toId} className={styles.fieldLabel} size="small">
                        To
                    </Label>
                    <Input
                        id={toId}
                        className={toDate ? styles.dateInput : styles.dateInputEmpty}
                        type="date"
                        value={toDate}
                        onChange={handleToChange}
                        aria-label="To date"
                    />
                    {validationError && (
                        <span className={styles.validationError}>{validationError}</span>
                    )}
                </div>
            )}
        </div>
    );
};

export default DateRangeFilter;
