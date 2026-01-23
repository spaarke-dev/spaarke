/**
 * ErrorState component
 *
 * Displays a user-friendly error message when search encounters an error.
 * Includes retry functionality.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    MessageBar,
    MessageBarBody,
    MessageBarActions,
} from "@fluentui/react-components";
import { ErrorCircle20Regular, ArrowClockwise20Regular } from "@fluentui/react-icons";
import { IErrorStateProps } from "../types";

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
        color: tokens.colorPaletteRedForeground1,
        marginBottom: tokens.spacingVerticalL,
    },
    heading: {
        marginBottom: tokens.spacingVerticalS,
    },
    message: {
        color: tokens.colorNeutralForeground2,
        marginBottom: tokens.spacingVerticalL,
        maxWidth: "400px",
    },
    retryButton: {
        marginTop: tokens.spacingVerticalS,
    },
    messageBar: {
        maxWidth: "500px",
        width: "100%",
    },
});

// User-friendly error messages mapping
const getErrorMessage = (message: string, retryable: boolean): string => {
    // Network errors
    if (
        message.toLowerCase().includes("network") ||
        message.toLowerCase().includes("failed to fetch") ||
        message.toLowerCase().includes("connection")
    ) {
        return "Unable to connect to the server. Please check your internet connection and try again.";
    }

    // Authentication errors
    if (
        message.toLowerCase().includes("401") ||
        message.toLowerCase().includes("unauthorized") ||
        message.toLowerCase().includes("authentication")
    ) {
        return "Your session has expired. Please sign in again to continue.";
    }

    // Permission errors
    if (
        message.toLowerCase().includes("403") ||
        message.toLowerCase().includes("forbidden") ||
        message.toLowerCase().includes("permission")
    ) {
        return "You don't have permission to access this resource.";
    }

    // Server errors
    if (
        message.toLowerCase().includes("500") ||
        message.toLowerCase().includes("server error") ||
        message.toLowerCase().includes("internal")
    ) {
        return "The server encountered an error. Please try again in a few moments.";
    }

    // Timeout errors
    if (
        message.toLowerCase().includes("timeout") ||
        message.toLowerCase().includes("timed out")
    ) {
        return "The request took too long to complete. Please try again.";
    }

    // Default message
    if (retryable) {
        return "Something went wrong. Please try again.";
    }
    return "An error occurred. If this problem persists, please contact support.";
};

/**
 * ErrorState component for displaying search errors.
 *
 * @param props.message - Error message (internal)
 * @param props.retryable - Whether error can be retried
 * @param props.onRetry - Callback to retry the operation
 */
export const ErrorState: React.FC<IErrorStateProps> = ({
    message,
    retryable,
    onRetry,
}) => {
    const styles = useStyles();
    const userMessage = getErrorMessage(message, retryable);

    return (
        <div className={styles.container}>
            {/* Icon */}
            <ErrorCircle20Regular className={styles.icon} />

            {/* Heading */}
            <Text size={500} weight="semibold" className={styles.heading}>
                Something went wrong
            </Text>

            {/* User-friendly message */}
            <Text className={styles.message}>{userMessage}</Text>

            {/* Retry button (only if retryable) */}
            {retryable && (
                <Button
                    className={styles.retryButton}
                    appearance="primary"
                    icon={<ArrowClockwise20Regular />}
                    onClick={onRetry}
                >
                    Try Again
                </Button>
            )}

            {/* Alternative: MessageBar format for inline errors */}
            {/*
            <MessageBar intent="error" className={styles.messageBar}>
                <MessageBarBody>{userMessage}</MessageBarBody>
                {retryable && (
                    <MessageBarActions>
                        <Button appearance="transparent" onClick={onRetry}>
                            Retry
                        </Button>
                    </MessageBarActions>
                )}
            </MessageBar>
            */}
        </div>
    );
};

export default ErrorState;
