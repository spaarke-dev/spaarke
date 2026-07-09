/**
 * widget-serialize-restore.test.ts
 *
 * Integration tests verifying that all workspace and context widgets are
 * correctly registered in their respective registries with valid metadata
 * and resolvable component factories.
 *
 * Covers:
 * - All 11 workspace widget types are registered and resolvable.
 * - All 10 context widget types are registered and resolvable.
 * - Workspace widget metadata includes displayName for every type.
 * - Workspace widget components are valid React component types.
 * - Context widget factories return non-null components.
 * - Unknown types produce the correct fallback (GenericTextWidget for
 *   workspace, null for context).
 *
 * These tests exercise the registration layer only — component rendering
 * and serialize/restore lifecycle are covered by WorkspaceWidgetWrapper
 * and ContextWidgetAdapter tests respectively.
 */

import React from 'react';
import type * as WorkspaceWidgetRegistryModule from '../registry/WorkspaceWidgetRegistry';
import type * as ContextWidgetRegistryModule from '../registry/ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Mock GenericTextWidget (workspace fallback)
// ---------------------------------------------------------------------------

const MockGenericText: React.FC = () => null;
MockGenericText.displayName = 'MockGenericTextWidget';

jest.mock('../widgets/GenericTextWidget', () => ({
  __esModule: true,
  default: MockGenericText,
}));

// ---------------------------------------------------------------------------
// Mock R1 output widget modules (workspace widgets 1-7)
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
// Corrected to the real import path (see the identical fix + rationale in
// register-workspace-widgets.test.ts).
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
// Mock R2-native workspace widget modules (widgets 8-11)
// ---------------------------------------------------------------------------

jest.mock('../widgets/workspace/RedlineViewerWidget', () => ({
  __esModule: true,
  default: createMockWidget('RedlineViewerWidget'),
}));
jest.mock('../widgets/workspace/CreateMatterWizardWidget', () => ({
  __esModule: true,
  default: createMockWidget('CreateMatterWizardWidget'),
}));
jest.mock('../widgets/workspace/DocumentUploadWizardWidget', () => ({
  __esModule: true,
  default: createMockWidget('DocumentUploadWizardWidget'),
}));
jest.mock('../widgets/workspace/SearchSelectWizardWidget', () => ({
  __esModule: true,
  default: createMockWidget('SearchSelectWizardWidget'),
}));

// ---------------------------------------------------------------------------
// Mock context widget modules
// ---------------------------------------------------------------------------

jest.mock('../widgets/context/DocumentViewerContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('DocumentViewerContextWidget'),
}));
jest.mock('../widgets/context/WebSourceContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('WebSourceContextWidget'),
}));
jest.mock('../widgets/context/LegalLibraryContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('LegalLibraryContextWidget'),
}));
jest.mock('../widgets/context/CitationContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('CitationContextWidget'),
}));
jest.mock('../widgets/context/ImageViewerContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('ImageViewerContextWidget'),
}));
jest.mock('../widgets/context/CodeViewerContextWidget', () => ({
  __esModule: true,
  default: createMockWidget('CodeViewerContextWidget'),
}));
jest.mock('../widgets/context/ProgressTrackerWidget', () => ({
  __esModule: true,
  default: createMockWidget('ProgressTrackerWidget'),
}));
jest.mock('../widgets/context/PlaybookGalleryWidget', () => ({
  __esModule: true,
  default: createMockWidget('PlaybookGalleryWidget'),
}));
jest.mock('../widgets/context/EntityInfoWidget', () => ({
  __esModule: true,
  default: createMockWidget('EntityInfoWidget'),
}));
jest.mock('../widgets/context/FindingsWidget', () => ({
  __esModule: true,
  default: createMockWidget('FindingsWidget'),
}));
jest.mock('../widgets/context/PinnedMemoryListWidget', () => ({
  __esModule: true,
  default: createMockWidget('PinnedMemoryListWidget'),
}));

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

