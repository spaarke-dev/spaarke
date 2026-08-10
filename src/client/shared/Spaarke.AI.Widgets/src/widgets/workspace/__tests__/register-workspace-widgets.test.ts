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
 *
 * `communications-list` — upgrade-in-place identity (messaging-communication-app-r3
 * task 031, FR-14a / NFR-06): a dedicated describe block below asserts the
 * registered type string and section id are UNCHANGED after the
 * `CommunicationsWorkspaceWidget` body swap (Pattern D DataGrid →
 * `ConversationWorkspace`/`ConversationView`), that no second registration was
 * added, and that the factory resolves to the REAL upgraded widget export
 * (not the GenericTextWidget fallback and not a re-implementation). Full
 * render-mount coverage of the upgraded body lives in the widget package's
 * own test suite (see that describe block's comment for why).
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

// ---------------------------------------------------------------------------
// Tests: `communications-list` upgrade-in-place identity (messaging-communication-app-r3
// task 031, FR-14a / NFR-06)
// ---------------------------------------------------------------------------
//
// `@spaarke/communication-components` is mapped to SOURCE by this package's
// jest.config.ts moduleNameMapper (`'^@spaarke/communication-components$'`),
// so `resolveWorkspaceWidget('communications-list')` below loads the REAL
// (upgraded) `CommunicationsWorkspaceWidget`, not a mock — these assertions
// exercise the actual registered factory, the actual widget type/section
// identity, and the actual upgraded body.

describe('communications-list — upgrade in place (task 031, FR-14a / NFR-06)', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it('the registered type string is exactly "communications-list" (unchanged)', () => {
    expect(registry.hasWorkspaceWidget('communications-list')).toBe(true);
  });

  it('registers communications-list exactly ONCE — no second widget/registry entry was added', () => {
    const types = registry.getAllWorkspaceWidgetTypes();
    const communicationsEntries = types.filter(t => t === 'communications-list' || /communication/i.test(t));
    expect(communicationsEntries).toEqual(['communications-list']);
  });

  it('metadata reflects the Messages relabel (messaging-r3 UAT 2026-07-27); category/allowMultiple unchanged', () => {
    const meta = registry.getWorkspaceWidgetMetadata('communications-list');
    expect(meta).toBeDefined();
    // Human-facing label is 'Messages' (the widget TYPE string stays 'communications-list').
    expect(meta!.displayName).toBe('Messages');
    expect(meta!.category).toBe('data');
    expect(meta!.allowMultiple).toBe(true);
  });

  it('resolves to the REAL upgraded CommunicationsWorkspaceWidget, not the GenericTextWidget fallback', async () => {
    const resolved = await registry.resolveWorkspaceWidget('communications-list');
    expect(resolved).not.toBeNull();
    expect(resolved).not.toBeUndefined();
    expect(resolved).not.toBe(MockGenericText);
  });

  it('resolves to the identical CommunicationsWorkspaceWidget component the upgraded widget package exports (identity, not a re-implementation)', async () => {
    // Full DOM-mount of the resolved component is intentionally NOT exercised
    // here: `@spaarke/communication-components` is mapped to SOURCE for this
    // package's Jest run, which pulls in its OWN `node_modules` copy of React/
    // Fluent — mounting it via THIS package's `react-dom` trips React's
    // single-instance invariant ("Invalid hook call") across the package
    // boundary. That full-mount coverage already exists, against the
    // package's OWN React instance, in
    // `Spaarke.Communication.Components/src/widgets/CommunicationsWorkspaceWidget/CommunicationsWorkspaceWidget.test.ts`
    // ("mounts the shared ConversationWorkspace two-pane shell"). This test's
    // job is narrower: prove the registry resolves to the SAME component
    // export the widget package publishes — not a stand-in, not the
    // GenericTextWidget fallback, and not a second/forked component.
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const widgetModule = require('@spaarke/communication-components');
    const resolved = await registry.resolveWorkspaceWidget('communications-list');

    expect(resolved).toBe(widgetModule.CommunicationsWorkspaceWidget);
    expect((resolved as React.ComponentType<unknown> & { displayName?: string }).displayName).toBe(
      'CommunicationsWorkspaceWidget'
    );
  });
});

// ---------------------------------------------------------------------------
// Tests: `contextType` closed set (FR-B1 + FR-C3, task 020)
// ---------------------------------------------------------------------------
//
// Covers the task-020 acceptance criteria:
//   - The union has exactly the six values (compile-time exhaustiveness guard
//     below — widening the union without updating the switch fails `tsc`).
//   - The email widget resolves to 'email' (FR-C3, BINDING).
//   - A widget with no honest fit (e.g. BudgetDashboard) resolves to none
//     (`undefined`), proving the field is additive/backward-compatible.

