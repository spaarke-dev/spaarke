/**
 * FilterDropdown component
 *
 * Reusable multi-select dropdown for filter options.
 * Supports Document Type, Matter Type, and File Type filters.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useMemo, useCallback } from "react";
import {
    makeStyles,
    tokens,
    Dropdown,
    Option,
    Label,
    Spinner,
    useId,
} from "@fluentui/react-components";
import { IFilterDropdownProps, FilterOption } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        minWidth: "150px",
    },
    label: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
    },
    dropdown: {
        width: "100%",
    },
    loading: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
});

/**
 * FilterDropdown component for filter selection.
 *
 * @param props.label - Label text for the dropdown
 * @param props.options - Available filter options
 * @param props.selectedKeys - Currently selected option keys
 * @param props.onSelectionChange - Callback when selection changes
 * @param props.disabled - Whether the dropdown is disabled
 * @param props.multiSelect - Whether to allow multiple selections (default: true)
 */
export const FilterDropdown: React.FC<IFilterDropdownProps> = ({
    label,
    options,
    selectedKeys,
    onSelectionChange,
    disabled,
    multiSelect = true,
}) => {
    const styles = useStyles();
    const dropdownId = useId("filter-dropdown");

    // Handle selection change
    const handleOptionSelect = useCallback(
        (
            _ev: React.SyntheticEvent,
            data: { optionValue?: string; selectedOptions: string[] }
        ) => {
            onSelectionChange(data.selectedOptions);
        },
        [onSelectionChange]
    );

    // Format selected value text
    const selectedValue = useMemo(() => {
        if (selectedKeys.length === 0) {
            return "All";
        }
        if (selectedKeys.length === 1) {
            const selectedOption = options.find((o) => o.key === selectedKeys[0]);
            return selectedOption?.label ?? selectedKeys[0];
        }
        return `${selectedKeys.length} selected`;
    }, [selectedKeys, options]);

    // Show loading state when options are empty but not disabled
    const isLoading = options.length === 0 && !disabled;

    if (isLoading) {
        return (
            <div className={styles.container}>
                <Label className={styles.label} htmlFor={dropdownId}>
                    {label}
                </Label>
                <div className={styles.loading}>
                    <Spinner size="tiny" />
                    <span>Loading...</span>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <Label className={styles.label} htmlFor={dropdownId}>
                {label}
            </Label>
            <Dropdown
                id={dropdownId}
                className={styles.dropdown}
                placeholder="All"
                value={selectedValue}
                selectedOptions={selectedKeys}
                onOptionSelect={handleOptionSelect}
                disabled={disabled}
                multiselect={multiSelect}
            >
                {options.map((option) => (
                    <Option key={option.key} value={option.key}>
                        {option.label}
                    </Option>
                ))}
            </Dropdown>
        </div>
    );
};

export default FilterDropdown;
