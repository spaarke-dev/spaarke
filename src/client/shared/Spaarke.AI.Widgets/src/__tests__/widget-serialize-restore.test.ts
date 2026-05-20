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
import {
  clearWorkspaceRegistry,
  getWorkspaceWidgetMetadata,
  hasWorkspaceWidget,
  resolveWorkspaceWidget,
  getAllWorkspaceWidgetTypes,
} from '../registry/WorkspaceWidgetRegistry';
import {
  clearContextRegistry,
  hasContextWidget,
  resolveContextWidget,
  getAllContextWidgetTypes,
} from '../registry/ContextWidgetRegistry';

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

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearWorkspaceRegistry();
  clearContextRegistry();
  jest.resetModules();
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function loadWorkspaceRegistrations(): void {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-workspace-widgets');
}

function loadContextRegistrations(): void {
  // Load both registration files to cover all 10 context widget types.
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/context/register-context-widgets').registerContextWidgets();
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
] as const;

// ===========================================================================
// Workspace Widget Registration Tests
// ===========================================================================

describe('Workspace widget serialize/restore — registration', () => {
  beforeEach(() => {
    loadWorkspaceRegistrations();
  });

  it('registers all 11 workspace widget types', () => {
    const types = getAllWorkspaceWidgetTypes();
    expect(types).toHaveLength(EXPECTED_WORKSPACE_WIDGETS.length);
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type is registered in WorkspaceWidgetRegistry',
    ({ type }) => {
      expect(hasWorkspaceWidget(type)).toBe(true);
    }
  );
});

describe('Workspace widget serialize/restore — metadata', () => {
  beforeEach(() => {
    loadWorkspaceRegistrations();
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type has correct displayName "$displayName"',
    ({ type, displayName }) => {
      const meta = getWorkspaceWidgetMetadata(type);
      expect(meta).toBeDefined();
      expect(meta!.displayName).toBe(displayName);
    }
  );

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type has correct category "$category"',
    ({ type, category }) => {
      const meta = getWorkspaceWidgetMetadata(type);
      expect(meta).toBeDefined();
      expect(meta!.category).toBe(category);
    }
  );

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type metadata includes displayName (non-empty string)',
    ({ type }) => {
      const meta = getWorkspaceWidgetMetadata(type);
      expect(meta).toBeDefined();
      expect(typeof meta!.displayName).toBe('string');
      expect(meta!.displayName.length).toBeGreaterThan(0);
    }
  );
});

describe('Workspace widget serialize/restore — factory resolution', () => {
  beforeEach(() => {
    loadWorkspaceRegistrations();
  });

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type resolves to a non-null, non-undefined component',
    async ({ type }) => {
      const resolved = await resolveWorkspaceWidget(type);
      expect(resolved).not.toBeNull();
      expect(resolved).not.toBeUndefined();
    }
  );

  it.each(EXPECTED_WORKSPACE_WIDGETS)(
    '$type resolves to a valid React component type',
    async ({ type }) => {
      const resolved = await resolveWorkspaceWidget(type);
      // React components are either functions or classes
      expect(typeof resolved).toBe('function');
    }
  );

  it('unknown workspace type falls back to GenericTextWidget', async () => {
    const resolved = await resolveWorkspaceWidget('__nonexistent_widget__');
    expect(resolved).toBe(MockGenericText);
  });
});

// ===========================================================================
// Context Widget Registration Tests
// ===========================================================================

describe('Context widget serialize/restore — registration', () => {
  beforeEach(() => {
    loadContextRegistrations();
  });

  it('registers all 10 context widget types', () => {
    const types = getAllContextWidgetTypes();
    expect(types).toHaveLength(EXPECTED_CONTEXT_WIDGETS.length);
  });

  it.each(EXPECTED_CONTEXT_WIDGETS)(
    '%s is registered in ContextWidgetRegistry',
    (type) => {
      expect(hasContextWidget(type)).toBe(true);
    }
  );
});

describe('Context widget serialize/restore — factory resolution', () => {
  beforeEach(() => {
    loadContextRegistrations();
  });

  it.each(EXPECTED_CONTEXT_WIDGETS)(
    '%s resolves to a non-null component',
    async (type) => {
      const component = await resolveContextWidget(type);
      expect(component).not.toBeNull();
    }
  );

  it.each(EXPECTED_CONTEXT_WIDGETS)(
    '%s resolves to a valid React component type',
    async (type) => {
      const component = await resolveContextWidget(type);
      expect(component).not.toBeUndefined();
      expect(typeof component).toBe('function');
    }
  );

  it('unknown context type returns null (not a fallback)', async () => {
    const result = await resolveContextWidget('__nonexistent_context_widget__');
    expect(result).toBeNull();
  });
});

// ===========================================================================
// Cross-Registry Consistency Tests
// ===========================================================================

describe('Widget registries — cross-registry consistency', () => {
  beforeEach(() => {
    loadWorkspaceRegistrations();
    loadContextRegistrations();
  });

  it('workspace and context registries have no overlapping type strings', () => {
    const workspaceTypes = new Set(getAllWorkspaceWidgetTypes());
    const contextTypes = getAllContextWidgetTypes();

    for (const ctxType of contextTypes) {
      expect(workspaceTypes.has(ctxType)).toBe(false);
    }
  });

  it('total registered widgets across both registries is 21', () => {
    const total =
      getAllWorkspaceWidgetTypes().length + getAllContextWidgetTypes().length;
    expect(total).toBe(21);
  });
});
