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
import type { MatrixJustification } from "./MetricCardMatrix";
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
import {
  parseFieldPivotConfig,
  fetchAndPivot,
} from "../services/FieldPivotService";
import {
  executeClickAction,
  hasClickAction,
} from "../services/ClickActionHandler";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    height: "100%",
    minWidth: 0, // Allow shrinking below intrinsic content width
    padding: "2px",
    paddingBottom: "14px", // Minimal space for version badge
    boxSizing: "border-box",
    position: "relative",
    overflow: "hidden", // Prevent content overflow from affecting form column sizing
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
    alignItems: "stretch",
    width: "100%",
  },
  versionBadge: {
    position: "absolute",
    bottom: 0,
    left: "4px",
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

  // v1.2.0: FetchXML override from PCF property (highest query priority)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const fetchXmlOverride = (context.parameters as any).fetchXmlOverride?.raw?.trim() || null;

  // v1.2.33: Value format override per-placement
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const valueFormatOverride = (context.parameters as any).valueFormatOverride?.raw?.trim() || null;

  const showToolbar = context.parameters.showToolbar?.raw !== false;
  const enableDrillThrough = context.parameters.enableDrillThrough?.raw !== false;
  const height = context.parameters.height?.raw;
  const width = context.parameters.width?.raw;
  const justification = (context.parameters.justification?.raw?.trim() as MatrixJustification) || null;
  const columns = context.parameters.columns?.raw;

  // v1.2.35: Column position for multi-column coordination
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const columnPosition = (context.parameters as any).columnPosition?.raw as number | null;

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

      // DueDateCard and DueDateCardList fetch their own data — skip aggregation
      const skipAggregation =
        definition.sprk_visualtype === 100000008 || // DueDateCard
        definition.sprk_visualtype === 100000009;   // DueDateCardList

      // v1.2.41: Check for field pivot mode — reads multiple fields from
      // the current record and presents each as a card (generic, not KPI-specific)
      const pivotConfig = parseFieldPivotConfig(definition.sprk_configurationjson);

      if (pivotConfig && definition.sprk_entitylogicalname && contextRecordId) {
        try {
          logger.info("VisualHostRoot", "Field pivot mode detected", {
            entity: definition.sprk_entitylogicalname,
            fields: pivotConfig.fields.length,
          });
          const data = await fetchAndPivot(
            context.webAPI,
            definition.sprk_entitylogicalname,
            contextRecordId,
            pivotConfig
          );
          setChartData(data);
          logger.info("VisualHostRoot", `Field pivot: ${data.dataPoints.length} data points`);
        } catch (pivotErr) {
          logger.error("VisualHostRoot", "Field pivot error", pivotErr);
          setChartData(null);
        }
      } else if (definition.sprk_entitylogicalname && !skipAggregation) {
        // Existing data aggregation path (VIEW or BASIC mode)
        try {
          const ctxFilter = contextFieldName && contextRecordId
            ? { fieldName: contextFieldName, recordId: contextRecordId }
            : undefined;
          logger.info("VisualHostRoot", `Context filter for aggregation: ${ctxFilter ? `fieldName="${ctxFilter.fieldName}", recordId="${ctxFilter.recordId}"` : "(none - no context)"}`);

          const data = await fetchAndAggregate(
            { webAPI: context.webAPI },
            definition,
            {
              contextFilter: ctxFilter,
            }
          );
          setChartData(data);
          logger.info("VisualHostRoot", `Data loaded: ${data.dataPoints.length} data points from ${data.totalRecords} records`);
        } catch (aggErr) {
          if (aggErr instanceof AggregationError) {
            logger.error("VisualHostRoot", "Data aggregation error", aggErr);
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
   * Handle expand button click - drill-through navigation.
   * v1.2.25: If sprk_drillthroughtarget is configured, opens the Custom Page in a
   * dialog with context filter params (entity, view, filter field/value, mode=dialog).
   * Otherwise falls back to navigateTo entitylist dialog (unfiltered).
   */
  const handleExpandClick = useCallback(async () => {
    logger.info("VisualHostRoot", "Expand clicked - navigating to view", { chartDefinitionId });

    if (!chartDefinition) {
      logger.warn("VisualHostRoot", "No chart definition for drill-through");
      return;
    }

    const entityName = chartDefinition.sprk_entitylogicalname;
    if (!entityName) {
      logger.warn("VisualHostRoot", "No entity name configured for drill-through");
      return;
    }

    // Resolve Xrm from multiple scopes — PCF controls run in iframes and
    // custom page navigation may require the parent frame's Xrm object.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window.parent as any)?.Xrm || (window as any).Xrm;

    if (!xrm?.Navigation?.navigateTo) {
      logger.warn("VisualHostRoot", "Xrm.Navigation not available");
      return;
    }

    logger.info("VisualHostRoot", "Xrm source", {
      fromParent: !!(window.parent as any)?.Xrm,
      fromWindow: !!(window as any).Xrm,
    });

    const viewId = chartDefinition.sprk_baseviewid;
    const drillThroughTarget = chartDefinition.sprk_drillthroughtarget?.trim();

    // Build context filter params
    const ctxField = chartDefinition.sprk_contextfieldname || contextFieldName;
    let filterField: string | null = null;
    let filterValue: string | null = null;
    if (ctxField && contextRecordId) {
      filterField = ctxField.replace(/^_/, "").replace(/_value$/, "");
      filterValue = contextRecordId.replace(/[{}]/g, "");
      logger.info("VisualHostRoot", "Context filter for drill-through", {
        filterField,
        filterValue,
      });
    }

    try {
      if (drillThroughTarget) {
        // Drill-through target is a web resource name (e.g. "sprk_eventspage.html").
        // Use pageType "webresource" to open it in a dialog.
        logger.info("VisualHostRoot", "Opening web resource drill-through dialog", {
          webresource: drillThroughTarget,
          entityName,
          filterField: filterField || "(none)",
          filterValue: filterValue || "(none)",
        });

        // Build query string to pass context to the web resource
        const params = new URLSearchParams();
        if (entityName) params.set("entityName", entityName);
        if (filterField) params.set("filterField", filterField);
        if (filterValue) params.set("filterValue", filterValue);
        if (viewId) params.set("viewId", viewId.replace(/[{}]/g, ""));
        params.set("mode", "dialog");

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const pageInput: any = {
          pageType: "webresource",
          webresourceName: drillThroughTarget,
          data: params.toString(),
        };

        const navOptions = {
          target: 2 as const,
          position: 1 as const,
          width: { value: 90, unit: "%" as const },
          height: { value: 85, unit: "%" as const },
        };

        try {
          await xrm.Navigation.navigateTo(pageInput, navOptions);
        } catch {
          logger.info("VisualHostRoot", "Dialog not supported, navigating inline");
          await xrm.Navigation.navigateTo(pageInput, { target: 1 });
        }
      } else {
        // Fallback: entitylist dialog (unfiltered — filterXml not supported by navigateTo)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const pageInput: any = { pageType: "entitylist", entityName };
        if (viewId) pageInput.viewId = viewId;

        logger.info("VisualHostRoot", "Opening entity list dialog (no custom page configured)", {
          entityName,
          viewId: viewId || "(default)",
        });

        try {
          await xrm.Navigation.navigateTo(pageInput, {
            target: 2,
            position: 1,
            width: { value: 90, unit: "%" },
            height: { value: 85, unit: "%" },
          });
        } catch {
          logger.info("VisualHostRoot", "Dialog not supported for entity list, navigating full page");
          await xrm.Navigation.navigateTo(pageInput, { target: 1 });
        }
      }

      logger.info("VisualHostRoot", "Drill-through view opened");
    } catch (err) {
      logger.error("VisualHostRoot", "Failed to open drill-through view", err);
    }
  }, [chartDefinition, contextFieldName, contextRecordId]);

  /**
   * Handle configured click action from ClickActionHandler
   */
  const handleClickAction = useCallback(
    async (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => {
      if (!chartDefinition || !hasClickAction(chartDefinition)) return;

      await executeClickAction(
        {
          chartDefinition,
          recordId,
          entityName,
          recordData,
        },
        handleExpandClick
      );
    },
    [chartDefinition, handleExpandClick]
  );

  /**
   * Handle "View List" navigation - switch to configured tab on current form
   */
  const handleViewListClick = useCallback(() => {
    if (!chartDefinition?.sprk_viewlisttabname) return;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm;
    const tabName = chartDefinition.sprk_viewlisttabname;

    logger.info("VisualHostRoot", "View List click - navigating to tab", { tabName });

    try {
      // Navigate to the configured tab on the current form
      const formContext = xrm?.Page;
      if (formContext?.ui?.tabs?.get) {
        const tab = formContext.ui.tabs.get(tabName);
        if (tab) {
          tab.setFocus();
        } else {
          logger.warn("VisualHostRoot", `Tab '${tabName}' not found on current form`);
        }
      }
    } catch (err) {
      logger.error("VisualHostRoot", "Failed to navigate to tab", err);
    }
  }, [chartDefinition]);

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
        webApi={context.webAPI}
        contextRecordId={contextRecordId || undefined}
        onClickAction={hasClickAction(chartDefinition) ? handleClickAction : undefined}
        onViewListClick={chartDefinition.sprk_viewlisttabname ? handleViewListClick : undefined}
        fetchXmlOverride={fetchXmlOverride || undefined}
        valueFormatOverride={valueFormatOverride || undefined}
        width={width || undefined}
        justification={justification || undefined}
        columns={columns || undefined}
      />
    );
  };

  // Container style with optional height and column-position edge padding
  const containerStyle: React.CSSProperties = {
    ...(height ? { minHeight: `${height}px` } : {}),
    // v1.2.35: When columnPosition is set, remove padding on inner edges
    // so adjacent PCFs visually merge into one cohesive row
    ...(columnPosition === 1 ? { paddingRight: 0 } : {}),
    ...(columnPosition != null && columnPosition >= 2 && columnPosition <= 3
      ? { paddingLeft: 0, paddingRight: 0 } : {}),
    ...(columnPosition != null && columnPosition >= 4 ? { paddingLeft: 0 } : {}),
  };

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
      <span className={styles.versionBadge}>v1.2.41 • 2026-02-15</span>

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
