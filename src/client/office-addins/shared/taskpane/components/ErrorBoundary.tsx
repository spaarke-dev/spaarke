import React, { Component, ErrorInfo } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Title3,
  Body1,
  Card,
  CardHeader,
} from '@fluentui/react-components';
import { ErrorCircleRegular, ArrowResetRegular } from '@fluentui/react-icons';

/**
 * ErrorBoundary - React error boundary for Office Add-in task pane.
 *
 * Catches JavaScript errors in child components and displays
 * a fallback UI instead of crashing the entire add-in.
 *
 * Uses Fluent UI v9 design tokens per ADR-021.
 */

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    padding: tokens.spacingVerticalXL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  card: {
    maxWidth: '400px',
    width: '100%',
  },
  icon: {
    fontSize: '48px',
    color: tokens.colorPaletteRedForeground1,
    marginBottom: tokens.spacingVerticalM,
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  errorDetails: {
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    overflow: 'auto',
    maxHeight: '150px',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
  },
});

interface ErrorBoundaryProps {
  /** Fallback component to render (optional, uses default if not provided) */
  fallback?: React.ReactNode;
  /** Callback when error occurs */
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
  /** Callback when reset is clicked */
  onReset?: () => void;
  /** Whether to show error details (for development) */
  showDetails?: boolean;
  /** Child components */
  children: React.ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
}

/**
 * Fallback UI component displayed when an error occurs.
 */
const ErrorFallback: React.FC<{
  error: Error | null;
  errorInfo: ErrorInfo | null;
  onReset?: () => void;
  showDetails?: boolean;
}> = ({ error, errorInfo, onReset, showDetails = false }) => {
  // Use the module-level useStyles hook
  const styles = useStyles();

  return (
    <div className={styles.container}>
      <Card className={styles.card}>
        <CardHeader
          image={<ErrorCircleRegular className={styles.icon} />}
          header={<Title3>Something went wrong</Title3>}
        />
        <div className={styles.content}>
          <Body1>
            We encountered an unexpected error. Please try again or contact
            support if the problem persists.
          </Body1>

          {showDetails && error && (
            <details>
              <summary style={{ cursor: 'pointer', color: tokens.colorNeutralForeground3 }}>
                Error details
              </summary>
              <div className={styles.errorDetails}>
                <strong>{error.name}:</strong> {error.message}
                {errorInfo?.componentStack && (
                  <>
                    {'\n\nComponent Stack:\n'}
                    {errorInfo.componentStack}
                  </>
                )}
              </div>
            </details>
          )}

          <div className={styles.actions}>
            {onReset && (
              <Button
                appearance="primary"
                icon={<ArrowResetRegular />}
                onClick={onReset}
              >
                Try again
              </Button>
            )}
            <Button
              appearance="secondary"
              onClick={() => window.location.reload()}
            >
              Reload add-in
            </Button>
          </div>
        </div>
      </Card>
    </div>
  );
};

export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = {
      hasError: false,
      error: null,
      errorInfo: null,
    };
  }

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    this.setState({ errorInfo });

    // Log error for debugging
    console.error('ErrorBoundary caught an error:', error, errorInfo);

    // Call onError callback if provided
    if (this.props.onError) {
      this.props.onError(error, errorInfo);
    }
  }

  handleReset = (): void => {
    this.setState({
      hasError: false,
      error: null,
      errorInfo: null,
    });

    if (this.props.onReset) {
      this.props.onReset();
    }
  };

  render(): React.ReactNode {
    if (this.state.hasError) {
      // Render custom fallback if provided
      if (this.props.fallback) {
        return this.props.fallback;
      }

      // Render default fallback
      return (
        <ErrorFallback
          error={this.state.error}
          errorInfo={this.state.errorInfo}
          onReset={this.handleReset}
          showDetails={this.props.showDetails}
        />
      );
    }

    return this.props.children;
  }
}

/**
 * Hook-based alternative for functional components.
 * Wraps children in an ErrorBoundary.
 */
export const withErrorBoundary = <P extends object>(
  WrappedComponent: React.ComponentType<P>,
  errorBoundaryProps?: Omit<ErrorBoundaryProps, 'children'>
): React.FC<P> => {
  const WithErrorBoundary: React.FC<P> = (props) => (
    <ErrorBoundary {...errorBoundaryProps}>
      <WrappedComponent {...props} />
    </ErrorBoundary>
  );

  WithErrorBoundary.displayName = `withErrorBoundary(${
    WrappedComponent.displayName || WrappedComponent.name || 'Component'
  })`;

  return WithErrorBoundary;
};
