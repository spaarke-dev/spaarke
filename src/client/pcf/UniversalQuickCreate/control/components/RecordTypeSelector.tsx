/**
 * Record Type Selector Component
 *
 * Dropdown component for selecting target record type(s) for matching
 * (Matters, Projects, Invoices, or All).
 *
 * ADR Compliance:
 * - ADR-006: PCF control pattern
 * - ADR-012: Fluent UI v9 components
 *
 * @version 1.0.0
 */

import * as React from 'react';
import {
    Dropdown,
    Option,
    Label,
    makeStyles,
    tokens,
    SelectionEvents,
    OptionOnSelectData
} from '@fluentui/react-components';
import { FilterRegular } from '@fluentui/react-icons';
import { RecordTypeFilter, RECORD_TYPE_OPTIONS } from '../services/useRecordMatch';

/**
 * Component Props
 */
export interface RecordTypeSelectorProps {
    /** Currently selected record type filter */
    value: RecordTypeFilter;
    /** Callback when selection changes */
    onChange: (value: RecordTypeFilter) => void;
    /** Whether the selector is disabled */
    disabled?: boolean;
    /** Optional className for styling */
    className?: string;
    /** Show label (default: true) */
    showLabel?: boolean;
}

/**
 * Styles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS
    },
    labelContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS
    },
    label: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground2
    },
    icon: {
        fontSize: '16px',
        color: tokens.colorNeutralForeground3
    },
    dropdown: {
        minWidth: '160px'
    }
});

/**
 * Record Type Selector Component
 *
 * Allows users to select which record types to match against.
 */
export const RecordTypeSelector: React.FC<RecordTypeSelectorProps> = ({
    value,
    onChange,
    disabled = false,
    className,
    showLabel = true
}) => {
    const styles = useStyles();

    const handleChange = React.useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            if (data.optionValue) {
                onChange(data.optionValue as RecordTypeFilter);
            }
        },
        [onChange]
    );

    // Get the display label for the current value
    const selectedLabel = RECORD_TYPE_OPTIONS.find(opt => opt.value === value)?.label || 'All Records';

    return (
        <div className={`${styles.container} ${className || ''}`}>
            {showLabel && (
                <div className={styles.labelContainer}>
                    <FilterRegular className={styles.icon} />
                    <Label className={styles.label}>Match Type</Label>
                </div>
            )}
            <Dropdown
                className={styles.dropdown}
                value={selectedLabel}
                selectedOptions={[value]}
                onOptionSelect={handleChange}
                disabled={disabled}
                aria-label="Select record type to match"
            >
                {RECORD_TYPE_OPTIONS.map(option => (
                    <Option key={option.value} value={option.value}>
                        {option.label}
                    </Option>
                ))}
            </Dropdown>
        </div>
    );
};

export default RecordTypeSelector;
