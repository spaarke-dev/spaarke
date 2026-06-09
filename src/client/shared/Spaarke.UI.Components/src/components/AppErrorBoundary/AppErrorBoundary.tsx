import * as React from "react";
import {
  Body1,
  Button,
  FluentProvider,
  Subtitle1,
  Text,
  makeStyles,
  shorthands,
  tokens,
  webLightTheme,
} from "@fluentui/react-components";
import { ArrowClockwise20Regular, ErrorCircle24Regular } from "@fluentui/react-icons";

export interface AppErrorBoundaryProps {
  /** Logical name shown to the user when a crash is caught — e.g. "SpaarkeAi", "Daily Briefing". */
  surfaceName: string;
  /** Optional telemetry hook fired once per caught error. */
  onError?: (error: Error, errorInfo: React.ErrorInfo) => void;
  /** Optional fallback override. If provided, AppErrorBoundary will render this instead of the default panel. */
  fallback?: (error: Error, resetError: () => void) => React.ReactNode;
  children: React.ReactNode;
}

interface AppErrorBoundaryState {
  error: Error | null;
}

const useStyles = makeStyles({
  root: {
    minHeight: "100vh",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalXXL),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  panel: {
    maxWidth: "640px",
    width: "100%",
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalM,
    ...shorthands.padding(tokens.spacingVerticalXL, tokens.spacingHorizontalXL),
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground2,
    boxShadow: tokens.shadow8,
  },
  header: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorPaletteRedForeground1,
  },
  message: {
    color: tokens.colorNeutralForeground2,
  },
  details: {
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: "auto",
    maxHeight: "240px",
    whiteSpace: "pre-wrap",
  },
  actions: {
    display: "flex",
    columnGap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalS,
  },
});

const ErrorFallback: React.FC<{
  surfaceName: string;
  error: Error;
  resetError: () => void;
}> = ({ surfaceName, error, resetError }) => {
  const styles = useStyles();
  const [showDetails, setShowDetails] = React.useState(false);

  const handleReload = React.useCallback(() => {
    window.location.reload();
  }, []);

  return (
    <div className={styles.root} role="alert" data-testid="app-error-boundary-fallback">
      <div className={styles.panel}>
        <div className={styles.header}>
          <ErrorCircle24Regular />
          <Subtitle1>{surfaceName} encountered an error</Subtitle1>
        </div>
        <Body1 className={styles.message}>
          The page could not be displayed. Reload to try again. If the problem persists, contact
          support with the message below.
        </Body1>
        <Text className={styles.message} weight="semibold">
          {error.message || "Unknown error"}
        </Text>
        <div className={styles.actions}>
          <Button appearance="primary" icon={<ArrowClockwise20Regular />} onClick={handleReload}>
            Reload page
          </Button>
          <Button appearance="secondary" onClick={resetError}>
            Try again
          </Button>
          <Button appearance="subtle" onClick={() => setShowDetails((v) => !v)}>
            {showDetails ? "Hide details" : "Show details"}
          </Button>
        </div>
        {showDetails && error.stack && <pre className={styles.details}>{error.stack}</pre>}
      </div>
    </div>
  );
};

/**
 * AppErrorBoundary — top-level React error boundary for Spaarke Code Page surfaces.
 *
 * Wrap each surface's root component (typically `<App />` in main.tsx) so a runtime
 * error during rendering — undestructured props (the 2026-06-09 SpaarkeAi blank-page
 * incident), missing modules, type mismatches — renders a graceful fallback instead
 * of blanking the page.
 *
 * Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase B.1.
 *
 * @example
 *   import { AppErrorBoundary } from '@spaarke/ui-components';
 *
 *   root.render(
 *     <AppErrorBoundary surfaceName="SpaarkeAi">
 *       <App />
 *     </AppErrorBoundary>
 *   );
 */
export class AppErrorBoundary extends React.Component<
  AppErrorBoundaryProps,
  AppErrorBoundaryState
> {
  state: AppErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): AppErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    console.error(
      `[AppErrorBoundary:${this.props.surfaceName}] React render error caught:`,
      error,
      errorInfo,
    );
    try {
      this.props.onError?.(error, errorInfo);
    } catch (telemetryErr) {
      console.warn(
        `[AppErrorBoundary:${this.props.surfaceName}] onError callback threw:`,
        telemetryErr,
      );
    }
  }

  resetError = (): void => {
    this.setState({ error: null });
  };

  render(): React.ReactNode {
    if (this.state.error) {
      if (this.props.fallback) {
        return this.props.fallback(this.state.error, this.resetError);
      }
      // Self-contained FluentProvider: the parent tree may have crashed, so we
      // cannot rely on an ancestor providing one. webLightTheme is hardcoded
      // because the boundary may fire before theme resolution completes.
      return (
        <FluentProvider theme={webLightTheme}>
          <ErrorFallback
            surfaceName={this.props.surfaceName}
            error={this.state.error}
            resetError={this.resetError}
          />
        </FluentProvider>
      );
    }
    return this.props.children;
  }
}
