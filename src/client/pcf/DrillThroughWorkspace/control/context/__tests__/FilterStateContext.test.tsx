/**
 * FilterStateContext Tests
 *
 * Tests for the filter state context that bridges chart drill interactions
 * with the platform dataset filtering API.
 */

import * as React from "react";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import {
  FilterStateProvider,
  useFilterState,
  drillInteractionToFilterExpression,
  IFilterStateContextValue,
} from "../FilterStateContext";
import { DrillInteraction } from "@spaarke/ui-components";

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Create mock dataset for testing
 */
function createMockDataset(): ComponentFramework.PropertyTypes.DataSet {
  return {
    loading: false,
    error: false,
    errorMessage: "",
    sortedRecordIds: ["1", "2", "3"],
    records: {},
    columns: [],
    paging: {
      pageSize: 25,
      totalResultCount: 3,
      hasNextPage: false,
      hasPreviousPage: false,
      loadNextPage: jest.fn(),
      loadPreviousPage: jest.fn(),
      reset: jest.fn(),
      setPageSize: jest.fn(),
    } as unknown as ComponentFramework.PropertyHelper.DataSetApi.Paging,
    sorting: [],
    filtering: {
      clearFilter: jest.fn(),
      getFilter: jest.fn().mockReturnValue({ conditions: [], filterOperator: 0 }),
      setFilter: jest.fn(),
    } as unknown as ComponentFramework.PropertyHelper.DataSetApi.Filtering,
    linking: {
      addLinkedEntity: jest.fn(),
      getLinkedEntities: jest.fn().mockReturnValue([]),
    } as unknown as ComponentFramework.PropertyHelper.DataSetApi.Linking,
    refresh: jest.fn(),
    getTargetEntityType: jest.fn().mockReturnValue("account"),
    getTitle: jest.fn().mockReturnValue("Test Dataset"),
    getViewId: jest.fn().mockReturnValue("view-id"),
    openDatasetItem: jest.fn(),
    clearSelectedRecordIds: jest.fn(),
    getSelectedRecordIds: jest.fn().mockReturnValue([]),
    setSelectedRecordIds: jest.fn(),
    addColumn: jest.fn(),
  } as unknown as ComponentFramework.PropertyTypes.DataSet;
}

/**
 * Test component to access context values
 */
