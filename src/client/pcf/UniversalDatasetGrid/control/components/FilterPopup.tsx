/**
 * FilterPopup Component
 *
 * Popup UI for column filtering - supports text, choice (OptionSet), and date filters.
 * Uses Fluent UI v9 components and design tokens for dark mode support.
 *
 * Task 016: Add Column/Field Filters
 */

import * as React from 'react';
import {
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Button,
    Input,
    Dropdown,
    Option,
    makeStyles,
    tokens,
    Text,
    Divider,
    Field,
} from '@fluentui/react-components';
import {
    Filter20Regular,
    Filter20Filled,
    Dismiss20Regular,
} from '@fluentui/react-icons';

/**
 * Filter operator types for different data types
 */
export type TextFilterOperator = 'contains' | 'equals' | 'startswith' | 'endswith';
export type ChoiceFilterOperator = 'equals' | 'in';
export type DateFilterOperator = 'equals' | 'before' | 'after' | 'between';

/**
 * Filter value structure for different filter types
 */
export interface TextFilterValue {
    type: 'text';
    operator: TextFilterOperator;
    value: string;
}

export interface ChoiceFilterValue {
    type: 'choice';
    operator: ChoiceFilterOperator;
    values: (string | number)[];
}

export interface DateFilterValue {
    type: 'date';
    operator: DateFilterOperator;
    value?: Date;
    endValue?: Date; // For 'between' operator
}

export type FilterValue = TextFilterValue | ChoiceFilterValue | DateFilterValue | null;

/**
 * Choice option for OptionSet columns
 */
export interface ChoiceOption {
    value: number | string;
    label: string;
}

/**
 * Props for FilterPopup component
 */
export interface FilterPopupProps {
    /** Column name being filtered */
    columnName: string;

    /** Display name for the column */
    columnDisplayName: string;

    /** Data type of the column (affects filter UI) */
    dataType: string;

    /** Current filter value (null if no filter) */
    filterValue: FilterValue;

    /** Callback when filter is applied */
    onFilterChange: (columnName: string, value: FilterValue) => void;

    /** Available options for choice/OptionSet columns */
    choiceOptions?: ChoiceOption[];

    /** Whether the filter popup is open (controlled) */
    open?: boolean;

    /** Callback when popup open state changes */
    onOpenChange?: (open: boolean) => void;
}

const useStyles = makeStyles({
    popoverSurface: {
        padding: tokens.spacingVerticalM,
        minWidth: '280px',
        maxWidth: '350px',
    },
    header: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: tokens.spacingVerticalM,
    },
    filterSection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    operatorDropdown: {
        minWidth: '120px',
    },
    inputRow: {
        display: 'flex',
        gap: tokens.spacingHorizontalS,
        alignItems: 'center',
    },
    buttonRow: {
        display: 'flex',
        gap: tokens.spacingHorizontalS,
        justifyContent: 'flex-end',
        marginTop: tokens.spacingVerticalM,
    },
    triggerButton: {
        minWidth: 'auto',
        padding: '2px',
    },
    activeIndicator: {
        position: 'absolute' as const,
        top: '-2px',
        right: '-2px',
        width: '8px',
        height: '8px',
        borderRadius: '50%',
        backgroundColor: tokens.colorBrandBackground,
    },
    triggerWrapper: {
        position: 'relative' as const,
        display: 'inline-flex',
    },
});

/**
 * Get filter operators based on data type
 */
function getOperatorsForDataType(dataType: string): { value: string; label: string }[] {
    const normalizedType = dataType.toLowerCase();

    if (normalizedType.includes('date') || normalizedType.includes('time')) {
        return [
            { value: 'equals', label: 'Equals' },
            { value: 'before', label: 'Before' },
            { value: 'after', label: 'After' },
            { value: 'between', label: 'Between' },
        ];
    }

    if (normalizedType.includes('optionset') || normalizedType.includes('picklist') || normalizedType.includes('choice')) {
        return [
            { value: 'equals', label: 'Equals' },
            { value: 'in', label: 'Is one of' },
        ];
    }

    // Default: text operators
    return [
        { value: 'contains', label: 'Contains' },
        { value: 'equals', label: 'Equals' },
        { value: 'startswith', label: 'Starts with' },
        { value: 'endswith', label: 'Ends with' },
    ];
}

/**
 * Determine filter type from data type
 */
