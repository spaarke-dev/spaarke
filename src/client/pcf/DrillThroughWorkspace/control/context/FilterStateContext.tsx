/**
 * Filter State Context - DrillThroughWorkspace
 *
 * React context that shares filter state between the chart and dataset grid.
 * When chart selection changes, applies filter to the platform dataset using
 * the dataset.filtering API (Dataset PCF pattern per ADR-011).
 *
 * @version 1.0.0
 */

import * as React from "react";
import { createContext, useContext, useState, useCallback, useMemo } from "react";
import type { DrillInteraction, DrillOperator } from "../types";
import { logger } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Type alias for PCF ConditionOperator
 */
type ConditionOperator = ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator;

/**
 * Type alias for PCF FilterOperator
 */
type FilterOperator = ComponentFramework.PropertyHelper.DataSetApi.Types.FilterOperator;

/**
 * PCF Condition Operator mapping
 * Based on ComponentFramework.PropertyHelper.DataSetApi.Types.ConditionOperator
 * @see https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.query.conditionoperator
 */
const ConditionOperatorMap: Record<string, ConditionOperator> = {
  Equal: 0,
  NotEqual: 1,
  GreaterThan: 2,
  LessThan: 3,
  GreaterEqual: 4,
  LessEqual: 5,
  Like: 6,
  In: 8,
  Between: 12,
  NotIn: 14,
  Null: 15,
};

/**
 * PCF Filter Operator (And=0, Or=1)
 */
const FilterOperatorMap: Record<string, FilterOperator> = {
  And: 0,
  Or: 1,
};

/**
 * Context value interface
 */
export interface IFilterStateContextValue {
  /** Currently active drill filter (null if no filter) */
  activeFilter: DrillInteraction | null;

  /** Apply a drill interaction filter to the dataset */
  setFilter: (filter: DrillInteraction) => void;

  /** Clear the active filter from the dataset */
  clearFilter: () => void;

  /** Whether a filter is currently active */
  isFiltered: boolean;

  /** Dataset reference for grid rendering */
  dataset: ComponentFramework.PropertyTypes.DataSet | null;
}

/**
 * Props for the FilterStateProvider
 */
export interface IFilterStateProviderProps {
  /** Platform-provided dataset from PCF context */
  dataset: ComponentFramework.PropertyTypes.DataSet;

  /** React children */
  children: React.ReactNode;
}

// ─────────────────────────────────────────────────────────────────────────────
// Context
// ─────────────────────────────────────────────────────────────────────────────

const defaultContextValue: IFilterStateContextValue = {
  activeFilter: null,
  setFilter: () => {
    logger.warn("FilterStateContext", "setFilter called without provider");
  },
  clearFilter: () => {
    logger.warn("FilterStateContext", "clearFilter called without provider");
  },
  isFiltered: false,
  dataset: null,
};

/**
 * Filter State Context
 */
export const FilterStateContext = createContext<IFilterStateContextValue>(defaultContextValue);

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Convert DrillOperator to PCF ConditionOperator
 */
function drillOperatorToConditionOperator(
  operator: DrillOperator
): ConditionOperator {
  switch (operator) {
    case "eq":
      return ConditionOperatorMap.Equal;
    case "in":
      return ConditionOperatorMap.In;
    case "between":
      return ConditionOperatorMap.Between;
    default:
      return ConditionOperatorMap.Equal;
  }
}

/**
 * Convert DrillInteraction to PCF FilterExpression
 *
 * @param interaction - The drill interaction from chart click
 * @returns FilterExpression for dataset.filtering.setFilter()
 */
