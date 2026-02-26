/**
 * ReAnalysisProgressOverlay - Semi-transparent overlay showing re-analysis progress
 *
 * Renders a centered overlay on top of the editor panel during re-analysis operations.
 * Shows a Fluent UI v9 ProgressBar with percent completion and a status message.
 * The editor content remains visible behind the semi-transparent backdrop.
 *
 * Props-only component: all state management is handled by useReAnalysisProgress hook.
 *
 * Theme support:
 *   - Uses design tokens exclusively (no hard-coded colors)
 *   - Inherits light/dark/high-contrast from FluentProvider context
 *
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + design tokens)
 * @see ADR-012 - Shared Component Library conventions
 */

import {
    makeStyles,
    mergeClasses,
    tokens,
    ProgressBar,
    Text,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ReAnalysisProgressOverlayProps {
    /** Whether the overlay is visible (re-analysis in progress) */
    isVisible: boolean;
    /** Progress percentage (0-100) */
    percent: number;
    /** Human-readable status message (e.g., "Extracting key terms...") */
    message: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    overlay: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        backgroundColor: tokens.colorNeutralBackgroundAlpha2,
        zIndex: 10,
        transition: "opacity 200ms ease-in-out",
        pointerEvents: "auto",
    },
    overlayHidden: {
        opacity: 0,
        pointerEvents: "none",
    },
    overlayVisible: {
        opacity: 1,
    },
    card: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: tokens.spacingVerticalM,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow16,
        paddingTop: tokens.spacingVerticalXL,
        paddingBottom: tokens.spacingVerticalXL,
        paddingLeft: tokens.spacingHorizontalXXL,
        paddingRight: tokens.spacingHorizontalXXL,
        minWidth: "280px",
        maxWidth: "400px",
    },
    title: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    progressContainer: {
        width: "100%",
    },
    percentText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        fontFamily: tokens.fontFamilyMonospace,
        textAlign: "center",
    },
    message: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        textAlign: "center",
        minHeight: "20px",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ReAnalysisProgressOverlay({
    isVisible,
    percent,
    message,
}: ReAnalysisProgressOverlayProps): JSX.Element {
    const styles = useStyles();

    // Clamp percent to 0-100 range
    const clampedPercent = Math.max(0, Math.min(100, percent));
    // ProgressBar value is 0-1
    const progressValue = clampedPercent / 100;

    const overlayClass = mergeClasses(
        styles.overlay,
        isVisible ? styles.overlayVisible : styles.overlayHidden,
    );

    return (
        <div
            className={overlayClass}
            role="status"
            aria-live="polite"
            aria-label={
                isVisible
                    ? `Re-analysis in progress: ${clampedPercent}% complete. ${message}`
                    : undefined
            }
            data-testid="reanalysis-progress-overlay"
        >
            {isVisible && (
                <div className={styles.card}>
                    <Text className={styles.title} size={400}>
                        Re-analyzing Document
                    </Text>

                    <div className={styles.progressContainer}>
                        <ProgressBar
                            value={progressValue}
                            thickness="large"
                            aria-label={`Re-analysis progress: ${clampedPercent}%`}
                        />
                    </div>

                    <Text className={styles.percentText}>
                        {clampedPercent}%
                    </Text>

                    <Text className={styles.message}>
                        {message || "Processing..."}
                    </Text>
                </div>
            )}
        </div>
    );
}