const TestConsumer: React.FC<{
  onContext?: (ctx: IFilterStateContextValue) => void;
}> = ({ onContext }) => {
  const context = useFilterState();

  React.useEffect(() => {
    onContext?.(context);
  }, [context, onContext]);

  return (
    <div>
      <span data-testid="isFiltered">{context.isFiltered ? "yes" : "no"}</span>
      <span data-testid="activeFilter">
        {context.activeFilter ? context.activeFilter.field : "none"}
      </span>
      <button
        data-testid="setFilter"
        onClick={() =>
          context.setFilter({
            field: "statuscode",
            operator: "eq",
            value: 1,
            label: "Active",
          })
        }
      >
        Set Filter
      </button>
      <button data-testid="clearFilter" onClick={() => context.clearFilter()}>
        Clear Filter
      </button>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// drillInteractionToFilterExpression Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("drillInteractionToFilterExpression", () => {
  it("converts eq operator to Equal condition", () => {
    const interaction: DrillInteraction = {
      field: "statuscode",
      operator: "eq",
      value: 1,
    };

    const result = drillInteractionToFilterExpression(interaction);

    expect(result.filterOperator).toBe(0); // And
    expect(result.conditions).toHaveLength(1);
    expect(result.conditions[0].attributeName).toBe("statuscode");
    expect(result.conditions[0].conditionOperator).toBe(0); // Equal
    expect(result.conditions[0].value).toBe("1");
  });

  it("converts in operator to In condition with array value", () => {
    const interaction: DrillInteraction = {
      field: "sprk_type",
      operator: "in",
      value: [1, 2, 3],
    };

    const result = drillInteractionToFilterExpression(interaction);

    expect(result.filterOperator).toBe(0); // And
    expect(result.conditions).toHaveLength(1);
    expect(result.conditions[0].attributeName).toBe("sprk_type");
    expect(result.conditions[0].conditionOperator).toBe(8); // In
    expect(result.conditions[0].value).toEqual(["1", "2", "3"]);
  });

  it("converts between operator to GreaterEqual and LessEqual conditions", () => {
    const interaction: DrillInteraction = {
      field: "createdon",
      operator: "between",
      value: ["2025-01-01", "2025-01-31"],
    };

    const result = drillInteractionToFilterExpression(interaction);

    expect(result.filterOperator).toBe(0); // And
    expect(result.conditions).toHaveLength(2);

    // First condition: GreaterEqual
    expect(result.conditions[0].attributeName).toBe("createdon");
    expect(result.conditions[0].conditionOperator).toBe(4); // GreaterEqual
    expect(result.conditions[0].value).toBe("2025-01-01");

    // Second condition: LessEqual
    expect(result.conditions[1].attributeName).toBe("createdon");
    expect(result.conditions[1].conditionOperator).toBe(5); // LessEqual
    expect(result.conditions[1].value).toBe("2025-01-31");
  });

  it("handles string values", () => {
    const interaction: DrillInteraction = {
      field: "name",
      operator: "eq",
      value: "Test Account",
    };

    const result = drillInteractionToFilterExpression(interaction);

    expect(result.conditions[0].value).toBe("Test Account");
  });

  it("handles boolean values", () => {
    const interaction: DrillInteraction = {
      field: "isactive",
      operator: "eq",
      value: true,
    };

    const result = drillInteractionToFilterExpression(interaction);

    expect(result.conditions[0].value).toBe("true");
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// FilterStateProvider Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("FilterStateProvider", () => {
  it("provides initial state with no filter", () => {
    const mockDataset = createMockDataset();

    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer />
      </FilterStateProvider>
    );

    expect(screen.getByTestId("isFiltered")).toHaveTextContent("no");
    expect(screen.getByTestId("activeFilter")).toHaveTextContent("none");
  });

  it("provides dataset to consumers", () => {
    const mockDataset = createMockDataset();
    let capturedContext: IFilterStateContextValue | undefined;

    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer onContext={(ctx) => (capturedContext = ctx)} />
      </FilterStateProvider>
    );

    expect(capturedContext?.dataset).toBe(mockDataset);
  });

  it("setFilter applies filter to dataset", () => {
    const mockDataset = createMockDataset();

    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer />
      </FilterStateProvider>
    );

    // Click set filter button
    act(() => {
      screen.getByTestId("setFilter").click();
    });

    // Verify setFilter was called on dataset
    expect(mockDataset.filtering.setFilter).toHaveBeenCalled();
    expect(mockDataset.refresh).toHaveBeenCalled();

    // Verify state updated
    expect(screen.getByTestId("isFiltered")).toHaveTextContent("yes");
    expect(screen.getByTestId("activeFilter")).toHaveTextContent("statuscode");
  });

  it("clearFilter clears filter from dataset", () => {
    const mockDataset = createMockDataset();

    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer />
      </FilterStateProvider>
    );

    // Set a filter first
    act(() => {
      screen.getByTestId("setFilter").click();
    });

    expect(screen.getByTestId("isFiltered")).toHaveTextContent("yes");

    // Clear the filter
    act(() => {
      screen.getByTestId("clearFilter").click();
    });

    // Verify clearFilter was called on dataset
    expect(mockDataset.filtering.clearFilter).toHaveBeenCalled();
    expect(mockDataset.refresh).toHaveBeenCalledTimes(2); // Once for set, once for clear

    // Verify state updated
    expect(screen.getByTestId("isFiltered")).toHaveTextContent("no");
    expect(screen.getByTestId("activeFilter")).toHaveTextContent("none");
  });

  it("handles missing filtering API gracefully", () => {
    const mockDataset = createMockDataset();
    // Remove filtering API
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (mockDataset as any).filtering = undefined;

    // Should not throw
    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer />
      </FilterStateProvider>
    );

    // Click buttons - should not throw
    act(() => {
      screen.getByTestId("setFilter").click();
    });

    act(() => {
      screen.getByTestId("clearFilter").click();
    });

    // State should remain unchanged
    expect(screen.getByTestId("isFiltered")).toHaveTextContent("no");
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// useFilterState Hook Tests
// ─────────────────────────────────────────────────────────────────────────────

describe("useFilterState", () => {
  it("returns default values when used outside provider", () => {
    // Suppress console.warn for this test
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => {});

    let capturedContext: IFilterStateContextValue | undefined;

    render(
      <TestConsumer onContext={(ctx) => (capturedContext = ctx)} />
    );

    expect(capturedContext?.isFiltered).toBe(false);
    expect(capturedContext?.activeFilter).toBeNull();
    expect(capturedContext?.dataset).toBeNull();

    warnSpy.mockRestore();
  });

  it("returns context values when used inside provider", () => {
    const mockDataset = createMockDataset();
    let capturedContext: IFilterStateContextValue | undefined;

    render(
      <FilterStateProvider dataset={mockDataset}>
        <TestConsumer onContext={(ctx) => (capturedContext = ctx)} />
      </FilterStateProvider>
    );

    expect(capturedContext?.isFiltered).toBe(false);
    expect(capturedContext?.activeFilter).toBeNull();
    expect(capturedContext?.dataset).toBe(mockDataset);
    expect(typeof capturedContext?.setFilter).toBe("function");
    expect(typeof capturedContext?.clearFilter).toBe("function");
  });
});
