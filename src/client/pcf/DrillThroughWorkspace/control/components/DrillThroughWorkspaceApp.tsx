/**
 * Drill-Through Workspace App Component
 * Two-panel layout: Chart (1/3) | Dataset Grid (2/3)
 */

import * as React from "react";
import { useState, useCallback } from "react";
import {
  makeStyles,
  tokens,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import { DismissRegular, ArrowMaximizeRegular } from "@fluentui/react-icons";
import { logger } from "../utils/logger";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  headerActions: {
    display: "flex",
    gap: tokens.spacingHorizontalS,
  },
  content: {
    display: "flex",
    flex: 1,
    overflow: "hidden",
  },
  chartPanel: {
    width: "33.33%",
    minWidth: "300px",
    borderRight: `1px solid ${tokens.colorNeutralStroke1}`,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  gridPanel: {
    flex: 1,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  panelHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: tokens.spacingVerticalM,
  },
  panelTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  filterBadge: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200,
  },
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    padding: tokens.spacingVerticalS,
    paddingRight: tokens.spacingHorizontalL,
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

export interface IDrillThroughWorkspaceAppProps {
  chartDefinitionId: string;
  entityName: string;
  initialFilter: string;
  webApi: ComponentFramework.WebApi;
  onRecordSelect: (recordIds: string[]) => void;
  onClose: () => void;
}

interface IActiveFilter {
  field: string;
  operator: "eq" | "in" | "between";
  value: string | string[] | [string, string];
  label?: string;
}

export const DrillThroughWorkspaceApp: React.FC<IDrillThroughWorkspaceAppProps> = ({
  chartDefinitionId,
  entityName,
  initialFilter,
  webApi,
  onRecordSelect,
  onClose,
}) => {
  const styles = useStyles();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chartTitle, setChartTitle] = useState<string>("Chart");
  const [activeFilter, setActiveFilter] = useState<IActiveFilter | null>(null);

  // Simulate loading chart definition
  React.useEffect(() => {
    if (!chartDefinitionId) {
      setIsLoading(false);
      setError("No chart definition ID provided");
      return;
    }

    // TODO: Task 021 - Implement actual configuration loader
    const loadChartDefinition = async () => {
      try {
        setIsLoading(true);
        logger.info("DrillThroughWorkspaceApp", `Loading chart: ${chartDefinitionId}`);

        // Simulate loading delay
        await new Promise((resolve) => setTimeout(resolve, 500));

        // For now, show placeholder
        setChartTitle("Drill-Through Chart");
        setError("Configuration loader not yet implemented. Complete task 021.");
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        logger.error("DrillThroughWorkspaceApp", "Failed to load chart", err);
        setError(`Failed to load chart: ${errorMessage}`);
      } finally {
        setIsLoading(false);
      }
    };

    loadChartDefinition();
  }, [chartDefinitionId]);

  /**
   * Handle drill interaction from chart
   */
  const handleDrillInteraction = useCallback(
    (filter: IActiveFilter) => {
      logger.info("DrillThroughWorkspaceApp", "Drill interaction", filter);
      setActiveFilter(filter);
      // TODO: Task 032 - Apply filter to dataset grid
    },
    []
  );

  /**
   * Clear active filter
   */
  const handleClearFilter = useCallback(() => {
    logger.info("DrillThroughWorkspaceApp", "Clearing filter");
    setActiveFilter(null);
  }, []);

  /**
   * Format filter label for display
   */
  const getFilterLabel = (filter: IActiveFilter): string => {
    if (filter.label) return filter.label;
    if (Array.isArray(filter.value)) {
      if (filter.operator === "between") {
        return `${filter.field}: ${filter.value[0]} - ${filter.value[1]}`;
      }
      return `${filter.field}: ${filter.value.join(", ")}`;
    }
    return `${filter.field}: ${filter.value}`;
  };

  return (
    <div className={styles.container}>
      {/* Header with title and close button */}
      <div className={styles.header}>
        <Text className={styles.headerTitle}>
          {chartTitle} - Drill-Through View
        </Text>
        <div className={styles.headerActions}>
          <Tooltip content="Close" relationship="label">
            <Button
              appearance="subtle"
              icon={<DismissRegular />}
              onClick={onClose}
              aria-label="Close workspace"
            />
          </Tooltip>
        </div>
      </div>

      {/* Main content area */}
      <div className={styles.content}>
        {/* Chart panel (1/3) */}
        <div className={styles.chartPanel}>
          <div className={styles.panelHeader}>
            <Text className={styles.panelTitle}>Chart</Text>
          </div>

          {isLoading ? (
            <div className={styles.placeholder}>
              <Spinner label="Loading chart..." />
            </div>
          ) : error ? (
            <MessageBar intent="error">
              <MessageBarBody>{error}</MessageBarBody>
            </MessageBar>
          ) : (
            <div className={styles.placeholder}>
              <ArrowMaximizeRegular style={{ fontSize: 48 }} />
              <Text>Chart visualization will render here</Text>
              <Text size={200}>Click chart elements to filter the dataset</Text>
            </div>
          )}
        </div>

        {/* Grid panel (2/3) */}
        <div className={styles.gridPanel}>
          <div className={styles.panelHeader}>
            <Text className={styles.panelTitle}>
              Dataset
              {entityName && ` - ${entityName}`}
            </Text>
            {activeFilter && (
              <div style={{ display: "flex", alignItems: "center", gap: tokens.spacingHorizontalS }}>
                <span className={styles.filterBadge}>
                  {getFilterLabel(activeFilter)}
                </span>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<DismissRegular />}
                  onClick={handleClearFilter}
                  aria-label="Clear filter"
                />
              </div>
            )}
          </div>

          <div className={styles.placeholder}>
            <Text>Dataset grid will render here</Text>
            <Text size={200}>
              {activeFilter
                ? "Showing filtered records"
                : "Select a chart element to filter records"}
            </Text>
            <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
              Task 033: Implement dataset grid with filtering
            </Text>
          </div>
        </div>
      </div>

      {/* Footer with version */}
      <div className={styles.footer}>
        <Text size={100}>v1.0.0 • Built 2025-12-29</Text>
      </div>
    </div>
  );
};
