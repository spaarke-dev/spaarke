/**
 * Drill-Through Workspace App Component
 * Uses TwoPanelLayout: Chart (1/3) | Dataset Grid (2/3)
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
import { DismissRegular, ArrowMaximizeRegular, FilterRegular } from "@fluentui/react-icons";
import { TwoPanelLayout } from "./TwoPanelLayout";
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
    flex: 1,
    overflow: "hidden",
  },
  filterBadge: {
    display: "inline-flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200,
  },
  filterActions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
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

  // Load chart definition
  React.useEffect(() => {
    if (!chartDefinitionId) {
      setIsLoading(false);
      setError("No chart definition ID provided");
      return;
    }

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
  const handleDrillInteraction = useCallback((filter: IActiveFilter) => {
    logger.info("DrillThroughWorkspaceApp", "Drill interaction", filter);
    setActiveFilter(filter);
    // TODO: Task 032 - Apply filter to dataset grid
  }, []);

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

  /**
   * Render chart panel content
   */
  const renderChartContent = () => {
    if (isLoading) {
      return (
        <div className={styles.placeholder}>
          <Spinner label="Loading chart..." />
        </div>
      );
    }

    if (error) {
      return (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      );
    }

    return (
      <div className={styles.placeholder}>
        <ArrowMaximizeRegular style={{ fontSize: 48 }} />
        <Text>Chart visualization will render here</Text>
        <Text size={200}>Click chart elements to filter the dataset</Text>
      </div>
    );
  };

  /**
   * Render dataset grid content
   */
  const renderGridContent = () => {
    return (
      <div className={styles.placeholder}>
        <FilterRegular style={{ fontSize: 48 }} />
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
    );
  };

  /**
   * Render filter badge for right panel header
   */
  const renderRightActions = () => {
    if (!activeFilter) return null;

    return (
      <div className={styles.filterActions}>
        <span className={styles.filterBadge}>
          <FilterRegular fontSize={12} />
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
    );
  };

  return (
    <div className={styles.container}>
      {/* Header with title and close button */}
      <div className={styles.header}>
        <Text className={styles.headerTitle}>
          {chartTitle} - Drill-Through View
        </Text>
        <div className={styles.headerActions}>
          <Tooltip content="Close (Esc)" relationship="label">
            <Button
              appearance="subtle"
              icon={<DismissRegular />}
              onClick={onClose}
              aria-label="Close workspace"
            />
          </Tooltip>
        </div>
      </div>

      {/* Main content - Two Panel Layout */}
      <div className={styles.content}>
        <TwoPanelLayout
          leftContent={renderChartContent()}
          rightContent={renderGridContent()}
          leftTitle="Chart"
          rightTitle={entityName ? `Dataset - ${entityName}` : "Dataset"}
          rightActions={renderRightActions()}
          showHeaders={true}
          enableResize={true}
        />
      </div>

      {/* Footer with version */}
      <div className={styles.footer}>
        <Text size={100}>v1.0.0 • Built 2025-12-29</Text>
      </div>
    </div>
  );
};
