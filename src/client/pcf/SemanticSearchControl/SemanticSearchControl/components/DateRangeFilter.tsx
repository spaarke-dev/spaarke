/**
 * DateRangeFilter component
 *
 * Date range picker for filtering by document creation date.
 * Section title doubles as a Quick Select dropdown for presets.
 * Stacked vertical layout with From/To inputs below.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useState, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Input,
    Label,
    Button,
    Menu,
    MenuTrigger,
    MenuPopover,
    MenuList,
    MenuItem,
    useId,
} from "@fluentui/react-components";
import { ChevronDownRegular } from "@fluentui/react-icons";
import { IDateRangeFilterProps, DateRange } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    inputGroup: {
        display: "flex",
        flexDirection: "column",
        alignItems: "stretch",
        gap: tokens.spacingVerticalXS,
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
            color: "transparent",
        },
    },
    titleButton: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        minWidth: "auto",
        paddingLeft: "0px",
    },
});

/**
 * Quick date presets
 */
const DATE_PRESETS = [
    { label: "Last 30 days", days: 30 },
    { label: "Last 90 days", days: 90 },
    { label: "This year", days: -1 }, // Special case: start of year
    { label: "Last year", days: -2 }, // Special case: previous year
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
    const today = new Date();
    const to = formatDateForInput(today);

    if (days === -1) {
        // This year
        const startOfYear = new Date(today.getFullYear(), 0, 1);
        return { from: formatDateForInput(startOfYear), to };
    }

    if (days === -2) {
        // Last year
        const lastYearStart = new Date(today.getFullYear() - 1, 0, 1);
        const lastYearEnd = new Date(today.getFullYear() - 1, 11, 31);
        return {
            from: formatDateForInput(lastYearStart),
            to: formatDateForInput(lastYearEnd),
        };
    }

    // Days ago
    const from = new Date(today);
    from.setDate(from.getDate() - days);
    return { from: formatDateForInput(from), to };
}

/**
 * DateRangeFilter component for date filtering.
 * The section title is a Quick Select dropdown for common presets.
 * Below it are From/To date inputs for custom range.
 */
export const DateRangeFilter: React.FC<IDateRangeFilterProps> = ({
    label,
    value,
    onChange,
    disabled,
}) => {
    const styles = useStyles();
    const fromId = useId("date-from");
    const toId = useId("date-to");

    // Local state for individual date inputs
    const [fromDate, setFromDate] = useState<string>(value?.from ?? "");
    const [toDate, setToDate] = useState<string>(value?.to ?? "");
    const [validationError, setValidationError] = useState<string | null>(null);

    // Sync local state with prop changes
    React.useEffect(() => {
        setFromDate(value?.from ?? "");
        setToDate(value?.to ?? "");
    }, [value]);

    // Validate and update range
    const updateRange = useCallback(
        (from: string, to: string) => {
            // Clear error
            setValidationError(null);

            // If both empty, set to null
            if (!from && !to) {
                onChange(null);
                return;
            }

            // Validate: from must be <= to if both set
            if (from && to && from > to) {
                setValidationError("From date must be before To date");
                return;
            }

            onChange({
                from: from || null,
                to: to || null,
            });
        },
        [onChange]
    );

    // Handle from date change
    const handleFromChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            const newFrom = ev.target.value;
            setFromDate(newFrom);
            updateRange(newFrom, toDate);
        },
        [toDate, updateRange]
    );

    // Handle to date change
    const handleToChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            const newTo = ev.target.value;
            setToDate(newTo);
            updateRange(fromDate, newTo);
        },
        [fromDate, updateRange]
    );

    // Handle preset selection
    const handlePresetSelect = useCallback(
        (days: number) => {
            const range = calculatePresetRange(days);
            setFromDate(range.from ?? "");
            setToDate(range.to ?? "");
            onChange(range);
            setValidationError(null);
        },
        [onChange]
    );

    // Handle clear
    const handleClear = useCallback(() => {
        setFromDate("");
        setToDate("");
        onChange(null);
        setValidationError(null);
    }, [onChange]);

    // Check if has value
    const hasValue = useMemo(() => {
        return Boolean(fromDate || toDate);
    }, [fromDate, toDate]);

    return (
        <div className={styles.container}>
            {/* Section title = Quick Select dropdown */}
            <Menu>
                <MenuTrigger disableButtonEnhancement>
                    <Button
                        className={styles.titleButton}
                        appearance="subtle"
                        size="small"
                        icon={<ChevronDownRegular />}
                        iconPosition="after"
                        disabled={disabled}
                    >
                        {label}
                    </Button>
                </MenuTrigger>
                <MenuPopover>
                    <MenuList>
                        {DATE_PRESETS.map((preset) => (
                            <MenuItem
                                key={preset.label}
                                onClick={() => handlePresetSelect(preset.days)}
                            >
                                {preset.label}
                            </MenuItem>
                        ))}
                    </MenuList>
                </MenuPopover>
            </Menu>

            {/* From/To date inputs */}
            <div className={styles.inputGroup}>
                <Label htmlFor={fromId} className={styles.fieldLabel} size="small">From</Label>
                <Input
                    id={fromId}
                    className={fromDate ? styles.dateInput : styles.dateInputEmpty}
                    type="date"
                    value={fromDate}
                    onChange={handleFromChange}
                    disabled={disabled}
                    aria-label="From date"
                />
                <Label htmlFor={toId} className={styles.fieldLabel} size="small">To</Label>
                <Input
                    id={toId}
                    className={toDate ? styles.dateInput : styles.dateInputEmpty}
                    type="date"
                    value={toDate}
                    onChange={handleToChange}
                    disabled={disabled}
                    aria-label="To date"
                />
                {hasValue && (
                    <Button
                        appearance="subtle"
                        size="small"
                        onClick={handleClear}
                        disabled={disabled}
                    >
                        Clear
                    </Button>
                )}
            </div>
            {validationError && (
                <span style={{ color: tokens.colorPaletteRedForeground1, fontSize: tokens.fontSizeBase200 }}>
                    {validationError}
                </span>
            )}
        </div>
    );
};

export default DateRangeFilter;
