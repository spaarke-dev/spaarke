/**
 * Typing Indicator Component - AI Response Loading Animation
 *
 * Displays an animated indicator while the AI is generating a response.
 * Uses CSS animations with Fluent UI design tokens for theming support.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
    Text,
} from "@fluentui/react-components";
import { Bot20Regular } from "@fluentui/react-icons";

// ============================================================================
// Types
// ============================================================================

export interface TypingIndicatorProps {
    /** Optional label to show (e.g., "AI is thinking...") */
    label?: string;
    /** Size variant */
    size?: "small" | "medium" | "large";
    /** Custom class name */
    className?: string;
    /** Show bot icon */
    showIcon?: boolean;
}

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    iconContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "28px",
        height: "28px",
        ...shorthands.borderRadius("50%"),
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    dotsContainer: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        ...shorthands.borderRadius(tokens.borderRadiusLarge),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    dot: {
        width: "8px",
        height: "8px",
        ...shorthands.borderRadius("50%"),
        backgroundColor: tokens.colorNeutralForeground3,
        animationName: {
            "0%, 60%, 100%": {
                transform: "scale(1)",
                opacity: 0.4,
            },
            "30%": {
                transform: "scale(1.2)",
                opacity: 1,
            },
        },
        animationDuration: "1.4s",
        animationIterationCount: "infinite",
        animationTimingFunction: "ease-in-out",
    },
    dot1: {
        animationDelay: "0s",
    },
    dot2: {
        animationDelay: "0.2s",
    },
    dot3: {
        animationDelay: "0.4s",
    },
    // Size variants
    small: {
        "& $dot": {
            width: "6px",
            height: "6px",
        },
    },
    large: {
        "& $dot": {
            width: "10px",
            height: "10px",
        },
    },
    label: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontStyle: "italic",
    },
    // Pulse animation for the icon
    iconPulse: {
        animationName: {
            "0%, 100%": {
                transform: "scale(1)",
            },
            "50%": {
                transform: "scale(1.05)",
            },
        },
        animationDuration: "2s",
        animationIterationCount: "infinite",
        animationTimingFunction: "ease-in-out",
    },
});

// ============================================================================
// Component
// ============================================================================

export const TypingIndicator: React.FC<TypingIndicatorProps> = ({
    label,
    size = "medium",
    className,
    showIcon = true,
}) => {
    const styles = useStyles();

    return (
        <div
            className={mergeClasses(styles.container, className)}
            role="status"
            aria-live="polite"
            aria-label={label || "AI is typing"}
        >
            {showIcon && (
                <div className={mergeClasses(styles.iconContainer, styles.iconPulse)}>
                    <Bot20Regular />
                </div>
            )}
            <div className={styles.dotsContainer}>
                <div className={mergeClasses(styles.dot, styles.dot1)} />
                <div className={mergeClasses(styles.dot, styles.dot2)} />
                <div className={mergeClasses(styles.dot, styles.dot3)} />
            </div>
            {label && (
                <Text className={styles.label}>{label}</Text>
            )}
        </div>
    );
};

export default TypingIndicator;