// NOTE (test-repair task 021, 2026-07-08): this file previously
// statically `import`-ed the registry accessor functions while calling
// `jest.resetModules()` in beforeEach — a module-instance split identical to
// the bug fixed in register-workspace-widgets.test.ts (see that file's
// detailed comment). Every registration written by loadWorkspaceRegistrations()
// / loadContextRegistrations() landed in a FRESH post-reset module instance,
// while the statically-imported accessor functions stayed bound to the
// original (pre-reset) instance, so every assertion read stale/empty state.
// Fixed by re-requiring both registry modules inside the load* helpers and
// routing all assertions through the freshly-required references.
let workspaceRegistry: typeof WorkspaceWidgetRegistryModule;
let contextRegistry: typeof ContextWidgetRegistryModule;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function loadWorkspaceRegistrations(): void {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  workspaceRegistry = require('../registry/WorkspaceWidgetRegistry');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-workspace-widgets');
}

function loadContextRegistrations(): void {
  jest.resetModules();
  // ONE registration module covers ALL context widget types (task 046
  // registry dedupe — shell context widgets + R1 source widgets).
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  contextRegistry = require('../registry/ContextWidgetRegistry');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../registry/register-context-widgets');
}

// ---------------------------------------------------------------------------
// Expected workspace widget types
// ---------------------------------------------------------------------------

const EXPECTED_WORKSPACE_WIDGETS = [
  {
    type: 'BudgetDashboard',
    displayName: 'Budget Dashboard',
    category: 'financial',
  },
  {
    type: 'SearchResults',
    displayName: 'Search Results',
    category: 'search',
  },
  {
    type: 'AnalysisEditor',
    displayName: 'Analysis Editor',
    category: 'analysis',
  },
  {
    type: 'ContractComparison',
    displayName: 'Contract Comparison',
    category: 'document',
  },
  {
    type: 'StatusSummary',
    displayName: 'Status Summary',
    category: 'status',
  },
  {
    type: 'Recommendation',
    displayName: 'Recommendations',
    category: 'recommendation',
  },
  {
    type: 'ActionPlan',
    displayName: 'Action Plan',
    category: 'planning',
  },
  {
    type: 'redline-viewer',
    displayName: 'Document Comparison',
    category: 'document',
  },
  {
    type: 'create-matter-wizard',
    displayName: 'Create Matter Wizard',
    category: 'wizard',
  },
  {
    type: 'document-upload-wizard',
    displayName: 'Upload Documents',
    category: 'wizard',
  },
  {
    type: 'search-select-wizard',
    displayName: 'Search & Select',
    category: 'wizard',
  },
] as const;

// ---------------------------------------------------------------------------
// Expected context widget types
// ---------------------------------------------------------------------------

const EXPECTED_CONTEXT_WIDGETS = [
  'DocumentViewer',
  'WebSource',
  'LegalLibrary',
  'Citation',
  'ImageViewer',
  'CodeViewer',
  'progress-tracker',
  'playbook-gallery',
  'entity-info',
  'findings',
  // R6 task 062 / D-C-15: ExecutionTraceWidget — Claude-Code-like activity
  // timeline. Subscribes to the six `context.*` trace event types added by
  // R6 task 059 (D-C-12). Per ADR-015 BINDING: renders only typed enumerated
  // fields (tool name + decision + timestamp + numeric metrics).
  'execution-trace',
  // R6 task 070 / D-C-24 + D-C-25: PinnedMemoryListWidget — Q7 scope expansion
  // (Pillar 7). Visualises + manages cross-session pinned memory items. Loads
  // via GET /api/memory/pins and supports create / edit / delete.
  'pinned-memory-list',
] as const;

// ===========================================================================
// Workspace Widget Registration Tests
// ===========================================================================