describe('contextType — closed set (task 020, FR-B1 + FR-C3)', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it('the WidgetContextType union has exactly these six values (compile-time exhaustiveness)', () => {
    // If a 7th value is ever added to WidgetContextType without updating this
    // switch, TypeScript fails to compile (the `default: assertNever(value)`
    // branch requires `value` to be typed `never`) — the closed-set
    // invariant is enforced at build time, not just documented here.
    function assertNever(x: never): never {
      throw new Error(`Unexpected WidgetContextType value: ${String(x)}`);
    }
    function exhaustiveCheck(value: import('../../../types/shared').WidgetContextType): string {
      switch (value) {
        case 'email':
          return 'email';
        case 'document':
          return 'document';
        case 'compose-doc':
          return 'compose-doc';
        case 'matter-grid':
          return 'matter-grid';
        case 'dashboard':
          return 'dashboard';
        case 'calendar':
          return 'calendar';
        default:
          return assertNever(value);
      }
    }
    const allSix: import('../../../types/shared').WidgetContextType[] = [
      'email',
      'document',
      'compose-doc',
      'matter-grid',
      'dashboard',
      'calendar',
    ];
    expect(allSix.map(exhaustiveCheck)).toEqual(allSix);
    expect(new Set(allSix).size).toBe(6);
  });

  it("the email widget's registered contextType is 'email' (FR-C3, BINDING)", () => {
    const meta = registry.getWorkspaceWidgetMetadata('email');
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBe('email');
  });

  it('a widget with no honest fit (BudgetDashboard) resolves to none (undefined)', () => {
    const meta = registry.getWorkspaceWidgetMetadata('BudgetDashboard');
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBeUndefined();
  });

  it("matters-list (a Dataverse entity-view grid) resolves to 'matter-grid'", () => {
    const meta = registry.getWorkspaceWidgetMetadata('matters-list');
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBe('matter-grid');
  });

  it("workspace (the workspace-layout dispatcher) resolves to 'dashboard'", () => {
    const meta = registry.getWorkspaceWidgetMetadata('workspace');
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBe('dashboard');
  });

  it('every registered widget carries either a valid contextType or none (undefined) — never an invalid string', () => {
    const validValues = new Set(['email', 'document', 'compose-doc', 'matter-grid', 'dashboard', 'calendar']);
    for (const type of registry.getAllWorkspaceWidgetTypes()) {
      const meta = registry.getWorkspaceWidgetMetadata(type);
      const contextType = meta?.contextType;
      if (contextType !== undefined) {
        expect(validValues.has(contextType)).toBe(true);
      }
    }
  });
});

// ---------------------------------------------------------------------------
// Tests: assistantContract — Assistant-contract metadata SHAPE
// (FR-08 + FR-15 SHAPE, R3 task 022)
// ---------------------------------------------------------------------------
//
// Covers task 022's acceptance criteria:
//   - Every overview-only surface (grids + 'workspace' dashboard, hosting
//     Daily Briefing/Calendar) declares an overview tool and NO per-item
//     cards.
//   - The 'email' widget declares per-item cards Reply/Reply All/Forward/
//     Summarize the thread.
//   - A widget with no declared contract resolves deterministically to
//     `undefined` (the default/none case) — never a partial/garbage value.
//   - No card label/tool/landing carries item content (a static string set,
//     never data pulled from a live record).

