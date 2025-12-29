/**
 * Visual Host Root Component
 * Main React component for the Visual Host PCF control
 */

import * as React from "react";
import { useState, useEffect } from "react";
import {
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";
import { IChartDefinition, VisualType } from "../types";
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
}) => {
  const styles = useStyles();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chartDefinition, setChartDefinition] = useState<IChartDefinition | null>(null);

  // Get chart definition ID from PCF input
  const chartDefinitionId = context.parameters.chartDefinitionId?.raw;
  const showToolbar = context.parameters.showToolbar?.raw !== false;
  const height = context.parameters.height?.raw;

  useEffect(() => {
    if (!chartDefinitionId) {
      setIsLoading(false);
      setError(null);
      setChartDefinition(null);
      return;
    }

    loadChartDefinition(chartDefinitionId);
  }, [chartDefinitionId]);

  /**
   * Load chart definition from Dataverse
   */
  const loadChartDefinition = async (id: string) => {
    try {
      setIsLoading(true);
      setError(null);

      logger.info("VisualHostRoot", `Loading chart definition: ${id}`);

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
   * Render the appropriate visual based on chart definition
   */
  const renderVisual = () => {
    if (!chartDefinition) {
      return (
        <div className={styles.placeholder}>
          <Text size={400}>No chart configured</Text>
          <Text size={200}>
            Bind this control to a sprk_chartdefinition record
          </Text>
        </div>
      );
    }

    // TODO: Implement visual rendering in tasks 010-016
    switch (chartDefinition.sprk_visualtype) {
      case VisualType.MetricCard:
        return <div>MetricCard - Coming in Task 010</div>;
      case VisualType.BarChart:
        return <div>BarChart - Coming in Task 011</div>;
      case VisualType.LineChart:
        return <div>LineChart - Coming in Task 012</div>;
      case VisualType.DonutChart:
        return <div>DonutChart - Coming in Task 013</div>;
      case VisualType.StatusBar:
        return <div>StatusBar - Coming in Task 014</div>;
      case VisualType.Calendar:
        return <div>Calendar - Coming in Task 015</div>;
      case VisualType.MiniTable:
        return <div>MiniTable - Coming in Task 016</div>;
      default:
        return <div>Unknown visual type</div>;
    }
  };

  // Container style with optional height
  const containerStyle: React.CSSProperties = height
    ? { height: `${height}px` }
    : {};

  return (
    <div className={styles.container} style={containerStyle}>
      {/* Toolbar area - expand button will go here */}
      {showToolbar && (
        <div className={styles.toolbar}>
          {/* TODO: Add expand button in task 030 */}
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
        <Text size={100}>v1.0.0 • Built 2025-12-29</Text>
      </div>
    </div>
  );
};
