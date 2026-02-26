/**
 * StatusBar — Bottom bar showing search metadata and version
 *
 * Displays: result count, search time, and app version.
 * Shows "Ready" when no search has been executed yet.
 *
 * @see spec.md — Status bar layout
 */

import React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";

// =============================================
// Props
// =============================================

export interface StatusBarProps {
    /** Total result count. Null = no search run yet. */
    totalCount: number | null;
    /** Last search execution time in milliseconds. */
    searchTime: number | null;
    /** App version string. */
    version: string;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    bar: {
        display: "flex",
        alignItems: "center",
        width: "100%",
        height: "28px",
        minHeight: "28px",
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        gap: tokens.spacingHorizontalS,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground3,
    },
    text: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    separator: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground4,
    },
});

// =============================================
// Component
// =============================================

export const StatusBar: React.FC<StatusBarProps> = ({
    totalCount,
    searchTime,
    version,
}) => {
    const styles = useStyles();

    const getStatusText = (): string => {
        if (totalCount === null) return "Ready";
        if (totalCount === 0) return "No results found";
        return `${totalCount} result${totalCount !== 1 ? "s" : ""} found`;
    };

    return (
        <div className={styles.bar} role="status" aria-live="polite">
            <Text className={styles.text}>{getStatusText()}</Text>
            {searchTime !== null && totalCount !== null && totalCount > 0 && (
                <>
                    <Text className={styles.separator}>&middot;</Text>
                    <Text className={styles.text}>{searchTime}ms</Text>
                </>
            )}
            <div style={{ flex: 1 }} />
            <Text className={styles.text}>{version}</Text>
        </div>
    );
};

export default StatusBar;
