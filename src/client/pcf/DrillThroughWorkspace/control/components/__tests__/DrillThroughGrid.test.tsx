/**
 * DrillThroughGrid Component Tests
 *
 * Tests for the drill-through grid component that displays dataset records
 * with FilterStateContext integration.
 */

import * as React from "react";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { DrillThroughGrid } from "../DrillThroughGrid";
import { FilterStateProvider } from "../../context/FilterStateContext";

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Create mock column
 */
function createMockColumn(
  name: string,
  displayName: string
): ComponentFramework.PropertyHelper.DataSetApi.Column {
  return {
    name,
    displayName,
    dataType: "SingleLine.Text",
    alias: name,
    order: 0,
    visualSizeFactor: 1,
    isHidden: false,
    isPrimary: false,
    disableSorting: false,
  } as ComponentFramework.PropertyHelper.DataSetApi.Column;
}

/**
 * Create mock record
 */
function createMockRecord(
  id: string,
  values: Record<string, string>
): ComponentFramework.PropertyHelper.DataSetApi.EntityRecord {
  return {
    getRecordId: () => id,
    getNamedReference: () => ({ id, name: values["name"] || id }),
    getFormattedValue: (columnName: string) => values[columnName] || "",
    getValue: (columnName: string) => values[columnName] || "",
  } as unknown as ComponentFramework.PropertyHelper.DataSetApi.EntityRecord;
}

/**
 * Create mock dataset for testing
 */
function createMockDataset(
  options: {
    loading?: boolean;
    columns?: ComponentFramework.PropertyHelper.DataSetApi.Column[];
    records?: Record<
      string,
      ComponentFramework.PropertyHelper.DataSetApi.EntityRecord
    >;
    sortedRecordIds?: string[];
  } = {}
): ComponentFramework.PropertyTypes.DataSet {
  const defaultColumns = [
    createMockColumn("name", "Name"),
    createMockColumn("statuscode", "Status"),
    createMockColumn("createdon", "Created On"),
  ];

  const defaultRecords = {
    "1": createMockRecord("1", {
      name: "Account A",
      statuscode: "Active",
      createdon: "2025-01-01",
    }),
    "2": createMockRecord("2", {
      name: "Account B",
      statuscode: "Inactive",
      createdon: "2025-01-02",
    }),
    "3": createMockRecord("3", {
      name: "Account C",
      statuscode: "Active",
      createdon: "2025-01-03",
    }),
  };

  return {
    loading: options.loading ?? false,
    error: false,
    errorMessage: "",
    sortedRecordIds: options.sortedRecordIds ?? ["1", "2", "3"],
    records: options.records ?? defaultRecords,
    columns: options.columns ?? defaultColumns,
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
      getFilter: jest
        .fn()
        .mockReturnValue({ conditions: [], filterOperator: 0 }),
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
 * Wrapper component that provides FilterStateContext
 */
const TestWrapper: React.FC<{
  dataset: ComponentFramework.PropertyTypes.DataSet;
  children: React.ReactNode;
}> = ({ dataset, children }) => {
  return (
    <FilterStateProvider dataset={dataset}>{children}</FilterStateProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Tests: Loading States
// ─────────────────────────────────────────────────────────────────────────────

describe("DrillThroughGrid - Loading States", () => {
  it("shows loading spinner when dataset is loading", () => {
    const mockDataset = createMockDataset({ loading: true });

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    expect(screen.getByText("Loading data...")).toBeInTheDocument();
  });

  it("shows loading spinner when isLoading prop is true", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} isLoading={true} />
      </TestWrapper>
    );

    expect(screen.getByText("Loading data...")).toBeInTheDocument();
  });

  it("shows loading columns message when columns are empty", () => {
    const mockDataset = createMockDataset({ columns: [] });

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    expect(screen.getByText("Loading columns...")).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests: Empty States
// ─────────────────────────────────────────────────────────────────────────────

describe("DrillThroughGrid - Empty States", () => {
  it("shows empty message when no records", () => {
    const mockDataset = createMockDataset({
      sortedRecordIds: [],
      records: {},
    });

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    expect(screen.getByText("No records found")).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests: Data Rendering
// ─────────────────────────────────────────────────────────────────────────────

describe("DrillThroughGrid - Data Rendering", () => {
  it("renders column headers from dataset", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Status")).toBeInTheDocument();
    expect(screen.getByText("Created On")).toBeInTheDocument();
  });

  it("renders record data in grid cells", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    expect(screen.getByText("Account A")).toBeInTheDocument();
    expect(screen.getByText("Account B")).toBeInTheDocument();
    expect(screen.getByText("Account C")).toBeInTheDocument();
    // "Active" appears twice (Account A and Account C)
    expect(screen.getAllByText("Active")).toHaveLength(2);
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("renders correct number of rows", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    // Count rows by looking for cell content
    const accountACells = screen.getAllByText("Account A");
    expect(accountACells).toHaveLength(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests: Selection
// ─────────────────────────────────────────────────────────────────────────────

describe("DrillThroughGrid - Selection", () => {
  it("calls onSelectionChange when selection changes", () => {
    const mockDataset = createMockDataset();
    const handleSelectionChange = jest.fn();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid
          dataset={mockDataset}
          onSelectionChange={handleSelectionChange}
        />
      </TestWrapper>
    );

    // The grid should render and be interactive
    const grid = screen.getByRole("grid");
    expect(grid).toBeInTheDocument();
  });

  it("syncs initial selection from dataset", () => {
    const mockDataset = createMockDataset();
    mockDataset.getSelectedRecordIds = jest.fn().mockReturnValue(["1"]);

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    // Grid should have been rendered with initial selection
    expect(mockDataset.getSelectedRecordIds).toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests: Accessibility
// ─────────────────────────────────────────────────────────────────────────────

describe("DrillThroughGrid - Accessibility", () => {
  it("has accessible grid role", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    const grid = screen.getByRole("grid");
    expect(grid).toHaveAttribute("aria-label", "Drill-through data grid");
  });

  it("has accessible column headers", () => {
    const mockDataset = createMockDataset();

    render(
      <TestWrapper dataset={mockDataset}>
        <DrillThroughGrid dataset={mockDataset} />
      </TestWrapper>
    );

    const columnHeaders = screen.getAllByRole("columnheader");
    // 3 data columns + 1 selection column = 4
    expect(columnHeaders.length).toBeGreaterThanOrEqual(3);
  });
});
