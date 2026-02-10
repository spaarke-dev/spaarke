/**
 * ErrorBoundary Component
 *
 * Catches React errors and displays a user-friendly error message.
 * Prevents the entire PCF control from crashing on component errors.
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    shorthands
} from "@fluentui/react-components";
import { ErrorCircle20Regular } from "@fluentui/react-icons";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        ...shorthands.padding(tokens.spacingVerticalL),
        backgroundColor: tokens.colorStatusDangerBackground1,
        ...shorthands.borderRadius(tokens.borderRadiusMedium)
    },
    icon: {
        color: tokens.colorStatusDangerForeground1,
        marginBottom: tokens.spacingVerticalS
    },
    title: {
        color: tokens.colorStatusDangerForeground1,
        fontWeight: tokens.fontWeightSemibold,
        marginBottom: tokens.spacingVerticalS
    },
    message: {
        color: tokens.colorNeutralForeground1,
        textAlign: "center",
        marginBottom: tokens.spacingVerticalM
    }
});

interface IErrorBoundaryState {
    hasError: boolean;
    error?: Error;
}

interface IErrorBoundaryProps {
    children: React.ReactNode;
}

export class ErrorBoundary extends React.Component<IErrorBoundaryProps, IErrorBoundaryState> {
    constructor(props: IErrorBoundaryProps) {
        super(props);
        this.state = { hasError: false };
    }

    static getDerivedStateFromError(error: Error): IErrorBoundaryState {
        return { hasError: true, error };
    }

    componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
        // Log error for debugging
        console.error("[DueDatesWidget] Component error:", error, errorInfo);
    }

    handleRetry = (): void => {
        this.setState({ hasError: false, error: undefined });
    };

    render(): React.ReactNode {
        if (this.state.hasError) {
            return <ErrorFallback onRetry={this.handleRetry} />;
        }

        return this.props.children;
    }
}

interface IErrorFallbackProps {
    onRetry: () => void;
}

const ErrorFallback: React.FC<IErrorFallbackProps> = ({ onRetry }) => {
    const styles = useStyles();

    return (
        <div className={styles.container}>
            <ErrorCircle20Regular className={styles.icon} />
            <Text className={styles.title}>Something went wrong</Text>
            <Text className={styles.message} size={200}>
                The widget encountered an error. Please try again.
            </Text>
            <Button appearance="primary" onClick={onRetry}>
                Retry
            </Button>
        </div>
    );
};