describe('Workspace widget serialize/restore — registration', () => {
  // NOTE (test-repair task 021, 2026-07-08): switched from beforeEach to
  // beforeAll. Every registration call in this file triggers
  // jest.resetModules(), which forces re-instantiation of the entire
  // @spaarke/ui-components / @spaarke/ai-outputs dependency graph (including
  // the full @fluentui/react-icons barrel) on every call. With beforeEach,
  // the ~34 tests across this file's 6 describe blocks each re-triggered
  // that reload, compounding to a heap-exhaustion crash ("JavaScript heap
  // out of memory"). None of the tests in this describe block mutate
  // registry state that a sibling test depends on, so loading once per
  // describe block (beforeAll) is safe and cuts resetModules() cycles from
  // ~34 to 6 for the whole file.
  beforeAll(() => {
    loadWorkspaceRegistrations();
  });

  // NOTE (test-repair task 021, 2026-07-08): register-workspace-widgets.ts
  // now registers more than the 11 widgets originally enumerated here (task
  // 085 and later work added several "list"/"dashboard" system widgets —
  // documents-list, projects-list, invoices-list, work-assignments-list,
  // communications-list, matters-dashboard — on top of the 11 tracked by
  // EXPECTED_WORKSPACE_WIDGETS). This test's job is verifying THESE 11 are
  // present, so assert subset containment instead of an exact total (see the
  // identical fix in register-workspace-widgets.test.ts).
  it('registers all 11 tracked workspace widget types (subset of the full registry)', () => {
    const types = workspaceRegistry.getAllWorkspaceWidgetTypes();
    for (const w of EXPECTED_WORKSPACE_WIDGETS) {
      expect(types).toContain(w.type);
    }
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type is registered in WorkspaceWidgetRegistry', ({ type }) => {
    expect(workspaceRegistry.hasWorkspaceWidget(type)).toBe(true);
  });
});

describe('Workspace widget serialize/restore — metadata', () => {
  beforeAll(() => {
    loadWorkspaceRegistrations();
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type has correct displayName "$displayName"', ({ type, displayName }) => {
    const meta = workspaceRegistry.getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe(displayName);
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type has correct category "$category"', ({ type, category }) => {
    const meta = workspaceRegistry.getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    expect(meta!.category).toBe(category);
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type metadata includes displayName (non-empty string)', ({ type }) => {
    const meta = workspaceRegistry.getWorkspaceWidgetMetadata(type);
    expect(meta).toBeDefined();
    expect(typeof meta!.displayName).toBe('string');
    expect(meta!.displayName.length).toBeGreaterThan(0);
  });
});

