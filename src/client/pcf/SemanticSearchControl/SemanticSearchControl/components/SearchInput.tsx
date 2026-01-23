/**
 * SearchInput component
 *
 * Provides the search text input with search button for semantic document search.
 * Supports placeholder configuration and triggers search on button click or Enter key.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useState, useCallback, KeyboardEvent } from "react";
import {
    makeStyles,
    tokens,
    Input,
    Button,
    Spinner,
} from "@fluentui/react-components";
import { Search20Regular } from "@fluentui/react-icons";
import { ISearchInputProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        gap: tokens.spacingHorizontalS,
        alignItems: "center",
    },
    input: {
        flex: 1,
        minWidth: "200px",
    },
    button: {
        minWidth: "100px",
    },
});

/**
 * SearchInput component with text input and search button.
 *
 * @param props.value - Current search query value
 * @param props.placeholder - Placeholder text for input
 * @param props.disabled - Whether input is disabled (during search)
 * @param props.onValueChange - Callback when input value changes
 * @param props.onSearch - Callback when search is triggered
 */
export const SearchInput: React.FC<ISearchInputProps> = ({
    value,
    placeholder,
    disabled,
    onValueChange,
    onSearch,
}) => {
    const styles = useStyles();
    const [localValue, setLocalValue] = useState(value);

    // Sync local value with prop value
    React.useEffect(() => {
        setLocalValue(value);
    }, [value]);

    // Handle input change
    const handleInputChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            const newValue = ev.target.value;
            setLocalValue(newValue);
            onValueChange(newValue);
        },
        [onValueChange]
    );

    // Handle Enter key press
    const handleKeyDown = useCallback(
        (ev: KeyboardEvent<HTMLInputElement>) => {
            if (ev.key === "Enter" && !disabled && localValue.trim()) {
                onSearch();
            }
        },
        [disabled, localValue, onSearch]
    );

    // Handle search button click
    const handleSearchClick = useCallback(() => {
        if (!disabled && localValue.trim()) {
            onSearch();
        }
    }, [disabled, localValue, onSearch]);

    return (
        <div className={styles.container}>
            <Input
                className={styles.input}
                value={localValue}
                placeholder={placeholder}
                disabled={disabled}
                onChange={handleInputChange}
                onKeyDown={handleKeyDown}
                contentBefore={<Search20Regular />}
                appearance="outline"
                size="medium"
            />
            <Button
                className={styles.button}
                appearance="primary"
                disabled={disabled || !localValue.trim()}
                onClick={handleSearchClick}
                icon={disabled ? <Spinner size="tiny" /> : undefined}
            >
                {disabled ? "Searching..." : "Search"}
            </Button>
        </div>
    );
};

export default SearchInput;
