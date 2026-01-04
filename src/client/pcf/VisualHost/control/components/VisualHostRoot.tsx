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
import {
  loadChartDefinition as loadChartDefinitionFromDataverse,
  ConfigurationNotFoundError,
  ConfigurationLoadError,
} from "../services/ConfigurationLoader";
import {
  fetchAndAggregate,
  AggregationError,
} from "../services/DataAggregationService";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    minHeight: "200px",
    padding: tokens.spacingVerticalM,
    paddingBottom: "36px", // Extra space for version badge
    boxSizing: "border-box",
    position: "relative",
  },
  expandButton: {
    position: "absolute",
    top: 0,
    right: 0,
    zIndex: 10,
  },
  chartContainer: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "150px",
  },
  versionBadge: {
    position: "absolute",
    bottom: 0,
    left: "8px",
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    opacity: 0.6,
    pointerEvents: "none",
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

      // Load from Dataverse using ConfigurationLoader service
      const definition = await loadChartDefinitionFromDataverse(
        { webAPI: context.webAPI },
        id
      );

      setChartDefinition(definition);
      logger.info("VisualHostRoot", `Loaded: ${definition.sprk_name}`, {
        visualType: definition.sprk_visualtype,
        entity: definition.sprk_entitylogicalname,
      });

      // Fetch and aggregate data using DataAggregationService
      if (definition.sprk_entitylogicalname) {
        try {
          const data = await fetchAndAggregate(
            { webAPI: context.webAPI },
            definition,
            {
              // v1.1.0: Add context filter if configured
              contextFilter: contextFieldName && contextRecordId
                ? { fieldName: contextFieldName, recordId: contextRecordId }
                : undefined,
            }
          );
          setChartData(data);
          logger.info("VisualHostRoot", `Data loaded: ${data.dataPoints.length} data points from ${data.totalRecords} records`);
        } catch (aggErr) {
          if (aggErr instanceof AggregationError) {
            logger.error("VisualHostRoot", "Data aggregation error", aggErr);
            // Show chart with no data rather than error - data fetch failed but config is valid
            setChartData(null);
          } else {
            throw aggErr;
          }
        }
      } else {
        logger.warn("VisualHostRoot", "No entity configured, skipping data fetch");
        setChartData(null);
      }
    } catch (err) {
      if (err instanceof ConfigurationNotFoundError) {
        logger.warn("VisualHostRoot", `Chart definition not found: ${id}`);
        setError(`Chart definition not found. Please verify the ID is correct.`);
      } else if (err instanceof ConfigurationLoadError) {
        logger.error("VisualHostRoot", "Configuration load error", err);
        setError(`Failed to load chart: ${err.message}`);
      } else {
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        logger.error("VisualHostRoot", "Failed to load chart definition", err);
        setError(`Failed to load chart: ${errorMessage}`);
      }
      setChartDefinition(null);
      setChartData(null);
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
   * Map entity logical name to Custom Page name
   * Each entity has its own drill-through page configured in Power Apps
   *
   * Custom Page Names:
   * - sprk_visualizationdrillthroughworkspace_e48a5: "Visualization Drill Through Documents"
   */
  const getCustomPageName = useCallback((entityLogicalName: string | undefined): string => {
    // Entity-specific Custom Page mapping
    // Update these as you create Custom Pages for each entity
    const pageMapping: Record<string, string> = {
      "sprk_document": "sprk_visualizationdrillthroughworkspace_e48a5", // Visualization Drill Through Documents
      // Future pages (create and add mappings):
      // "sprk_matter": "sprk_drillthrough_matters",
      // "sprk_event": "sprk_drillthrough_events",
      // "sprk_invoice": "sprk_drillthrough_invoices",
    };

    if (entityLogicalName && pageMapping[entityLogicalName]) {
      return pageMapping[entityLogicalName];
    }

    // Fallback: use Documents page as default
    return "sprk_visualizationdrillthroughworkspace_e48a5";
  }, []);

  /**
   * Handle expand button click - opens drill-through workspace Custom Page
   * Passes filter parameters via URL for the Custom Page to apply
   */
  const handleExpandClick = useCallback(async () => {
    logger.info("VisualHostRoot", "Expand clicked", { chartDefinitionId });

    if (!chartDefinitionId || !chartDefinition) {
      logger.warn("VisualHostRoot", "No chart definition to expand");
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

      // Determine which Custom Page to open based on entity
      const customPageName = getCustomPageName(chartDefinition.sprk_entitylogicalname);

      // Build URL parameters for the Custom Page
      // The Custom Page reads these via Param() function in Power Fx
      const params: Record<string, string> = {
        chartDefinitionId: chartDefinitionId,
        chartTitle: chartDefinition.sprk_name || "Drill Through",
      };

      // If the chart has a groupBy field, pass it as the default filter field
      if (chartDefinition.sprk_groupbyfield) {
        params.filterField = chartDefinition.sprk_groupbyfield;
      }

      logger.info("VisualHostRoot", "Opening Custom Page", {
        pageName: customPageName,
        params,
      });

      // Get app context for URL construction
      const globalContext = xrm.Utility?.getGlobalContext?.();
      const appId = globalContext?.getCurrentAppId?.() || "";
      const baseUrl = globalContext?.getClientUrl?.() || window.location.origin;

      // Build the Custom Page URL with parameters
      // Custom Pages read params via Param("filterField"), Param("filterValue"), etc.
      const queryParams = new URLSearchParams({
        pagetype: "custom",
        name: customPageName,
        ...params,
      });

      if (appId) {
        queryParams.set("appid", appId);
      }

      const customPageUrl = `${baseUrl}/main.aspx?${queryParams.toString()}`;

      // Store params in sessionStorage for Custom Page to read
      // Custom Page can read via: JSON.parse(sessionStorage.getItem("drillThroughParams"))
      try {
        sessionStorage.setItem("drillThroughParams", JSON.stringify(params));
      } catch {
        // sessionStorage may be unavailable in some contexts
      }

      // Open Custom Page as modal dialog per spec FR-04
      // Use the pattern from dialog-patterns.md
      logger.info("VisualHostRoot", "Opening as modal dialog", {
        pageName: customPageName,
        recordId: chartDefinitionId,
        params
      });

      await xrm.Navigation.navigateTo(
        {
          pageType: "custom",
          name: customPageName,
          recordId: chartDefinitionId, // Custom Page reads via Param("recordId")
        },
        {
          target: 2, // Dialog
          position: 1, // Center
          width: { value: 90, unit: "%" },
          height: { value: 85, unit: "%" },
        }
      );

      logger.info("VisualHostRoot", "Drill-through modal opened");
    } catch (err) {
      logger.error("VisualHostRoot", "Failed to open drill-through workspace", err);
      // Fallback: log for debugging
      console.log("Expand clicked for chart:", chartDefinitionId, chartDefinition);
    }
  }, [chartDefinitionId, chartDefinition, getCustomPageName]);

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
      {/* Expand button - upper right, aligned with chart title */}
      {showToolbar && enableDrillThrough && chartDefinition && (
        <div className={styles.expandButton}>
          <Tooltip content="View details" relationship="label">
            <Button
              appearance="subtle"
              icon={<OpenRegular />}
              onClick={handleExpandClick}
              aria-label="View details in expanded workspace"
            />
          </Tooltip>
        </div>
      )}

      {/* Version badge - lower left, unobtrusive */}
      <span className={styles.versionBadge}>v1.1.17 • 2026-01-02</span>

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
    </div>
  );
};