describe('Workspace widget serialize/restore — factory resolution', () => {
  beforeAll(() => {
    loadWorkspaceRegistrations();
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type resolves to a non-null, non-undefined component', async ({ type }) => {
    const resolved = await workspaceRegistry.resolveWorkspaceWidget(type);
    expect(resolved).not.toBeNull();
    expect(resolved).not.toBeUndefined();
    // Must be the REAL registered factory's component, not the
    // GenericTextWidget fallback — a mock-path mismatch would otherwise pass
    // this vacuously via WorkspaceWidgetRegistry's catch-and-fallback path.
    expect(resolved).not.toBe(MockGenericText);
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)('$type resolves to a valid React component type', async ({ type }) => {
    const resolved = await workspaceRegistry.resolveWorkspaceWidget(type);
    // React components are either functions or classes
    expect(typeof resolved).toBe('function');
  });

  it('unknown workspace type falls back to GenericTextWidget', async () => {
    const resolved = await workspaceRegistry.resolveWorkspaceWidget('__nonexistent_widget__');
    expect(resolved).toBe(MockGenericText);
  });
});

// ===========================================================================
// Context Widget Registration Tests
// ===========================================================================

describe('Context widget serialize/restore — registration', () => {
  beforeAll(() => {
    loadContextRegistrations();
  });

  // NOTE (test-repair task 021, 2026-07-08): register-context-widgets.ts now
  // registers more than the 12 types tracked here (e.g. 'get-started-cards',
  // 'file-preview' were added by later work). Same pattern as the workspace
  // registry above — assert subset containment instead of an exact total so
  // this test tracks its own 12 without going stale on unrelated additions.
  it('registers all 12 tracked context widget types (subset of the full registry)', () => {
    const types = contextRegistry.getAllContextWidgetTypes();
    for (const t of EXPECTED_CONTEXT_WIDGETS) {
      expect(types).toContain(t);
    }
  });

  it.each(EXPECTED_CONTEXT_WIDGETS)('%s is registered in ContextWidgetRegistry', type => {
    expect(contextRegistry.hasContextWidget(type)).toBe(true);
  });
});

describe('Context widget serialize/restore — factory resolution', () => {
  beforeAll(() => {
    loadContextRegistrations();
  });

  it.each(EXPECTED_CONTEXT_WIDGETS)('%s resolves to a non-null component', async type => {
    const component = await contextRegistry.resolveContextWidget(type);
    expect(component).not.toBeNull();
  });

  it.each(EXPECTED_CONTEXT_WIDGETS)('%s resolves to a valid React component type', async type => {
    const component = await contextRegistry.resolveContextWidget(type);
    expect(component).not.toBeUndefined();
    expect(typeof component).toBe('function');
  });

  it('unknown context type returns null (not a fallback)', async () => {
    const result = await contextRegistry.resolveContextWidget('__nonexistent_context_widget__');
    expect(result).toBeNull();
  });
});

// ===========================================================================
// Cross-Registry Consistency Tests
// ===========================================================================

describe('Widget registries — cross-registry consistency', () => {
  beforeAll(() => {
    loadWorkspaceRegistrations();
    loadContextRegistrations();
  });

  it('workspace and context registries have no overlapping type strings', () => {
    const workspaceTypes = new Set(workspaceRegistry.getAllWorkspaceWidgetTypes());
    const contextTypes = contextRegistry.getAllContextWidgetTypes();

    for (const ctxType of contextTypes) {
      expect(workspaceTypes.has(ctxType)).toBe(false);
    }
  });

  // NOTE (test-repair task 021, 2026-07-08): this test previously hardcoded
  // "11 workspace + 12 context = 23" as the combined total. The workspace
  // registry alone now holds more than 11 types (see the "registers all 11
  // tracked workspace widget types" test above), so a hardcoded combined
  // total is stale and will keep drifting as either registry grows for
  // unrelated reasons — a fragile assertion that doesn't guard a specific
  // behavioral contract (ADR-038 coverage-filler territory). Replaced with a
  // structural check: every EXPECTED widget list item (workspace + context)
  // is present, AND each registry's real size is at least as large as its
  // tracked EXPECTED list — preserving growth-tolerance while still failing
  // if either registry unexpectedly SHRINKS (a real regression signal).
  it('both registries contain at least their tracked widget sets (no unexpected shrinkage)', () => {
    const workspaceTypes = workspaceRegistry.getAllWorkspaceWidgetTypes();
    const contextTypes = contextRegistry.getAllContextWidgetTypes();

    expect(workspaceTypes.length).toBeGreaterThanOrEqual(EXPECTED_WORKSPACE_WIDGETS.length);
    expect(contextTypes.length).toBeGreaterThanOrEqual(EXPECTED_CONTEXT_WIDGETS.length);

    // Code-review follow-up (task 021): a pure lower-bound check can't catch
    // a duplicate-registration bug that registers a widget twice under two
    // DIFFERENT string keys (each registry's Map dedupes by key, so a keying
    // bug would silently inflate the count without tripping the "no overlap"
    // check above). Add a generous upper-bound sanity ceiling — wide enough
    // to tolerate many legitimate future widget additions without needing a
    // bump, but tight enough to catch a gross duplication bug (e.g.
    // accidentally registering the whole widget set twice).
    expect(workspaceTypes.length).toBeLessThan(EXPECTED_WORKSPACE_WIDGETS.length * 4);
    expect(contextTypes.length).toBeLessThan(EXPECTED_CONTEXT_WIDGETS.length * 4);
  });
});
