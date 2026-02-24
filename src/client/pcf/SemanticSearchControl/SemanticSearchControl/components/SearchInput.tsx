/**
 * SearchInput component
 *
 * Provides the search text input with search button and add document button.
 * Supports placeholder configuration and triggers search on button click or Enter key.
 * Empty query is allowed — returns all documents in scope.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useCallback, KeyboardEvent } from "react";
import {
    makeStyles,
    tokens,
    Input,
    Button,
    Spinner,
} from "@fluentui/react-components";
import { Search20Regular, AddRegular } from "@fluentui/react-icons";
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
    searchButton: {
        minWidth: "90px",
    },
});

/**
 * SearchInput component with text input, search button, and add document button.
 *
 * @param props.value - Current search query value
 * @param props.placeholder - Placeholder text for input
 * @param props.disabled - Whether input is disabled (during search)
 * @param props.onValueChange - Callback when input value changes
 * @param props.onSearch - Callback when search is triggered
 * @param props.onAddDocument - Callback when Add Document is clicked
 */
export const SearchInput: React.FC<ISearchInputProps> = ({
    value,
    placeholder,
    disabled,
    onValueChange,
    onSearch,
    onAddDocument,
}) => {
    const styles = useStyles();

    // Handle input change
    const handleInputChange = useCallback(
        (ev: React.ChangeEvent<HTMLInputElement>) => {
            onValueChange(ev.target.value);
        },
        [onValueChange]
    );

    // Handle Enter key press — allow search with empty query
    const handleKeyDown = useCallback(
        (ev: KeyboardEvent<HTMLInputElement>) => {
            if (ev.key === "Enter" && !disabled) {
                onSearch();
            }
        },
        [disabled, onSearch]
    );

    // Handle search button click
    const handleSearchClick = useCallback(() => {
        if (!disabled) {
            onSearch();
        }
    }, [disabled, onSearch]);

    return (
        <div className={styles.container}>
            <Input
                className={styles.input}
                value={value}
                placeholder={placeholder}
                disabled={disabled}
                onChange={handleInputChange}
                onKeyDown={handleKeyDown}
                contentBefore={<Search20Regular />}
                appearance="outline"
                size="medium"
            />
            <Button
                className={styles.searchButton}
                appearance="primary"
                disabled={disabled}
                onClick={handleSearchClick}
                icon={disabled ? <Spinner size="tiny" /> : undefined}
            >
                {disabled ? "Searching..." : "Search"}
            </Button>
            <Button
                appearance="secondary"
                icon={<AddRegular />}
                onClick={onAddDocument}
                disabled={disabled}
            >
                Add Document
            </Button>
        </div>
    );
};

export default SearchInput;
