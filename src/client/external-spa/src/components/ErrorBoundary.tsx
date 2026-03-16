import * as React from "react";
import { MessageBar, MessageBarTitle, MessageBarBody, Button, makeStyles, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    padding: tokens.spacingHorizontalXL,
    gap: tokens.spacingVerticalL,
  },
  messageBar: {
    maxWidth: "600px",
    width: "100%",
  },
});

interface ErrorBoundaryProps {
  children: React.ReactNode;
  fallback?: React.ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

/**
 * ErrorBoundary — catches unhandled React rendering errors and displays a
 * friendly fallback UI using Fluent UI v9 MessageBar.
 *
 * Wrap route content or page components to prevent the entire SPA from
 * crashing when an individual page throws during render.
 */
export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  override componentDidCatch(error: Error, info: React.ErrorInfo): void {
    console.error("[SecureProjectWorkspace] Uncaught error:", error, info);
  }

  private handleReset = (): void => {
    this.setState({ hasError: false, error: null });
  };

  override render(): React.ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }
      return <ErrorFallback error={this.state.error} onReset={this.handleReset} />;
    }
    return this.props.children;
  }
}

interface ErrorFallbackProps {
  error: Error | null;
  onReset: () => void;
}

/**
 * Functional fallback component rendered by ErrorBoundary.
 * Separated so it can use hooks (makeStyles).
 */
const ErrorFallback: React.FC<ErrorFallbackProps> = ({ error, onReset }) => {
  const styles = useStyles();

  return (
    <div className={styles.container}>
      <MessageBar className={styles.messageBar} intent="error" layout="multiline">
        <MessageBarBody>
          <MessageBarTitle>Something went wrong</MessageBarTitle>
          {error?.message
            ? `An unexpected error occurred: ${error.message}`
            : "An unexpected error occurred. Please try refreshing the page."}
        </MessageBarBody>
      </MessageBar>
      <Button appearance="primary" onClick={onReset}>
        Try again
      </Button>
    </div>
  );
};

export default ErrorBoundary;
