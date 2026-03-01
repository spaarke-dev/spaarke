/**
 * Error Display Component - Standardized Error Presentation
 *
 * Displays errors from AI Builder operations with consistent styling,
 * retry functionality, and correlation ID for support.
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useCallback } from "react";
import {
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
    MessageBarActions,
    Button,
    Text,
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
} from "@fluentui/react-components";
import {
    ErrorCircle20Regular,
    Warning20Regular,
    Info20Regular,
    ArrowClockwise20Regular,
    Copy20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";

// ============================================================================
// Types
// ============================================================================

export type ErrorSeverity = "error" | "warning" | "info";

export interface AiBuilderError {
    /** Error code for programmatic handling */
    errorCode: string;
    /** Human-readable error message */
    message: string;
    /** Detailed description (optional) */
    detail?: string;
    /** Correlation ID for support */
    correlationId?: string;
    /** Whether the operation can be retried */
    isRetryable?: boolean;
    /** When the error occurred */
    timestamp?: Date;
    /** Error severity level */
    severity?: ErrorSeverity;
}

export interface ErrorDisplayProps {
    /** The error to display */
    error: AiBuilderError;
    /** Called when user clicks retry (if retryable) */
    onRetry?: () => void;
    /** Called when user dismisses the error */
    onDismiss?: () => void;
    /** Whether a retry is in progress */
    isRetrying?: boolean;
    /** Custom class name */
    className?: string;
    /** Show compact version (no detail) */
    compact?: boolean;
}

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
    },
    messageBar: {
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
    },
    detail: {
        marginTop: tokens.spacingVerticalXS,
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200,
    },
    metadata: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalM),
        marginTop: tokens.spacingVerticalXS,
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    correlationId: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
        cursor: "pointer",
        ":hover": {
            color: tokens.colorNeutralForeground2,
        },
    },
    copyIcon: {
        fontSize: tokens.fontSizeBase200,
    },
    actions: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    inlineError: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        backgroundColor: tokens.colorPaletteRedBackground1,
        color: tokens.colorPaletteRedForeground1,
    },
    inlineWarning: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
        color: tokens.colorPaletteYellowForeground1,
    },
    inlineInfo: {
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground2,
    },
});

// ============================================================================
// Error Code Mapping
// ============================================================================

const ERROR_TITLES: Record<string, string> = {
    AI_BUILDER_VALIDATION: "Validation Error",
    AI_BUILDER_NODE_CONFIG: "Node Configuration Error",
    AI_BUILDER_SCOPE_RESOLUTION: "Scope Resolution Error",
    AI_BUILDER_TIMEOUT: "Operation Timeout",
    AI_BUILDER_SERVICE_FAILURE: "AI Service Error",
    AI_BUILDER_STREAMING: "Connection Error",
    AI_BUILDER_PLAYBOOK_NOT_FOUND: "Playbook Not Found",
    AI_BUILDER_OWNERSHIP: "Permission Denied",
    AI_BUILDER_INTERNAL: "Internal Error",
};

const getErrorTitle = (errorCode: string): string => {
    return ERROR_TITLES[errorCode] || "Error";
};

// ============================================================================
// Component
// ============================================================================

export const ErrorDisplay: React.FC<ErrorDisplayProps> = ({
    error,
    onRetry,
    onDismiss,
    isRetrying = false,
    className,
    compact = false,
}) => {
    const styles = useStyles();

    const handleCopyCorrelationId = useCallback(async () => {
        if (error.correlationId) {
            try {
                await navigator.clipboard.writeText(error.correlationId);
            } catch {
                console.warn("Failed to copy correlation ID to clipboard");
            }
        }
    }, [error.correlationId]);

    const severity = error.severity || "error";
    const title = getErrorTitle(error.errorCode);
    const intent = severity === "error" ? "error" : severity === "warning" ? "warning" : "info";

    // Compact inline version for chat messages
    if (compact) {
        return (
            <div
                className={mergeClasses(
                    styles.inlineError,
                    severity === "warning" && styles.inlineWarning,
                    severity === "info" && styles.inlineInfo,
                    className
                )}
                role="alert"
                aria-live="polite"
            >
                {severity === "error" && <ErrorCircle20Regular />}
                {severity === "warning" && <Warning20Regular />}
                {severity === "info" && <Info20Regular />}
                <Text size={200}>{error.message}</Text>
                {error.isRetryable && onRetry && (
                    <Button
                        appearance="transparent"
                        size="small"
                        icon={<ArrowClockwise20Regular />}
                        onClick={onRetry}
                        disabled={isRetrying}
                        aria-label="Retry"
                    />
                )}
            </div>
        );
    }

    // Full MessageBar version
    return (
        <div className={mergeClasses(styles.container, className)}>
            <MessageBar
                intent={intent}
                className={styles.messageBar}
                role="alert"
                aria-live="polite"
            >
                <MessageBarBody>
                    <MessageBarTitle>{title}</MessageBarTitle>
                    <Text>{error.message}</Text>

                    {error.detail && (
                        <Text className={styles.detail}>{error.detail}</Text>
                    )}

                    {(error.correlationId || error.timestamp) && (
                        <div className={styles.metadata}>
                            {error.correlationId && (
                                <span
                                    className={styles.correlationId}
                                    onClick={handleCopyCorrelationId}
                                    role="button"
                                    tabIndex={0}
                                    aria-label={`Copy correlation ID: ${error.correlationId}`}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter" || e.key === " ") {
                                            handleCopyCorrelationId();
                                        }
                                    }}
                                >
                                    <Copy20Regular className={styles.copyIcon} />
                                    <Text size={100}>ID: {error.correlationId.slice(0, 8)}...</Text>
                                </span>
                            )}
                            {error.timestamp && (
                                <Text size={100}>
                                    {error.timestamp.toLocaleTimeString()}
                                </Text>
                            )}
                        </div>
                    )}
                </MessageBarBody>

                <MessageBarActions>
                    <div className={styles.actions}>
                        {error.isRetryable && onRetry && (
                            <Button
                                appearance="transparent"
                                size="small"
                                icon={<ArrowClockwise20Regular />}
                                onClick={onRetry}
                                disabled={isRetrying}
                            >
                                {isRetrying ? "Retrying..." : "Retry"}
                            </Button>
                        )}
                        {onDismiss && (
                            <Button
                                appearance="transparent"
                                size="small"
                                icon={<Dismiss20Regular />}
                                onClick={onDismiss}
                                aria-label="Dismiss"
                            />
                        )}
                    </div>
                </MessageBarActions>
            </MessageBar>
        </div>
    );
};

export default ErrorDisplay;