export function drillInteractionToFilterExpression(
  interaction: DrillInteraction
): ComponentFramework.PropertyHelper.DataSetApi.FilterExpression {
  const { field, operator, value } = interaction;

  // Handle "between" operator - needs two conditions with ge/le
  if (operator === "between" && Array.isArray(value) && value.length === 2) {
    return {
      filterOperator: FilterOperatorMap.And,
      conditions: [
        {
          attributeName: field,
          conditionOperator: ConditionOperatorMap.GreaterEqual,
          value: String(value[0]),
        },
        {
          attributeName: field,
          conditionOperator: ConditionOperatorMap.LessEqual,
          value: String(value[1]),
        },
      ],
    };
  }

  // Handle "in" operator - value is an array
  if (operator === "in" && Array.isArray(value)) {
    return {
      filterOperator: FilterOperatorMap.And,
      conditions: [
        {
          attributeName: field,
          conditionOperator: ConditionOperatorMap.In,
          value: value.map((v) => String(v)),
        },
      ],
    };
  }

  // Default: "eq" operator - single value
  return {
    filterOperator: FilterOperatorMap.And,
    conditions: [
      {
        attributeName: field,
        conditionOperator: drillOperatorToConditionOperator(operator),
        value: String(value),
      },
    ],
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Provider
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Filter State Provider
 *
 * Wraps the drill-through workspace to provide filter state context.
 * Manages the connection between chart drill interactions and the platform dataset.
 */
export const FilterStateProvider: React.FC<IFilterStateProviderProps> = ({
  dataset,
  children,
}) => {
  const [activeFilter, setActiveFilterState] = useState<DrillInteraction | null>(null);

  /**
   * Apply a drill interaction filter to the dataset
   */
  const setFilter = useCallback(
    (filter: DrillInteraction) => {
      logger.info("FilterStateContext", "setFilter called", filter);

      if (!dataset?.filtering) {
        logger.error("FilterStateContext", "Dataset filtering API not available");
        return;
      }

      try {
        // Convert drill interaction to PCF filter expression
        const filterExpression = drillInteractionToFilterExpression(filter);

        logger.info(
          "FilterStateContext",
          "Applying filter expression",
          filterExpression
        );

        // Apply filter via platform API
        dataset.filtering.setFilter(filterExpression);

        // Update local state
        setActiveFilterState(filter);

        // Refresh dataset to apply filter
        dataset.refresh();

        logger.info("FilterStateContext", "Filter applied successfully");
      } catch (error) {
        logger.error("FilterStateContext", "Failed to apply filter", error);
      }
    },
    [dataset]
  );

  /**
   * Clear the active filter from the dataset
   */
  const clearFilter = useCallback(() => {
    logger.info("FilterStateContext", "clearFilter called");

    if (!dataset?.filtering) {
      logger.error("FilterStateContext", "Dataset filtering API not available");
      return;
    }

    try {
      // Clear filter via platform API
      dataset.filtering.clearFilter();

      // Update local state
      setActiveFilterState(null);

      // Refresh dataset to remove filter
      dataset.refresh();

      logger.info("FilterStateContext", "Filter cleared successfully");
    } catch (error) {
      logger.error("FilterStateContext", "Failed to clear filter", error);
    }
  }, [dataset]);

  /**
   * Context value
   */
  const contextValue = useMemo<IFilterStateContextValue>(
    () => ({
      activeFilter,
      setFilter,
      clearFilter,
      isFiltered: activeFilter !== null,
      dataset,
    }),
    [activeFilter, setFilter, clearFilter, dataset]
  );

  return (
    <FilterStateContext.Provider value={contextValue}>
      {children}
    </FilterStateContext.Provider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook to access filter state context
 *
 * @returns Filter state context value
 * @throws Error if used outside of FilterStateProvider
 *
 * @example
 * ```tsx
 * const { activeFilter, setFilter, clearFilter } = useFilterState();
 *
 * // Apply filter from chart click
 * const handleChartClick = (interaction: DrillInteraction) => {
 *   setFilter(interaction);
 * };
 *
 * // Clear filter
 * <Button onClick={clearFilter}>Reset Filter</Button>
 * ```
 */
export function useFilterState(): IFilterStateContextValue {
  const context = useContext(FilterStateContext);

  if (context === defaultContextValue) {
    logger.warn(
      "FilterStateContext",
      "useFilterState called outside of FilterStateProvider"
    );
  }

  return context;
}
