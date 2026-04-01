/**
 * ReportViewer.tsx
 * Core Power BI embed component for the Reporting Code Page.
 *
 * Wraps PowerBIEmbed from powerbi-client-react and configures default
 * settings required by the Reporting module:
 *   - Transparent background (dark mode compatible per ADR-021)
 *   - Filter pane hidden in view mode, shown in edit mode
 *   - Nav pane visible
 *   - Responsive layout
 *
 * Exposes the embedded Report object via onReportReady callback so callers
 * can drive token refresh (report.setAccessToken) and mode switching
 * (report.switchMode).
 *
 * @see ADR-021 - Fluent UI v9; design tokens; no hard-coded colors; dark mode
 * @see ADR-012 - Shared components from @spaarke/ui-components
 */

import * as React from "react";
import { PowerBIEmbed } from "powerbi-client-react";
import { models, Report, Embed, service } from "powerbi-client";
import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { IReportEmbedConfiguration } from "powerbi-models";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Embed configuration passed down from the parent (App.tsx / useEmbedToken).
 * Maps directly to IReportEmbedConfiguration fields used by the BFF response.
 */
export interface ReportEmbedConfig {
  /** Power BI report ID (GUID) */
  id: string;
  /** Embed URL from the BFF /api/reporting/embed-token response */
  embedUrl: string;
  /** Short-lived embed access token */
  accessToken: string;
  /** ISO-8601 expiry timestamp from BFF (informational — refresh driven by refreshAfter) */
  expiry?: string;
  /** ISO-8601 timestamp at which the token should be proactively refreshed */
  refreshAfter?: string;
  /** Token type — always Embed (App Owns Data) */
  tokenType?: models.TokenType;
  /** Optional view mode override */
  viewMode?: models.ViewMode;
}

export interface ReportViewerProps {
  /**
   * Embed configuration returned by the BFF embed-token endpoint.
   * Pass null to show the empty/loading placeholder.
   */
  embedConfig: ReportEmbedConfig | null;

  /**
   * When true, opens the report in Edit mode (filter pane visible).
   * Defaults to false (View mode).
   */
  editMode?: boolean;

  /**
   * Called once the PowerBIEmbed iframe fires its "loaded" event.
   * Receives the raw Embed object (cast to Report for type safety).
   */
  onReportLoaded?: (report: Report) => void;

  /**
   * Called when PowerBIEmbed fires its "rendered" event.
   * Useful for performance timing.
   */
  onReportRendered?: () => void;

  /**
   * Called when PowerBIEmbed fires its "error" event.
   */
  onError?: (event: service.ICustomEvent<unknown>) => void;

  /**
   * Called immediately when the embedded component is mounted and the Report
   * reference is available. Use this to store the reference for token refresh
   * and mode switching without waiting for the "loaded" event.
   */
  onReportReady?: (report: Report) => void;

  /**
   * Optional CSS class name to apply to the outer container.
   */
  cssClassName?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /** Fills the entire available height in the main content area */
  container: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    position: "relative",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  /** Spinner overlay shown while loading */
  loadingOverlay: {
    position: "absolute",
    inset: 0,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground1,
    zIndex: 1,
  },
  loadingLabel: {
    color: tokens.colorNeutralForeground2,
  },
  /** Empty state shown when no embedConfig is provided */
  emptyState: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    borderColor: tokens.colorNeutralStroke2,
    borderStyle: "dashed",
    borderWidth: "1px",
  },
  /** The PowerBIEmbed iframe fills the container */
  embedContainer: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
  },
});

// ---------------------------------------------------------------------------
// Default embed settings
// ---------------------------------------------------------------------------

/**
 * Build the IReportEmbedConfiguration for PowerBIEmbed.
 * Merges caller-supplied config with Reporting module defaults.
 */
