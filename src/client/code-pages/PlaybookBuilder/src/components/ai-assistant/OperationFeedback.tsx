/**
 * Operation Feedback Component - Real-time AI processing status
 *
 * Shows progress indicators during AI processing:
 * - Spinner animation
 * - Current step/operation text
 * - Operation count
 * - Hides when processing completes
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useState, useRef, useEffect } from "react";
import {
    Spinner,
    Text,
    Badge,
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
} from "@fluentui/react-components";
import { Checkmark12Regular } from "@fluentui/react-icons";
import { useAiAssistantStore } from "../../stores/aiAssistantStore";

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
    },
    containerActive: {
        backgroundColor: tokens.colorBrandBackground2,
        ...shorthands.borderColor(tokens.colorBrandStroke1),
    },
    containerComplete: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
        ...shorthands.borderColor(tokens.colorPaletteGreenBorder1),
    },
    hidden: {
        display: "none",
    },
    spinnerContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "20px",
        height: "20px",
    },
    textContainer: {
        display: "flex",
        flexDirection: "column",
        flex: 1,
        minWidth: 0,
    },
    statusText: {
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        textOverflow: "ellipsis",
        ...shorthands.overflow("hidden"),
    },
    stepText: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        whiteSpace: "nowrap",
        textOverflow: "ellipsis",
        ...shorthands.overflow("hidden"),
    },
    badge: {
        flexShrink: 0,
    },
    completeIcon: {
        color: tokens.colorPaletteGreenForeground1,
    },
});

// ============================================================================
// Types
// ============================================================================

export interface OperationFeedbackProps {
    /** Override visibility (default: auto based on streaming state) */
    visible?: boolean;
    /** Custom status text (default: from streamingState) */
    statusText?: string;
    /** Custom step text (default: from streamingState.currentStep) */
    stepText?: string;
    /** Show operation count badge (default: true) */
    showBadge?: boolean;
    /** Show completed state briefly before hiding (default: true) */
    showCompleteState?: boolean;
    /** Duration to show complete state in ms (default: 1500) */
    completeDuration?: number;
    /** Compact mode - single line (default: false) */
    compact?: boolean;
}

// ============================================================================
// Component
// ============================================================================

export const OperationFeedback: React.FC<OperationFeedbackProps> = ({
    visible,
    statusText,
    stepText,
    showBadge = true,
    showCompleteState = true,
    completeDuration = 1500,
    compact = false,
}) => {
    const styles = useStyles();
    const [showComplete, setShowComplete] = useState(false);
    const prevStreamingRef = useRef(false);

    // Store state
    const { isStreaming, streamingState } = useAiAssistantStore();

    // Detect transition from streaming to not streaming
    useEffect(() => {
        if (prevStreamingRef.current && !isStreaming && showCompleteState) {
            setShowComplete(true);
            const timer = setTimeout(() => {
                setShowComplete(false);
            }, completeDuration);
            return () => clearTimeout(timer);
        }
        prevStreamingRef.current = isStreaming;
    }, [isStreaming, showCompleteState, completeDuration]);

    // Determine visibility
    const isVisible = visible !== undefined ? visible : isStreaming || showComplete;

    // Don't render if not visible
    if (!isVisible) {
        return null;
    }

    // Determine display text
    const displayStatus = statusText ?? (showComplete ? "Complete!" : "Processing...");
    const displayStep =
        stepText ??
        streamingState.currentStep ??
        (showComplete ? `${streamingState.operationCount} changes applied` : "Analyzing your request...");

    return (
        <div
            className={mergeClasses(
                styles.container,
                isStreaming && styles.containerActive,
                showComplete && styles.containerComplete
            )}
            role="status"
            aria-live="polite"
            aria-label="Operation status"
        >
            {/* Spinner or complete icon */}
            <div className={styles.spinnerContainer}>
                {showComplete ? (
                    <Checkmark12Regular className={styles.completeIcon} />
                ) : (
                    <Spinner size="tiny" />
                )}
            </div>

            {/* Status text */}
            <div className={styles.textContainer}>
                <Text className={styles.statusText} size={200} weight="semibold">
                    {displayStatus}
                </Text>
                {!compact && (
                    <Text className={styles.stepText} size={100}>
                        {displayStep}
                    </Text>
                )}
            </div>

            {/* Operation count badge */}
            {showBadge && streamingState.operationCount > 0 && (
                <Badge
                    appearance="filled"
                    color={showComplete ? "success" : "informative"}
                    size="small"
                    className={styles.badge}
                >
                    {streamingState.operationCount}
                </Badge>
            )}
        </div>
    );
};

export default OperationFeedback;
