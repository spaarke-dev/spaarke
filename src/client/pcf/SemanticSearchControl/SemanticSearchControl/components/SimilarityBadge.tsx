/**
 * SimilarityBadge component
 *
 * Displays the similarity/relevance score with color coding.
 * Green for high (>=80%), yellow for medium (60-79%), gray for low (<60%).
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Badge,
    Tooltip,
} from "@fluentui/react-components";
import { ISimilarityBadgeProps } from "../types";

const useStyles = makeStyles({
    badge: {
        minWidth: "48px",
        justifyContent: "center",
    },
    // High relevance (>=80%) - success/green
    high: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        color: tokens.colorPaletteGreenForeground2,
    },
    // Medium relevance (60-79%) - warning/yellow
    medium: {
        backgroundColor: tokens.colorPaletteYellowBackground2,
        color: tokens.colorPaletteYellowForeground2,
    },
    // Low relevance (<60%) - neutral/gray
    low: {
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground3,
    },
});

/**
 * Get the appropriate style class based on score.
 */
function getScoreClass(
    score: number,
    styles: ReturnType<typeof useStyles>
): string {
    // Score is 0-1, convert to percentage
    const percentage = score * 100;

    if (percentage >= 80) {
        return styles.high;
    }
    if (percentage >= 60) {
        return styles.medium;
    }
    return styles.low;
}

/**
 * Get the appropriate appearance based on score.
 */
function getAppearance(score: number): "filled" | "outline" | "tint" | "ghost" {
    const percentage = score * 100;
    if (percentage >= 80) return "filled";
    if (percentage >= 60) return "filled";
    return "tint";
}

/**
 * SimilarityBadge component for displaying relevance scores.
 *
 * @param props.score - Similarity score (0-1)
 */
export const SimilarityBadge: React.FC<ISimilarityBadgeProps> = ({ score }) => {
    const styles = useStyles();

    // Convert to percentage for display
    const percentage = Math.round(score * 100);
    const scoreClass = getScoreClass(score, styles);

    return (
        <Tooltip
            content="Relevance score"
            relationship="label"
        >
            <Badge
                className={`${styles.badge} ${scoreClass}`}
                appearance={getAppearance(score)}
                size="medium"
            >
                {percentage}%
            </Badge>
        </Tooltip>
    );
};

export default SimilarityBadge;
