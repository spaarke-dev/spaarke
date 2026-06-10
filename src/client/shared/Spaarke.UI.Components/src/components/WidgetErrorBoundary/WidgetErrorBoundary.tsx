import * as React from "react";
import {
  Button,
  Caption1,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import { ArrowClockwise16Regular, ErrorCircle20Regular } from "@fluentui/react-icons";
import { reportClientError } from "../../services/reportClientError";

export interface WidgetErrorBoundaryProps {
  /** Widget type string — e.g. "matters-list", "calendar", "BudgetDashboard". Surfaced in fallback + telemetry. */
  widgetType: string;
  /** Optional display name shown in the fallback header (defaults to widgetType). */
  displayName?: string;
  /** Optional surface name for telemetry — e.g. "SpaarkeAi". */
  surface?: string;
  /** Optional caller-side hook fired once per caught error (in addition to the default telemetry). */
  onError?: (error: Error, errorInfo: React.ErrorInfo) => void;
  /** Optional fallback override. */
  fallback?: (error: Error, resetError: () => void) => React.ReactNode;
  children: React.ReactNode;
}

interface WidgetErrorBoundaryState {
  error: Error | null;
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    rowGap: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    ...shorthands.border("1px", "solid", tokens.colorPaletteRedBorder1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorPaletteRedBackground1,
    color: tokens.colorPaletteRedForeground2,
    margin: tokens.spacingVerticalM,
    maxWidth: "560px",
  },
  header: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalS,
  },
  actions: {
    display: "flex",
    columnGap: tokens.spacingHorizontalS,
  },
  message: {
    color: tokens.colorNeutralForeground2,
  },
});

const InlineFallback: React.FC<{
  displayName: string;
  error: Error;
  resetError: () => void;
}> = ({ displayName, error, resetError }) => {
  const styles = useStyles();
  return (
    <div className={styles.root} role="alert" data-testid="widget-error-boundary-fallback">
      <div className={styles.header}>
        <ErrorCircle20Regular />
        <Text weight="semibold">{displayName} failed to load</Text>
      </div>
      <Caption1 className={styles.message}>{error.message || "Unknown error"}</Caption1>
      <div className={styles.actions}>
        <Button
          appearance="primary"
          size="small"
          icon={<ArrowClockwise16Regular />}
          onClick={resetError}
        >
          Retry
        </Button>
      </div>
    </div>
  );
};

/**
 * WidgetErrorBoundary — per-widget React error boundary.
 *
 * Wrap each widget's mount point so a render error in ONE widget doesn't
 * propagate up to AppErrorBoundary and blank the whole surface. The bad
 * widget shows a small inline error card with a Retry button; sibling
 * widgets continue rendering normally.
 *
 * Goes hand-in-hand with AppErrorBoundary (the outer net) and the existing
 * resolveWorkspaceWidget() factory-failure safeguards in
 * @spaarke/ai-widgets/WorkspaceWidgetRegistry (which catch import failures
 * BEFORE render). This boundary catches errors AT and AFTER mount.
 *
 * Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase D.2.
 *
 * @example
 *   import { WidgetErrorBoundary } from '@spaarke/ui-components';
 *
 *   <WidgetErrorBoundary widgetType={tab.widgetType} displayName={tab.displayName} surface="SpaarkeAi">
 *     <Widget data={tab.widgetData} widgetType={tab.widgetType} />
 *   </WidgetErrorBoundary>
 */
export class WidgetErrorBoundary extends React.Component<
  WidgetErrorBoundaryProps,
  WidgetErrorBoundaryState
> {
  state: WidgetErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): WidgetErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    reportClientError(error, {
      scope: "WidgetErrorBoundary",
      surface: this.props.surface,
      widgetType: this.props.widgetType,
      componentStack: errorInfo.componentStack ?? undefined,
    });
    try {
      this.props.onError?.(error, errorInfo);
    } catch (telemetryErr) {
      console.warn(
        `[WidgetErrorBoundary:${this.props.widgetType}] onError callback threw:`,
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
      return (
        <InlineFallback
          displayName={this.props.displayName ?? this.props.widgetType}
          error={this.state.error}
          resetError={this.resetError}
        />
      );
    }
    return this.props.children;
  }
}