function buildEmbedConfig(
  config: ReportEmbedConfig,
  editMode: boolean
): IReportEmbedConfiguration {
  return {
    type: "report",
    id: config.id,
    embedUrl: config.embedUrl,
    accessToken: config.accessToken,
    tokenType: config.tokenType ?? models.TokenType.Embed,
    viewMode: editMode ? models.ViewMode.Edit : models.ViewMode.View,
    settings: {
      // Transparent background so Fluent theme bleeds through (ADR-021)
      background: models.BackgroundType.Transparent,
      // Show filter pane only in edit mode
      filterPaneEnabled: editMode,
      // Nav pane always visible
      navContentPaneEnabled: true,
      // Responsive layout adapts to container size
      layoutType: models.LayoutType.Custom,
      customLayout: {
        displayOption: models.DisplayOption.FitToPage,
      },
    },
  };
}

// ---------------------------------------------------------------------------
// ReportViewer
// ---------------------------------------------------------------------------

/**
 * Renders a Power BI report using PowerBIEmbed from powerbi-client-react.
 *
 * Lifecycle:
 *  1. While embedConfig is null  → shows empty placeholder
 *  2. While embedConfig is set   → renders PowerBIEmbed iframe
 *  3. On "loaded" event          → calls onReportLoaded with the Report object
 *  4. On "rendered" event        → calls onReportRendered for timing
 *  5. On "error" event           → calls onError for caller error handling
 *
 * The Report reference is exposed via onReportReady as soon as getEmbeddedComponent
 * fires, so callers can call setAccessToken() for token refresh and switchMode()
 * for view/edit toggling without waiting for the "loaded" event.
 */
export const ReportViewer: React.FC<ReportViewerProps> = ({
  embedConfig,
  editMode = false,
  onReportLoaded,
  onReportRendered,
  onError,
  onReportReady,
  cssClassName,
}) => {
  const styles = useStyles();
  const [isLoaded, setIsLoaded] = React.useState(false);

  // Reset loaded state when embed config changes (new report selected)
  const configRef = React.useRef<ReportEmbedConfig | null>(null);
  React.useEffect(() => {
    if (embedConfig?.id !== configRef.current?.id) {
      setIsLoaded(false);
      configRef.current = embedConfig;
    }
  }, [embedConfig]);

  // ------ Empty state -------------------------------------------------------

  if (!embedConfig) {
    return (
      <div className={`${styles.container} ${cssClassName ?? ""}`}>
        <div className={styles.emptyState} role="region" aria-label="Report area">
          <Text size={200}>Select a report to view it here.</Text>
        </div>
      </div>
    );
  }

  // ------ Event handlers (stable map, rebuilt on config change) -------------

  const eventHandlers = new Map<
    string,
    ((event?: service.ICustomEvent<unknown>, embeddedEntity?: Embed) => void) | null
  >([
    [
      "loaded",
      (_event, embeddedEntity) => {
        setIsLoaded(true);
        if (embeddedEntity && onReportLoaded) {
          onReportLoaded(embeddedEntity as Report);
        }
        console.info("[ReportViewer] Report loaded");
      },
    ],
    [
      "rendered",
      () => {
        console.info("[ReportViewer] Report rendered");
        onReportRendered?.();
      },
    ],
    [
      "error",
      (event) => {
        console.error("[ReportViewer] Embed error:", event?.detail);
        if (event && onError) {
          onError(event as service.ICustomEvent<unknown>);
        }
      },
    ],
  ]);

  // ------ Render ------------------------------------------------------------

  const resolvedEmbedConfig = buildEmbedConfig(embedConfig, editMode);

  return (
    <div className={`${styles.container} ${cssClassName ?? ""}`}>
      {/* Loading overlay — shown until the "loaded" event fires */}
      {!isLoaded && (
        <div className={styles.loadingOverlay} role="status" aria-label="Loading report">
          <Spinner
            size="large"
            label={
              <Text size={300} className={styles.loadingLabel}>
                Loading report…
              </Text>
            }
            labelPosition="below"
          />
        </div>
      )}

      {/* Power BI embed iframe */}
      <div className={styles.embedContainer} aria-hidden={!isLoaded}>
        <PowerBIEmbed
          embedConfig={resolvedEmbedConfig}
          eventHandlers={eventHandlers}
          cssClassName={styles.embedContainer}
          getEmbeddedComponent={(embeddedComponent: Embed) => {
            // Expose the Report reference as soon as it is available
            // so callers can call setAccessToken() / switchMode() immediately
            onReportReady?.(embeddedComponent as Report);
          }}
        />
      </div>
    </div>
  );
};
