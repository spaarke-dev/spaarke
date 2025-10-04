# Dataset Component Testing Guide

## Test Structure Overview
### File Organization
```
__tests__/
├── unit/
│   ├── components/
│   │   ├── UniversalDatasetGrid.test.tsx
│   │   ├── GridView.test.tsx
│   │   └── CardView.test.tsx
│   ├── hooks/
│   │   ├── useDatasetMode.test.ts
│   │   └── useHeadlessMode.test.ts
│   ├── services/
│   │   ├── EntityConfiguration.test.ts
│   │   └── CommandExecutor.test.ts
│   └── renderers/
│       └── ColumnRenderers.test.tsx
├── integration/
│   ├── DatasetBinding.test.ts
│   ├── CommandExecution.test.ts
│   └── Navigation.test.ts
├── e2e/
│   ├── DocumentGrid.e2e.ts
│   └── Performance.e2e.ts
└── fixtures/
    ├── mockDataset.ts
    ├── mockContext.ts
    └── testData.ts
```

## Unit Testing Patterns
### Component Testing with Fluent v9
```typescript
// __tests__/unit/components/UniversalDatasetGrid.test.tsx
import { render, screen } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { UniversalDatasetGrid } from "../../../components/UniversalDatasetGrid";
import { createMockDataset } from "../../fixtures/mockDataset";

const renderWithFluent = (component: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{component}</FluentProvider>);

describe("UniversalDatasetGrid", () => {
  it("renders grid view", () => {
    const props: any = { viewMode: "Grid", dataset: createMockDataset() };
    renderWithFluent(<UniversalDatasetGrid {...props} />);
    expect(screen.getByRole("grid")).toBeInTheDocument();
  });
});
```

### Hook Testing
```typescript
// __tests__/unit/hooks/useDatasetMode.test.ts
import { renderHook, act } from "@testing-library/react";
import { useDatasetMode } from "../../../hooks/useDatasetMode";
import { createMockDatasetWithPaging } from "../../fixtures/mockDataset";

describe("useDatasetMode", () => {
  it("supports paging", () => {
    const ds = createMockDatasetWithPaging();
    const { result } = renderHook(() => useDatasetMode({ dataset: ds } as any));
    expect(result.current.hasMore).toBe(true);
    act(() => result.current.loadMore());
    expect(ds.paging.loadNextPage).toHaveBeenCalled();
  });
});
```

## Integration Testing
### Dataset Binding Integration
```typescript
// __tests__/integration/DatasetBinding.test.ts
describe("Dataset Binding", () => {
  it("renders records", () => {
    const container = document.createElement("div");
    const comp = new UniversalDataset();
    const ctx = createMockContext({ dataset: createMockDataset("sprk_document") });
    comp.init(ctx, jest.fn(), {}, container);
    comp.updateView(ctx);
    expect(container.querySelectorAll("[role='row']").length).toBeGreaterThan(0);
  });
});
```

### Command Execution Testing
```typescript
// __tests__/integration/CommandExecution.test.ts
describe("Commands", () => {
  it("deletes selected records", async () => {
    const mockWebAPI = { deleteRecord: jest.fn().mockResolvedValue({}) };
    const context: any = { webAPI: mockWebAPI, refresh: jest.fn(), selectedRecords: [{ id: "1", entityName: "sprk_document" }] };
    const exec = new CommandExecutor(new CommandRegistry(), new UIService());
    await exec.execute("delete", context);
    expect(mockWebAPI.deleteRecord).toHaveBeenCalledWith("sprk_document", "1");
  });
});
```

## E2E Testing
### Playwright Test Example
```typescript
// __tests__/e2e/DocumentGrid.e2e.ts
import { test, expect } from "@playwright/test";

test("grid supports sorting, selection, delete", async ({ page }) => {
  await page.goto("/test-harness");
  await page.waitForSelector("[data-testid='dataset-grid']");

  await page.getByRole("columnheader", { name: "Name" }).click();
  const firstRow = page.getByRole("row").nth(1);
  await firstRow.click();
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(firstRow).not.toBeVisible();
});

test("virtualization limits rows in DOM", async ({ page }) => {
  await page.goto("/test-harness");
  await page.evaluate(() => (window as any).testHarness.loadRecords(10000));
  const count = await page.locator("[role='row']").count();
  expect(count).toBeLessThan(100);
});
```

## Performance Testing
### Performance Benchmarks
```typescript
// __tests__/performance/RenderPerformance.test.ts
describe("Performance", () => {
  it("renders 1000 rows < 500ms", () => {
    const start = performance.now();
    renderWithFluent(<UniversalDatasetGrid dataset={createMockDataset("t", [], generateMockRecords(1000))} /> as any);
    const dt = performance.now() - start;
    expect(dt).toBeLessThan(500);
  });
});
```

## Test Utilities
### Mock Factories
```typescript
// __tests__/fixtures/mockDataset.ts
export function createMockDataset(entityName = "test_entity", columns: any[] = [], records: any[] = []): ComponentFramework.PropertyTypes.DataSet {
  const map = Object.fromEntries(records.map(r => [r.id, r]));
  return {
    loading: false,
    columns,
    records: map,
    sortedRecordIds: records.map(r => r.id),
    paging: { pageSize: 25, hasNextPage: false, hasPreviousPage: false, loadNextPage: jest.fn(), loadPreviousPage: jest.fn(), reset: jest.fn(), setPageSize: jest.fn(), totalResultCount: records.length },
    sorting: [],
    filtering: { getFilter: jest.fn(), setFilter: jest.fn() },
    linking: {},
    getTargetEntityType: () => entityName,
    refresh: jest.fn(),
    openDatasetItem: jest.fn(),
    getTitle: () => "Test Dataset",
    getViewId: () => "test-view-id"
  } as any;
}
```

## Coverage Requirements
### Minimum Coverage Targets
| Type | Target | Required |
|------|--------|----------|
| Statements | 80% | Yes |
| Branches | 75% | Yes |
| Functions | 80% | Yes |
| Lines | 80% | Yes |
| E2E Scenarios | 100% | Yes |

### Critical Path Coverage
Must have 100% coverage for:
- Entity detection logic
- Security checks
- Command execution
- Data transformation
- Error boundaries

## AI Coding Prompt
Author a complete test suite:
- Unit tests for components (grid renders, view modes), hooks (dataset/headless paging), renderers, and services.
- Integration tests for dataset binding, navigation, and command execution; simulate selection and paging.
- E2E tests with Playwright: sort, select, execute delete, verify virtualization (<100 DOM rows for 10k items).
- Performance tests: render 1k rows <500ms; scrolling maintains >30fps in CI.
- Provide fixtures and helpers for dataset/context mocks; wrap UI tests with a `FluentProvider` test util.
Targets: statements 80, branches 75, functions 80, lines 80; 100% on critical paths.
