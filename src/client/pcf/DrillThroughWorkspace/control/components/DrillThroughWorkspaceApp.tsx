/**
 * Drill-Through Workspace App Component
 *
 * Two-panel layout: Chart (1/3) | Dataset Grid (2/3)
 * Click chart elements to filter dataset via FilterStateContext
 *
 * Architecture: Dataset PCF pattern (ADR-011)
 * - Grid data from platform dataset binding
 * - Filter via dataset.filtering.setFilter() API
 *
 * @version 1.1.0
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
import {
  DismissRegular,
  ArrowMaximizeRegular,
  FilterRegular,
} from "@fluentui/react-icons";
import { DrillInteraction } from "@spaarke/ui-components";
import { TwoPanelLayout } from "./TwoPanelLayout";
import { DrillThroughGrid } from "./DrillThroughGrid";
import {
  FilterStateProvider,
  useFilterState,
} from "../context/FilterStateContext";
import { logger } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface IDrillThroughWorkspaceAppProps {
  chartDefinitionId: string;
  /** Platform-provided dataset (Dataset PCF pattern per ADR-011) */
  dataset: ComponentFramework.PropertyTypes.DataSet;
  /** WebAPI for loading chart configuration */
  webApi: ComponentFramework.WebApi;
  onRecordSelect: (recordIds: string[]) => void;
  onClose: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Inner Component (uses context)
// ─────────────────────────────────────────────────────────────────────────────

interface IWorkspaceContentProps {
  chartDefinitionId: string;
  webApi: ComponentFramework.WebApi;
  onClose: () => void;
}

/**
 * Inner workspace content that uses the FilterStateContext
 */
const WorkspaceContent: React.FC<IWorkspaceContentProps> = ({
  chartDefinitionId,
  webApi,
  onClose,
}) => {
  const styles = useStyles();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chartTitle, setChartTitle] = useState<string>("Chart");

  // Get filter state from context
  const { activeFilter, setFilter, clearFilter, isFiltered, dataset } =
    useFilterState();

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
        logger.info(
          "WorkspaceContent",
          `Loading chart: ${chartDefinitionId}`
        );

        // TODO: Use ConfigurationLoader from VisualHost to load chart definition
        // For now, show placeholder
        await new Promise((resolve) => setTimeout(resolve, 500));
        setChartTitle("Drill-Through Chart");
        setError(null);
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        logger.error("WorkspaceContent", "Failed to load chart", err);
        setError(`Failed to load chart: ${errorMessage}`);
      } finally {
        setIsLoading(false);
      }
    };

    loadChartDefinition();
  }, [chartDefinitionId]);

  /**
   * Handle drill interaction from chart
   * Calls setFilter from context which applies filter via platform API
   */
  const handleDrillInteraction = useCallback(
    (interaction: DrillInteraction) => {
      logger.info("WorkspaceContent", "Drill interaction", interaction);
      setFilter(interaction);
    },
    [setFilter]
  );

  /**
   * Handle clear filter
   * Calls clearFilter from context which clears filter via platform API
   */
  const handleClearFilter = useCallback(() => {
    logger.info("WorkspaceContent", "Clearing filter");
    clearFilter();
  }, [clearFilter]);

  /**
   * Format filter label for display
   */
  const getFilterLabel = (filter: DrillInteraction): string => {
    if (filter.label) return filter.label;
    if (Array.isArray(filter.value)) {
      if (filter.operator === "between" && filter.value.length === 2) {
        return `${filter.field}: ${filter.value[0]} - ${filter.value[1]}`;
      }
      return `${filter.field}: ${filter.value.join(", ")}`;
    }
    return `${filter.field}: ${String(filter.value)}`;
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

    // TODO: Render actual chart component with onDrillInteraction callback
    return (
      <div className={styles.placeholder}>
        <ArrowMaximizeRegular style={{ fontSize: 48 }} />
        <Text>Chart visualization will render here</Text>
        <Text size={200}>Click chart elements to filter the dataset</Text>
        <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
          Task 033: Integrate chart component with handleDrillInteraction
        </Text>
      </div>
    );
  };

  /**
   * Handle record selection from grid
   */
  const handleGridSelectionChange = useCallback(
    (recordIds: string[]) => {
      logger.debug("WorkspaceContent", "Grid selection changed", { recordIds });
      // Selection is managed by DrillThroughGrid and synced with platform
    },
    []
  );

  /**
   * Render dataset grid content
   * Dataset provided by platform via Dataset PCF binding (ADR-011)
   */
  const renderGridContent = () => {
    if (!dataset) {
      return (
        <div className={styles.placeholder}>
          <Spinner label="Waiting for dataset..." />
        </div>
      );
    }

    return (
      <DrillThroughGrid
        dataset={dataset}
        onSelectionChange={handleGridSelectionChange}
      />
    );
  };

  /**
   * Render filter badge for right panel header
   */
  const renderRightActions = () => {
    if (!isFiltered || !activeFilter) return null;

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
          rightTitle={`Dataset (${dataset?.sortedRecordIds?.length ?? 0} records)`}
          rightActions={renderRightActions()}
          showHeaders={true}
          enableResize={true}
        />
      </div>

      {/* Footer with version */}
      <div className={styles.footer}>
        <Text size={100}>v1.1.0 • Dataset PCF</Text>
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Main Component (provides context)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Drill-Through Workspace App
 *
 * Wraps content with FilterStateProvider to enable filter sharing
 * between chart and dataset grid.
 */
export const DrillThroughWorkspaceApp: React.FC<IDrillThroughWorkspaceAppProps> = ({
  chartDefinitionId,
  dataset,
  webApi,
  onRecordSelect,
  onClose,
}) => {
  return (
    <FilterStateProvider dataset={dataset}>
      <WorkspaceContent
        chartDefinitionId={chartDefinitionId}
        webApi={webApi}
        onClose={onClose}
      />
    </FilterStateProvider>
  );
};
