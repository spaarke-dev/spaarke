/**
 * Visual Host Root Component
 * Main React component for the Visual Host PCF control
 */

import * as React from "react";
import { useState, useEffect, useCallback } from "react";
import {
  Spinner,
  MessageBar,
  MessageBarBody,
  Button,
  Tooltip,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { OpenRegular } from "@fluentui/react-icons";
import { IInputs } from "../generated/ManifestTypes";
import { IChartDefinition, IChartData, DrillInteraction } from "../types";
import { ChartRenderer } from "./ChartRenderer";
import { logger } from "../utils/logger";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    minHeight: "200px",
    padding: tokens.spacingVerticalM,
    boxSizing: "border-box",
  },
  toolbar: {
    display: "flex",
    justifyContent: "flex-end",
    marginBottom: tokens.spacingVerticalS,
  },
  chartContainer: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "150px",
  },
  versionFooter: {
    display: "flex",
    justifyContent: "flex-end",
    paddingTop: tokens.spacingVerticalXS,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
});

interface IVisualHostRootProps {
  context: ComponentFramework.Context<IInputs>;
  notifyOutputChanged: () => void;
}

/**
 * Visual Host Root - Main component that loads chart definition and renders the appropriate visual
 */
export const VisualHostRoot: React.FC<IVisualHostRootProps> = ({
  context,
  notifyOutputChanged,
}) => {
  const styles = useStyles();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chartDefinition, setChartDefinition] = useState<IChartDefinition | null>(null);
  const [chartData, setChartData] = useState<IChartData | null>(null);

  // v1.1.0: Hybrid chart definition resolution
  // Priority: 1) Lookup binding, 2) Static ID property
  const lookupValue = context.parameters.chartDefinition?.raw;
  const lookupId = lookupValue?.[0]?.id;
  const staticId = context.parameters.chartDefinitionId?.raw?.trim();
  const chartDefinitionId = lookupId || staticId || null;

  // Log resolution source for debugging
  const resolutionSource = lookupId ? "lookup" : staticId ? "static" : "none";

  // v1.1.0: Context filtering parameters
  const contextFieldName = context.parameters.contextFieldName?.raw?.trim() || null;
  // Get current record ID from context for related record filtering
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const contextRecordId = (context.mode as any).contextInfo?.entityId || null;

  const showToolbar = context.parameters.showToolbar?.raw !== false;
  const enableDrillThrough = context.parameters.enableDrillThrough?.raw !== false;
  const height = context.parameters.height?.raw;

  useEffect(() => {
    if (!chartDefinitionId) {
      setIsLoading(false);
      setError(null);
      setChartDefinition(null);
      logger.info("VisualHostRoot", "No chart definition configured");
      return;
    }

    loadChartDefinition(chartDefinitionId, resolutionSource);
  }, [chartDefinitionId, resolutionSource, contextFieldName, contextRecordId]);

  /**
   * Load chart definition from Dataverse
   * @param id - Chart definition GUID
   * @param source - Resolution source for logging ("lookup" | "static" | "none")
   */
  const loadChartDefinition = async (id: string, source: string) => {
    try {
      setIsLoading(true);
      setError(null);

      logger.info("VisualHostRoot", `Loading chart definition: ${id} (source: ${source})`, {
        contextFieldName,
        contextRecordId,
      });

      // TODO: Implement actual Dataverse query in task 021
      // For now, show placeholder
      await new Promise((resolve) => setTimeout(resolve, 500));

      // Placeholder - will be replaced with actual data fetch
      setChartDefinition(null);
      setError("Chart definition loading not yet implemented. Complete task 021.");
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      logger.error("VisualHostRoot", "Failed to load chart definition", err);
      setError(`Failed to load chart: ${errorMessage}`);
    } finally {
      setIsLoading(false);
    }
  };

  /**
   * Handle drill interaction from chart components
   */
  const handleDrillInteraction = useCallback(
    (interaction: DrillInteraction) => {
      if (!enableDrillThrough) return;

      logger.info("VisualHostRoot", "Drill interaction", interaction);

      // TODO: In task 030, navigate to drill-through Custom Page
      // For now, log the interaction for debugging
      console.log("Drill interaction:", interaction);

      // Notify PCF that output has changed (if we add drill output parameter)
      notifyOutputChanged();
    },
    [enableDrillThrough, notifyOutputChanged]
  );

  /**
   * Handle expand button click - opens drill-through workspace Custom Page
   */
  const handleExpandClick = useCallback(async () => {
    logger.info("VisualHostRoot", "Expand clicked", { chartDefinitionId });

    if (!chartDefinitionId) {
      logger.warn("VisualHostRoot", "No chart definition ID to expand");
      return;
    }

    try {
      // Access Xrm from global scope
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm;

      if (!xrm?.Navigation?.navigateTo) {
        logger.warn("VisualHostRoot", "Xrm.Navigation not available");
        console.log("Expand clicked for chart:", chartDefinitionId);
        return;
      }

      // Open the drill-through workspace as a dialog
      // Custom Page name: sprk_drillthroughworkspace
      await xrm.Navigation.navigateTo(
        {
          pageType: "custom",
          name: "sprk_drillthroughworkspace",
          recordId: chartDefinitionId,
        },
        {
          target: 2, // Dialog
          position: 1, // Center
          width: { value: 90, unit: "%" },
          height: { value: 85, unit: "%" },
        }
      );

      logger.info("VisualHostRoot", "Drill-through workspace opened");
    } catch (err) {
      logger.error("VisualHostRoot", "Failed to open drill-through workspace", err);
      // Fallback: log for debugging
      console.log("Expand clicked for chart:", chartDefinitionId);
    }
  }, [chartDefinitionId]);

  /**
   * Render the appropriate visual based on chart definition
   */
  const renderVisual = () => {
    if (!chartDefinition) {
      return (
        <div className={styles.placeholder}>
          <Text size={400}>No chart configured</Text>
          <Text size={200}>
            Bind to a lookup column or set Chart Definition ID property
          </Text>
        </div>
      );
    }

    return (
      <ChartRenderer
        chartDefinition={chartDefinition}
        chartData={chartData || undefined}
        onDrillInteraction={enableDrillThrough ? handleDrillInteraction : undefined}
        height={height || 300}
      />
    );
  };

  // Container style with optional height
  const containerStyle: React.CSSProperties = height
    ? { height: `${height}px` }
    : {};

  return (
    <div className={styles.container} style={containerStyle}>
      {/* Toolbar area with expand button */}
      {showToolbar && (
        <div className={styles.toolbar}>
          {enableDrillThrough && chartDefinition && (
            <Tooltip content="View details" relationship="label">
              <Button
                appearance="subtle"
                icon={<OpenRegular />}
                onClick={handleExpandClick}
                aria-label="View details in expanded workspace"
              />
            </Tooltip>
          )}
        </div>
      )}

      {/* Main chart area */}
      <div className={styles.chartContainer}>
        {isLoading ? (
          <Spinner label="Loading chart..." />
        ) : error ? (
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        ) : (
          renderVisual()
        )}
      </div>

      {/* Version footer - MANDATORY per CLAUDE.md */}
      <div className={styles.versionFooter}>
        <Text size={100}>v1.1.0 • Built 2025-12-30</Text>
      </div>
    </div>
  );
};
