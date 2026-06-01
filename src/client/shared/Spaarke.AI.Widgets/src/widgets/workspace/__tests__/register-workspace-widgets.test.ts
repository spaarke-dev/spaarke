/**
 * register-workspace-widgets — integration tests
 *
 * Verifies that all 7 R1 output widgets are correctly registered in
 * WorkspaceWidgetRegistry with the expected metadata. These tests exercise
 * the registration layer only — rendering is covered by the wrapper tests.
 *
 * Covered assertions:
 *   - All 7 widget types are registered after importing the module.
 *   - Each registration carries the correct displayName and category.
 *   - allowMultiple and defaultOrder are set appropriately.
 *   - resolveWorkspaceWidget() returns a non-null component for each type
 *     (the lazy factory resolves to the WorkspaceWidgetWrapper HOC).
 *   - Unknown types still fall back to GenericTextWidget (registry contract
 *     is preserved after the registrations run).
 */

import React from 'react';
import {
  clearWorkspaceRegistry,
  getWorkspaceWidgetMetadata,
  hasWorkspaceWidget,
  resolveWorkspaceWidget,
  getAllWorkspaceWidgetTypes,
} from '../../../registry/WorkspaceWidgetRegistry';

// ---------------------------------------------------------------------------
// Mock GenericTextWidget (required by WorkspaceWidgetRegistry fallback path)
// ---------------------------------------------------------------------------

const MockGenericText: React.FC = () => null;
MockGenericText.displayName = 'MockGenericTextWidget';

jest.mock('../../../widgets/GenericTextWidget', () => ({
  __esModule: true,
  default: MockGenericText,
}));

// ---------------------------------------------------------------------------
// Mock the @spaarke/ai-outputs widget modules loaded by the wrapper factories.
// Each module must export a default React component.
// ---------------------------------------------------------------------------

const createMockWidget = (name: string): React.FC => {
  const comp: React.FC = () => null;
  comp.displayName = name;
  return comp;
};

jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/BudgetDashboardWidget',
  () => ({ __esModule: true, default: createMockWidget('BudgetDashboardWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/SearchResultsWidget',
  () => ({ __esModule: true, default: createMockWidget('SearchResultsWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/AnalysisEditorWidget',
  () => ({ __esModule: true, default: createMockWidget('AnalysisEditorWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/ContractComparisonWidget',
  () => ({ __esModule: true, default: createMockWidget('ContractComparisonWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/StatusSummaryWidget',
  () => ({ __esModule: true, default: createMockWidget('StatusSummaryWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/RecommendationWidget',
  () => ({ __esModule: true, default: createMockWidget('RecommendationWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/src/output-widgets/ActionPlanWidget',
  () => ({ __esModule: true, default: createMockWidget('ActionPlanWidget') }),
  { virtual: true }
);

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearWorkspaceRegistry();
  // Re-import the registration module after each clear so the registrations
  // run fresh. Jest module cache is reset between describe blocks via
  // jest.resetModules() in afterEach.
  jest.resetModules();
});

// ---------------------------------------------------------------------------
// Helper: run registrations in a fresh module scope
// ---------------------------------------------------------------------------

function loadRegistrations(): void {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../register-workspace-widgets');
}

// ---------------------------------------------------------------------------
// Expected widget configuration
// ---------------------------------------------------------------------------

const EXPECTED_WIDGETS = [
  {
    type: 'BudgetDashboard',
    displayName: 'Budget Dashboard',
    category: 'financial',
    allowMultiple: false,
    defaultOrder: 10,
  },
  {
    type: 'SearchResults',
    displayName: 'Search Results',
    category: 'search',
    allowMultiple: true,
    defaultOrder: 20,
  },
  {
    type: 'AnalysisEditor',
    displayName: 'Analysis Editor',
    category: 'analysis',
    allowMultiple: true,
    defaultOrder: 30,
  },
  {
    type: 'ContractComparison',
    displayName: 'Contract Comparison',
    category: 'document',
    allowMultiple: true,
    defaultOrder: 40,
  },
  {
    type: 'StatusSummary',
    displayName: 'Status Summary',
    category: 'status',
    allowMultiple: false,
    defaultOrder: 50,
  },
  {
    type: 'Recommendation',
    displayName: 'Recommendations',
    category: 'recommendation',
    allowMultiple: false,
    defaultOrder: 60,
  },
  {
    type: 'ActionPlan',
    displayName: 'Action Plan',
    category: 'planning',
    allowMultiple: false,
    defaultOrder: 70,
  },
] as const;

// ---------------------------------------------------------------------------
// Tests: registration presence
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidgets — registration presence', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it('registers all 7 widget types', () => {
    const types = getAllWorkspaceWidgetTypes();
    expect(types).toHaveLength(7);
  });

  it.each(EXPECTED_WIDGETS)('registers $type', ({ type }) => {
    expect(hasWorkspaceWidget(type)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Tests: metadata correctness
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidgets — metadata', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it.each(EXPECTED_WIDGETS)('$type has correct displayName and category', ({ type, displayName, category }) => {
    const meta = getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe(displayName);
    expect(meta!.category).toBe(category);
  });

  it.each(EXPECTED_WIDGETS)(
    '$type has correct allowMultiple and defaultOrder',
    ({ type, allowMultiple, defaultOrder }) => {
      const meta = getWorkspaceWidgetMetadata(type);
      expect(meta).toBeDefined();
      expect(meta!.allowMultiple).toBe(allowMultiple);
      expect(meta!.defaultOrder).toBe(defaultOrder);
    }
  );

  it('defaultOrder values are unique across all 7 widgets', () => {
    loadRegistrations();
    const orders = EXPECTED_WIDGETS.map(w => getWorkspaceWidgetMetadata(w.type)!.defaultOrder);
    const unique = new Set(orders);
    expect(unique.size).toBe(EXPECTED_WIDGETS.length);
  });

  it('defaultOrder values are ordered correctly (10, 20, 30, ...)', () => {
    const orders = EXPECTED_WIDGETS.map(w => w.defaultOrder);
    for (let i = 1; i < orders.length; i++) {
      expect(orders[i]).toBeGreaterThan(orders[i - 1]);
    }
  });
});

// ---------------------------------------------------------------------------
// Tests: factory resolution
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidgets — factory resolution', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it.each(EXPECTED_WIDGETS)('$type resolves to a non-null component (lazy factory works)', async ({ type }) => {
    const resolved = await resolveWorkspaceWidget(type);
    expect(resolved).not.toBeNull();
    expect(resolved).not.toBeUndefined();
  });

  it('unknown widget type still falls back to GenericTextWidget', async () => {
    loadRegistrations();
    const resolved = await resolveWorkspaceWidget('__not_a_real_widget__');
    expect(resolved).toBe(MockGenericText);
  });
});

// ---------------------------------------------------------------------------
// Tests: idempotency — calling registerWorkspaceWidgets() twice is safe
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidgets — idempotency', () => {
  it('second import does not throw (first-wins silently ignores duplicates)', () => {
    expect(() => {
      loadRegistrations();
      // Loading again simulates a double-import scenario.
      // WorkspaceWidgetRegistry silently ignores duplicate registrations.
      loadRegistrations();
    }).not.toThrow();
  });

  it('still has 7 widgets after double registration', () => {
    loadRegistrations();
    loadRegistrations();
    expect(getAllWorkspaceWidgetTypes()).toHaveLength(7);
  });
});
