/**
 * EmptyState component
 *
 * Displays a helpful message when no search results are found.
 * Includes query echo and suggestions for improving search.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
} from "@fluentui/react-components";
import { SearchInfo20Regular, Dismiss20Regular } from "@fluentui/react-icons";
import { IEmptyStateProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingHorizontalXXL,
        textAlign: "center" as const,
        minHeight: "200px",
    },
    icon: {
        fontSize: "48px",
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalL,
    },
    heading: {
        marginBottom: tokens.spacingVerticalS,
    },
    queryEcho: {
        color: tokens.colorNeutralForeground2,
        marginBottom: tokens.spacingVerticalL,
    },
    query: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    suggestions: {
        marginBottom: tokens.spacingVerticalL,
        maxWidth: "400px",
    },
    suggestionsList: {
        listStyle: "disc",
        textAlign: "left" as const,
        paddingLeft: tokens.spacingHorizontalL,
        color: tokens.colorNeutralForeground2,
    },
    suggestionItem: {
        marginBottom: tokens.spacingVerticalXS,
    },
    clearButton: {
        marginTop: tokens.spacingVerticalS,
    },
});

/**
 * EmptyState component for no search results.
 *
 * @param props.query - The search query that returned no results
 * @param props.hasFilters - Whether filters are currently applied
 */
export const EmptyState: React.FC<IEmptyStateProps & { onClearFilters?: () => void }> = ({
    query,
    hasFilters,
    onClearFilters,
}) => {
    const styles = useStyles();

    return (
        <div className={styles.container}>
            {/* Icon */}
            <SearchInfo20Regular className={styles.icon} />

            {/* Heading */}
            <Text size={500} weight="semibold" className={styles.heading}>
                No results found
            </Text>

            {/* Query echo */}
            {query && (
                <Text className={styles.queryEcho}>
                    No documents matching{" "}
                    <span className={styles.query}>&ldquo;{query}&rdquo;</span>
                </Text>
            )}

            {/* Suggestions */}
            <div className={styles.suggestions}>
                <Text size={300}>Try the following:</Text>
                <ul className={styles.suggestionsList}>
                    <li className={styles.suggestionItem}>
                        <Text size={200}>Use different or fewer keywords</Text>
                    </li>
                    <li className={styles.suggestionItem}>
                        <Text size={200}>Check spelling of search terms</Text>
                    </li>
                    {hasFilters && (
                        <li className={styles.suggestionItem}>
                            <Text size={200}>Clear filters to broaden search</Text>
                        </li>
                    )}
                    <li className={styles.suggestionItem}>
                        <Text size={200}>Try using more general terms</Text>
                    </li>
                </ul>
            </div>

            {/* Clear filters button */}
            {hasFilters && onClearFilters && (
                <Button
                    className={styles.clearButton}
                    appearance="outline"
                    icon={<Dismiss20Regular />}
                    onClick={onClearFilters}
                >
                    Clear filters
                </Button>
            )}
        </div>
    );
};

export default EmptyState;
