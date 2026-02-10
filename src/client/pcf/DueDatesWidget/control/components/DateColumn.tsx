/**
 * DateColumn Component
 *
 * Displays the date in a vertical column format:
 * - Large day number (6, 10, 13)
 * - Day abbreviation below (FRI, TUE, SAT)
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 exclusively, design tokens only
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IDateColumnProps {
    /** The date to display */
    date: Date;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Design tokens only, no hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        minWidth: "40px",
        paddingRight: tokens.spacingHorizontalM
    },
    dayNumber: {
        fontSize: tokens.fontSizeHero800,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightHero800,
        color: tokens.colorNeutralForeground1
    },
    dayAbbreviation: {
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightMedium,
        color: tokens.colorNeutralForeground3,
        textTransform: "uppercase",
        letterSpacing: "0.5px"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Get day abbreviation (SUN, MON, TUE, etc.)
 */
const getDayAbbreviation = (date: Date): string => {
    const days = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
    return days[date.getDay()];
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const DateColumn: React.FC<IDateColumnProps> = ({ date }) => {
    const styles = useStyles();

    const dayNumber = date.getDate();
    const dayAbbreviation = getDayAbbreviation(date);

    return (
        <div className={styles.container}>
            <Text className={styles.dayNumber}>{dayNumber}</Text>
            <Text className={styles.dayAbbreviation}>{dayAbbreviation}</Text>
        </div>
    );
};
