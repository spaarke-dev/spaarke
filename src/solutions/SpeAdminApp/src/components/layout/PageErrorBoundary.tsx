import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from "@fluentui/react-components";
import { ArrowClockwise20Regular } from "@fluentui/react-icons";

/**
 * Catches render-time exceptions from a page and shows what went wrong, instead of letting the
 * error unmount the entire application.
 *
 * ## Why this exists
 *
 * On 2026-08-25 the Audit Log tab rendered a blank white page in dev UAT. The cause was a response
 * shape mismatch — `entries.slice is not a function` — but the *white screen* was a second, separate
 * defect: React's default behaviour when an error escapes render is to unmount the whole tree, and
 * this app had no boundary anywhere. So a single bad field on one of nine screens took down all nine
 * and reported nothing at all — no message, no stack, no page.
 *
 * That is the exact failure shape this project exists to remove, in its purest form: a real failure
 * presented as blankness. An operator seeing it cannot tell a crash from a permission problem from an
 * empty tenant. Fixing only the audit crash would have left the mechanism armed for the next one —
 * and with `vite build` performing no typecheck, the next one is a matter of when.
 *
 * ## Scope
 *
 * Deliberately wraps the routed page only, NOT the shell. The nav rail, BU picker, and config picker
 * stay interactive, so a broken page is one bad tab rather than a dead app — the operator can simply
 * navigate elsewhere. `pageKey` resets the boundary on navigation; without it React keeps the errored
 * state and every subsequent tab would render the fallback too.
 */

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXL,
    maxWidth: "760px",
  },
  detail: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    whiteSpace: "pre-wrap",
    overflowX: "auto",
  },
  actions: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalS,
  },
});

interface PageErrorFallbackProps {
  error: Error;
  onRetry: () => void;
}

const PageErrorFallback: React.FC<PageErrorFallbackProps> = ({ error, onRetry }) => {
  const styles = useStyles();

  return (
    <div className={styles.container} role="alert">
      <MessageBar intent="error">
        <MessageBarBody>
          <MessageBarTitle>This page failed to render</MessageBarTitle>
          The rest of the app is still working — use the navigation to switch pages. This is an error
          in the SPE Admin app itself, not a report about your SharePoint Embedded data.
        </MessageBarBody>
      </MessageBar>

      <Text className={styles.detail}>{error.message || String(error)}</Text>

      <div className={styles.actions}>
        <Button appearance="primary" icon={<ArrowClockwise20Regular />} onClick={onRetry}>
          Try again
        </Button>
        <Button appearance="secondary" onClick={() => window.location.reload()}>
          Reload app
        </Button>
      </div>
    </div>
  );
};

interface PageErrorBoundaryProps {
  /** Changing this resets the boundary — pass the active page id so navigation clears the error. */
  pageKey: string;
  children: React.ReactNode;
}

interface PageErrorBoundaryState {
  error: Error | null;
  /** The pageKey the current error belongs to, so a new page starts clean. */
  erroredOn: string | null;
}

export class PageErrorBoundary extends React.Component<
  PageErrorBoundaryProps,
  PageErrorBoundaryState
> {
  constructor(props: PageErrorBoundaryProps) {
    super(props);
    this.state = { error: null, erroredOn: null };
  }

  static getDerivedStateFromError(error: Error): Partial<PageErrorBoundaryState> {
    return { error };
  }

  static getDerivedStateFromProps(
    props: PageErrorBoundaryProps,
    state: PageErrorBoundaryState
  ): Partial<PageErrorBoundaryState> | null {
    // First render after catching: remember which page it was.
    if (state.error && state.erroredOn === null) {
      return { erroredOn: props.pageKey };
    }
    // Navigated to a different page — clear so the new page gets a real attempt.
    if (state.error && state.erroredOn !== props.pageKey) {
      return { error: null, erroredOn: null };
    }
    return null;
  }

  componentDidCatch(error: Error, info: React.ErrorInfo): void {
    // Keep the stack reachable in the browser console. A blank page with a silent console is
    // undiagnosable; this is the minimum an operator needs to hand us something actionable.
    console.error(
      `[SpeAdmin] Unhandled render error on page "${this.props.pageKey}":`,
      error,
      info.componentStack
    );
  }

  private handleRetry = (): void => {
    this.setState({ error: null, erroredOn: null });
  };

  render(): React.ReactNode {
    if (this.state.error) {
      return <PageErrorFallback error={this.state.error} onRetry={this.handleRetry} />;
    }
    return this.props.children;
  }
}
