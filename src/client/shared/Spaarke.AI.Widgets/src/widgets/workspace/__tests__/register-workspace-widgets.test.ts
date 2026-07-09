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
import type * as WorkspaceWidgetRegistryModule from '../../../registry/WorkspaceWidgetRegistry';

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

// NOTE (test-repair task 021, 2026-07-08): these mock paths previously read
// '@spaarke/ai-outputs/src/output-widgets/...' — an extra '/src/' segment
// that does NOT match the literal specifier register-workspace-widgets.ts
// actually imports ('@spaarke/ai-outputs/output-widgets/...', no '/src/').
// Because the paths never matched, none of these virtual mocks ever
// intercepted the real dynamic import; every factory call below silently
// failed to resolve and resolveWorkspaceWidget() caught the error and fell
// back to GenericTextWidget (see WorkspaceWidgetRegistry.ts's catch block).
// The "resolves to a non-null component" assertions were passing vacuously
// against the fallback, not the real widget. Corrected to the real import
// path so these tests exercise actual factory resolution.
jest.mock(
  '@spaarke/ai-outputs/output-widgets/BudgetDashboardWidget',
  () => ({ __esModule: true, default: createMockWidget('BudgetDashboardWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/SearchResultsWidget',
  () => ({ __esModule: true, default: createMockWidget('SearchResultsWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/AnalysisEditorWidget',
  () => ({ __esModule: true, default: createMockWidget('AnalysisEditorWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/ContractComparisonWidget',
  () => ({ __esModule: true, default: createMockWidget('ContractComparisonWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/StatusSummaryWidget',
  () => ({ __esModule: true, default: createMockWidget('StatusSummaryWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/RecommendationWidget',
  () => ({ __esModule: true, default: createMockWidget('RecommendationWidget') }),
  { virtual: true }
);
jest.mock(
  '@spaarke/ai-outputs/output-widgets/ActionPlanWidget',
  () => ({ __esModule: true, default: createMockWidget('ActionPlanWidget') }),
  { virtual: true }
);

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------
//
// NOTE (test-repair task 021, 2026-07-08): this file previously statically
// `import`-ed the registry accessor functions (getWorkspaceWidgetMetadata,
// hasWorkspaceWidget, resolveWorkspaceWidget, getAllWorkspaceWidgetTypes,
// clearWorkspaceRegistry) at the top of the file, while also calling
// `jest.resetModules()` in a top-level beforeEach before re-requiring
// '../register-workspace-widgets' via loadRegistrations(). Once
// jest.resetModules() clears the module registry, the NEXT require() of
// WorkspaceWidgetRegistry — the one register-workspace-widgets.ts performs
// internally — creates a FRESH module instance with its own empty `_registry`
// Map. But the test file's statically-imported accessor functions remain
// bound to the ORIGINAL (pre-reset) module instance forever, since ES
// `import` bindings are resolved once at file-load time and never
// re-evaluated. Every registration written by loadRegistrations() landed in
// the fresh instance; every assertion read from the stale original instance
// — so `getWorkspaceWidgetMetadata()` always returned undefined and
// `getAllWorkspaceWidgetTypes()` always returned []. This was a genuine test
// defect (a module-instance split caused by mixing jest.resetModules() with
// static imports of the module under test), not a production regression.
//
// Fix: re-require the registry module itself inside loadRegistrations() and
// route every assertion through the freshly-required `registry` reference so
// both the registration module and the accessor calls always share the same
// post-reset module instance.
let registry: typeof WorkspaceWidgetRegistryModule;

function loadRegistrations(): void {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  registry = require('../../../registry/WorkspaceWidgetRegistry');
  // Re-requiring the registration module after resetModules() runs its
  // top-level registerWorkspaceWidget() side effects fresh, against the SAME
  // module instance `registry` now points to (Node's require cache resolves
  // both requires to the one instance created since the last resetModules()).
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

  it('registers all 7 R1 output widget types (subset of the full registry)', () => {
    // NOTE (test-repair task 021, 2026-07-08): register-workspace-widgets.ts
    // now also registers ~16 wizard/utility widgets (RedlineViewer,
    // CreateMatterWizard, DocumentUploadWizard, etc. — added by task 085,
    // "Round 4 Fix 2" per ContextPaneController.tsx's provenance comment) in
    // the SAME file this test covers. The registry total is no longer 7 (see
    // widget-serialize-restore.test.ts's "total registered widgets ... is 23"
    // for the current whole-registry count) — asserting an exact total here
    // was stale. This test's actual job is verifying the 7 R1 output widgets
    // are present, so assert subset containment instead of exact length.
    const types = registry.getAllWorkspaceWidgetTypes();
    for (const w of EXPECTED_WIDGETS) {
      expect(types).toContain(w.type);
    }
  });

  it.each(EXPECTED_WIDGETS)('registers $type', ({ type }) => {
    expect(registry.hasWorkspaceWidget(type)).toBe(true);
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
    const meta = registry.getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe(displayName);
    expect(meta!.category).toBe(category);
  });

  it.each(EXPECTED_WIDGETS)(
    '$type has correct allowMultiple and defaultOrder',
    ({ type, allowMultiple, defaultOrder }) => {
      const meta = registry.getWorkspaceWidgetMetadata(type);
      expect(meta).toBeDefined();
      expect(meta!.allowMultiple).toBe(allowMultiple);
      expect(meta!.defaultOrder).toBe(defaultOrder);
    }
  );

  it('defaultOrder values are unique across all 7 widgets', () => {
    loadRegistrations();
    const orders = EXPECTED_WIDGETS.map(w => registry.getWorkspaceWidgetMetadata(w.type)!.defaultOrder);
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
    const resolved = await registry.resolveWorkspaceWidget(type);
    expect(resolved).not.toBeNull();
    expect(resolved).not.toBeUndefined();
    // Must be the REAL registered factory's component, not the
    // GenericTextWidget fallback — a factory-resolution failure (e.g. a mock
    // path mismatch) would otherwise pass this test vacuously via the catch
    // block in WorkspaceWidgetRegistry.resolveWorkspaceWidget().
    expect(resolved).not.toBe(MockGenericText);
  });

  it('unknown widget type still falls back to GenericTextWidget', async () => {
    loadRegistrations();
    const resolved = await registry.resolveWorkspaceWidget('__not_a_real_widget__');
    expect(resolved).toBe(MockGenericText);
  });
});

// ---------------------------------------------------------------------------
// Tests: idempotency — calling registerWorkspaceWidgets() twice is safe
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidgets — idempotency', () => {
  // NOTE (test-repair task 021, 2026-07-08): re-requiring
  // '../register-workspace-widgets' a second time WITHOUT jest.resetModules()
  // is a no-op under CommonJS's require cache — the module body (and its
  // registerWorkspaceWidget() calls) simply does not re-execute, so it never
  // actually exercised the "duplicate registration" branch this describe
  // block is meant to guard. These tests now call
  // registry.registerWorkspaceWidget() directly a second time for an
  // already-registered type, which is the real "double-import" scenario the
  // original comment described — see WorkspaceWidgetRegistry.ts's
  // `if (_registry.has(type)) { ...warn...; return; }` first-wins guard.
  beforeEach(() => {
    loadRegistrations();
  });

  it('second registration of an existing type does not throw (first-wins silently ignores duplicates)', () => {
    expect(() => {
      registry.registerWorkspaceWidget('BudgetDashboard', registry.getWorkspaceWidgetMetadata('BudgetDashboard')!, () =>
        Promise.resolve({ default: MockGenericText })
      );
    }).not.toThrow();
  });

  it('does not grow the registry after a duplicate registration attempt', () => {
    // NOTE (test-repair task 021): the full registry holds more than the 7
    // R1 output widgets (see the "registers all 7 R1 output widget types"
    // test above) — assert the count is unchanged by the duplicate attempt
    // rather than hardcoding a stale total.
    const before = registry.getAllWorkspaceWidgetTypes().length;
    registry.registerWorkspaceWidget('BudgetDashboard', registry.getWorkspaceWidgetMetadata('BudgetDashboard')!, () =>
      Promise.resolve({ default: MockGenericText })
    );
    expect(registry.getAllWorkspaceWidgetTypes()).toHaveLength(before);
  });

  it('keeps the original factory on a duplicate registration attempt (first-wins)', async () => {
    // Attempt to overwrite BudgetDashboard's factory with one that resolves
    // to MockGenericText — the first-wins guard should ignore this, so
    // resolving BudgetDashboard afterward still returns the ORIGINAL
    // (non-fallback) component, not MockGenericText.
    registry.registerWorkspaceWidget('BudgetDashboard', registry.getWorkspaceWidgetMetadata('BudgetDashboard')!, () =>
      Promise.resolve({ default: MockGenericText })
    );

    const resolved = await registry.resolveWorkspaceWidget('BudgetDashboard');
    expect(resolved).not.toBe(MockGenericText);
  });
});
