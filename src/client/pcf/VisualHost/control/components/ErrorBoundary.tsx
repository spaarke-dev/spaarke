/**
 * Error Boundary Component
 * Catches React errors and displays user-friendly error message
 */

import * as React from "react";
import {
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { logger } from "../utils/logger";

const useStyles = makeStyles({
  container: {
    padding: tokens.spacingVerticalL,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "200px",
    gap: tokens.spacingVerticalM,
  },
});

interface IErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

interface IErrorBoundaryProps {
  children: React.ReactNode;
}

export class ErrorBoundary extends React.Component<
  IErrorBoundaryProps,
  IErrorBoundaryState
> {
  constructor(props: IErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): IErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    logger.error("ErrorBoundary", "Caught error", {
      error: error.message,
      stack: error.stack,
      componentStack: errorInfo.componentStack,
    });
  }

  handleRetry = (): void => {
    this.setState({ hasError: false, error: null });
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
      <MessageBar intent="error">
        <MessageBarBody>
          <MessageBarTitle>Something went wrong</MessageBarTitle>
          An error occurred while rendering the chart. Please try again or
          contact your administrator if the problem persists.
        </MessageBarBody>
      </MessageBar>
      <Button appearance="primary" onClick={onRetry}>
        Try Again
      </Button>
    </div>
  );
};