describe('assistantContract — Assistant-contract metadata SHAPE (task 022, FR-08 + FR-15)', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  const OVERVIEW_ONLY_WIDGETS = [
    'workspace',
    'documents-list',
    'matters-list',
    'projects-list',
    'invoices-list',
    'work-assignments-list',
    'my-tasks-list',
    'communications-list',
  ] as const;

  it.each(OVERVIEW_ONLY_WIDGETS)('%s declares ONE overview tool and NO per-item cards (respond pattern)', type => {
    const meta = registry.getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    const contract = meta!.assistantContract;
    expect(contract).toBeDefined();
    expect(contract!.overviewTools).toEqual(['overview_query']);
    expect(contract!.perItemCards).toEqual([]);
    expect(contract!.interactionPattern).toBe('respond');
  });

  it('every overview-only widget references the SAME overview tool name (NFR-06 — one parameterized tool, not N handlers)', () => {
    const toolNames = new Set(
      OVERVIEW_ONLY_WIDGETS.map(type => registry.getWorkspaceWidgetMetadata(type)!.assistantContract!.overviewTools[0])
    );
    expect(toolNames.size).toBe(1);
  });

  it("the 'email' widget declares per-item cards Reply/Reply All/Forward/Summarize the thread (FR-09/FR-10)", () => {
    const meta = registry.getWorkspaceWidgetMetadata('email');
    expect(meta).toBeDefined();
    const contract = meta!.assistantContract;
    expect(contract).toBeDefined();
    expect(contract!.overviewTools).toEqual([]);
    expect(contract!.perItemCards.map(c => c.label)).toEqual(['Reply', 'Reply All', 'Forward', 'Summarize the thread']);
    expect(contract!.interactionPattern).toBe('hybrid');
  });

  it("email's Reply/Reply All/Forward cards land on the composer; Summarize lands in chat", () => {
    const contract = registry.getWorkspaceWidgetMetadata('email')!.assistantContract!;
    const byLabel = new Map(contract.perItemCards.map(c => [c.label, c]));
    expect(byLabel.get('Reply')!.landing).toBe('composer');
    expect(byLabel.get('Reply All')!.landing).toBe('composer');
    expect(byLabel.get('Forward')!.landing).toBe('composer');
    expect(byLabel.get('Summarize the thread')!.landing).toBe('chat');
  });

  it('Reply and Reply All invoke the SAME catalog tool (draft_reply — mode is a call-time argument, not a separate tool)', () => {
    const contract = registry.getWorkspaceWidgetMetadata('email')!.assistantContract!;
    const byLabel = new Map(contract.perItemCards.map(c => [c.label, c.tool]));
    expect(byLabel.get('Reply')).toBe('draft_reply');
    expect(byLabel.get('Reply All')).toBe('draft_reply');
    expect(byLabel.get('Forward')).toBe('draft_forward');
    expect(byLabel.get('Summarize the thread')).toBe('summarize_thread');
  });

  it('a widget with no declared contract resolves deterministically to undefined (default/none — e.g. BudgetDashboard)', () => {
    const meta = registry.getWorkspaceWidgetMetadata('BudgetDashboard');
    expect(meta).toBeDefined();
    expect(meta!.assistantContract).toBeUndefined();
  });

  it('no per-item card label, tool name, or landing tag carries item content (ADR-015) — every value is a static, non-empty string from a small closed set', () => {
    for (const type of registry.getAllWorkspaceWidgetTypes()) {
      const contract = registry.getWorkspaceWidgetMetadata(type)?.assistantContract;
      if (!contract) continue;
      for (const card of contract.perItemCards) {
        // Labels/tool names are short, static UI strings — never a GUID,
        // email subject, or free-text snippet (which would indicate live
        // record content leaking into registration metadata).
        expect(card.label.length).toBeGreaterThan(0);
        expect(card.label.length).toBeLessThan(60);
        expect(card.tool).toMatch(/^[a-z_]+$/);
        expect(['chat', 'composer', 'compose']).toContain(card.landing);
      }
    }
  });

  it('the shape is type-safe: a WidgetAssistantContract object literal missing a required field fails to typecheck', () => {
    // Compile-time proof (not a runtime assertion) — this function body is
    // never called; it exists so `tsc`/ts-jest fails the FILE if the
    // @ts-expect-error directives below stop being errors (i.e. if the
    // fields they annotate ever become optional by accident).
    function _typeSafetyFixture(): void {
      // @ts-expect-error — overviewTools is a required member.
      const _missingOverviewTools: import('../../../types/shared').WidgetAssistantContract = {
        perItemCards: [],
        interactionPattern: 'respond',
      };
      // @ts-expect-error — perItemCards is a required member.
      const _missingPerItemCards: import('../../../types/shared').WidgetAssistantContract = {
        overviewTools: [],
        interactionPattern: 'respond',
      };
      // @ts-expect-error — interactionPattern is a required member.
      const _missingInteractionPattern: import('../../../types/shared').WidgetAssistantContract = {
        overviewTools: [],
        perItemCards: [],
      };
      // @ts-expect-error — interactionPattern must be one of the closed set.
      const _invalidPattern: import('../../../types/shared').WidgetAssistantContract = {
        overviewTools: [],
        perItemCards: [],
        interactionPattern: 'not-a-real-pattern',
      };
      void _missingOverviewTools;
      void _missingPerItemCards;
      void _missingInteractionPattern;
      void _invalidPattern;
    }
    expect(typeof _typeSafetyFixture).toBe('function');
  });
});

// ---------------------------------------------------------------------------
// Tests: getWidgetContextTypeMap / getWidgetAssistantContract — derived
// registry accessors (FR-08, task 022)
// ---------------------------------------------------------------------------

describe('getWidgetContextTypeMap / getWidgetAssistantContract (task 022 registry accessors)', () => {
  beforeEach(() => {
    loadRegistrations();
  });

  it('getWidgetContextTypeMap() is derived from the live registry — matches getWorkspaceWidgetMetadata() per type', () => {
    const map = registry.getWidgetContextTypeMap();
    for (const type of registry.getAllWorkspaceWidgetTypes()) {
      expect(map[type]).toBe(registry.getWorkspaceWidgetMetadata(type)?.contextType);
    }
  });

  it("getWidgetContextTypeMap() reports 'email' → 'email' and 'matters-list' → 'matter-grid'", () => {
    const map = registry.getWidgetContextTypeMap();
    expect(map['email']).toBe('email');
    expect(map['matters-list']).toBe('matter-grid');
  });

  it('getWidgetAssistantContract() returns the same object as metadata.assistantContract', () => {
    expect(registry.getWidgetAssistantContract('email')).toBe(
      registry.getWorkspaceWidgetMetadata('email')?.assistantContract
    );
  });

  it('getWidgetAssistantContract() returns undefined for an unregistered type', () => {
    expect(registry.getWidgetAssistantContract('__not_a_real_widget__')).toBeUndefined();
  });
});