function getFilterTypeFromDataType(dataType: string): 'text' | 'choice' | 'date' {
    const normalizedType = dataType.toLowerCase();

    if (normalizedType.includes('date') || normalizedType.includes('time')) {
        return 'date';
    }

    if (normalizedType.includes('optionset') || normalizedType.includes('picklist') || normalizedType.includes('choice')) {
        return 'choice';
    }

    return 'text';
}

/**
 * FilterPopup Component
 *
 * Renders a filter icon button that opens a popover with filter controls.
 * Filter UI adapts based on column data type (text, choice, date).
 */
export const FilterPopup: React.FC<FilterPopupProps> = ({
    columnName,
    columnDisplayName,
    dataType,
    filterValue,
    onFilterChange,
    choiceOptions = [],
    open: controlledOpen,
    onOpenChange,
}) => {
    const styles = useStyles();

    // Local state for popover (uncontrolled mode)
    const [internalOpen, setInternalOpen] = React.useState(false);
    const isControlled = controlledOpen !== undefined;
    const isOpen = isControlled ? controlledOpen : internalOpen;

    // Local state for filter editing (before applying)
    const filterType = getFilterTypeFromDataType(dataType);
    const operators = getOperatorsForDataType(dataType);

    const [localOperator, setLocalOperator] = React.useState<string>(
        filterValue ?
            (filterValue as TextFilterValue | ChoiceFilterValue | DateFilterValue).operator :
            operators[0].value
    );
    const [localTextValue, setLocalTextValue] = React.useState<string>(
        filterValue?.type === 'text' ? filterValue.value : ''
    );
    const [localChoiceValues, setLocalChoiceValues] = React.useState<(string | number)[]>(
        filterValue?.type === 'choice' ? filterValue.values : []
    );
    const [localDateValue, setLocalDateValue] = React.useState<Date | undefined>(
        filterValue?.type === 'date' ? filterValue.value : undefined
    );
    const [localDateEndValue, setLocalDateEndValue] = React.useState<Date | undefined>(
        filterValue?.type === 'date' ? filterValue.endValue : undefined
    );

    // Reset local state when filterValue prop changes
    React.useEffect(() => {
        if (filterValue?.type === 'text') {
            setLocalOperator(filterValue.operator);
            setLocalTextValue(filterValue.value);
        } else if (filterValue?.type === 'choice') {
            setLocalOperator(filterValue.operator);
            setLocalChoiceValues(filterValue.values);
        } else if (filterValue?.type === 'date') {
            setLocalOperator(filterValue.operator);
            setLocalDateValue(filterValue.value);
            setLocalDateEndValue(filterValue.endValue);
        } else {
            setLocalOperator(operators[0].value);
            setLocalTextValue('');
            setLocalChoiceValues([]);
            setLocalDateValue(undefined);
            setLocalDateEndValue(undefined);
        }
    }, [filterValue, operators]);

    const handleOpenChange = (open: boolean) => {
        if (isControlled && onOpenChange) {
            onOpenChange(open);
        } else {
            setInternalOpen(open);
        }
    };

    const handleApplyFilter = () => {
        let newFilterValue: FilterValue = null;

        if (filterType === 'text' && localTextValue.trim()) {
            newFilterValue = {
                type: 'text',
                operator: localOperator as TextFilterOperator,
                value: localTextValue.trim(),
            };
        } else if (filterType === 'choice' && localChoiceValues.length > 0) {
            newFilterValue = {
                type: 'choice',
                operator: localOperator as ChoiceFilterOperator,
                values: localChoiceValues,
            };
        } else if (filterType === 'date' && localDateValue) {
            newFilterValue = {
                type: 'date',
                operator: localOperator as DateFilterOperator,
                value: localDateValue,
                endValue: localOperator === 'between' ? localDateEndValue : undefined,
            };
        }

        onFilterChange(columnName, newFilterValue);
        handleOpenChange(false);
    };

    const handleClearFilter = () => {
        setLocalOperator(operators[0].value);
        setLocalTextValue('');
        setLocalChoiceValues([]);
        setLocalDateValue(undefined);
        setLocalDateEndValue(undefined);
        onFilterChange(columnName, null);
        handleOpenChange(false);
    };

    const hasActiveFilter = filterValue !== null;

    // Render filter input based on type
    const renderFilterInput = () => {
        if (filterType === 'text') {
            return (
                <Input
                    placeholder={`Filter ${columnDisplayName}...`}
                    value={localTextValue}
                    onChange={(e, data) => setLocalTextValue(data.value)}
                    style={{ width: '100%' }}
                />
            );
        }

        if (filterType === 'choice') {
            return (
                <Dropdown
                    placeholder={`Select ${columnDisplayName}...`}
                    multiselect={localOperator === 'in'}
                    selectedOptions={localChoiceValues.map(String)}
                    onOptionSelect={(e, data) => {
                        if (localOperator === 'in') {
                            // Multiselect: toggle selection
                            const selectedValues = data.selectedOptions.map((opt: string) => {
                                // Try to parse as number if original was number
                                const numVal = parseInt(opt, 10);
                                return !isNaN(numVal) ? numVal : opt;
                            });
                            setLocalChoiceValues(selectedValues);
                        } else {
                            // Single select
                            const numVal = parseInt(data.optionValue || '', 10);
                            setLocalChoiceValues([!isNaN(numVal) ? numVal : data.optionValue || '']);
                        }
                    }}
                    style={{ width: '100%' }}
                >
                    {choiceOptions.map((opt) => (
                        <Option key={String(opt.value)} value={String(opt.value)}>
                            {opt.label}
                        </Option>
                    ))}
                </Dropdown>
            );
        }

        if (filterType === 'date') {
            // Format date for input type="date" (YYYY-MM-DD)
            const formatDateForInput = (date: Date | undefined): string => {
                if (!date) return '';
                return date.toISOString().split('T')[0];
            };

            // Parse date from input value
            const parseDateFromInput = (value: string): Date | undefined => {
                if (!value) return undefined;
                const date = new Date(value + 'T00:00:00');
                return isNaN(date.getTime()) ? undefined : date;
            };

            return (
                <div className={styles.filterSection}>
                    <Field label="Date">
                        <Input
                            type="date"
                            value={formatDateForInput(localDateValue)}
                            onChange={(_e, data) => setLocalDateValue(parseDateFromInput(data.value))}
                            style={{ width: '100%' }}
                        />
                    </Field>
                    {localOperator === 'between' && (
                        <Field label="End Date">
                            <Input
                                type="date"
                                value={formatDateForInput(localDateEndValue)}
                                onChange={(_e, data) => setLocalDateEndValue(parseDateFromInput(data.value))}
                                style={{ width: '100%' }}
                            />
                        </Field>
                    )}
                </div>
            );
        }

        return null;
    };

    return (
        <Popover
            open={isOpen}
            onOpenChange={(e, data) => handleOpenChange(data.open)}
            positioning="below-start"
        >
            <PopoverTrigger disableButtonEnhancement>
                <span className={styles.triggerWrapper}>
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={hasActiveFilter ? <Filter20Filled /> : <Filter20Regular />}
                        className={styles.triggerButton}
                        aria-label={`Filter ${columnDisplayName}`}
                        title={hasActiveFilter ? `Filter active on ${columnDisplayName}` : `Filter ${columnDisplayName}`}
                    />
                    {hasActiveFilter && <span className={styles.activeIndicator} />}
                </span>
            </PopoverTrigger>
            <PopoverSurface className={styles.popoverSurface}>
                {/* Header */}
                <div className={styles.header}>
                    <Text weight="semibold">Filter: {columnDisplayName}</Text>
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<Dismiss20Regular />}
                        onClick={() => handleOpenChange(false)}
                        aria-label="Close filter"
                    />
                </div>

                <Divider />

                {/* Filter controls */}
                <div className={styles.filterSection} style={{ marginTop: tokens.spacingVerticalM }}>
                    {/* Operator selector */}
                    <Dropdown
                        className={styles.operatorDropdown}
                        value={operators.find(op => op.value === localOperator)?.label || ''}
                        selectedOptions={[localOperator]}
                        onOptionSelect={(e, data) => setLocalOperator(data.optionValue || operators[0].value)}
                        style={{ width: '100%' }}
                    >
                        {operators.map((op) => (
                            <Option key={op.value} value={op.value}>
                                {op.label}
                            </Option>
                        ))}
                    </Dropdown>

                    {/* Filter value input */}
                    {renderFilterInput()}
                </div>

                {/* Action buttons */}
                <div className={styles.buttonRow}>
                    <Button
                        appearance="secondary"
                        onClick={handleClearFilter}
                        disabled={!hasActiveFilter}
                    >
                        Clear
                    </Button>
                    <Button
                        appearance="primary"
                        onClick={handleApplyFilter}
                    >
                        Apply
                    </Button>
                </div>
            </PopoverSurface>
        </Popover>
    );
};
