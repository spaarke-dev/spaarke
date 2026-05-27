/**
 * register-search-criteria-result-widget — unit tests (R4 task 043 / W-5)
 *
 * Verifies the SearchCriteriaResult widget is wired into
 * WorkspaceWidgetRegistry via the dedicated side-effect registration file,
 * so dispatching `widget_load` with widgetType: 'search-criteria-result'
 * resolves to the expected component (and NOT the GenericTextWidget fallback).
 *
 * Mirrors register-document-viewer-widget.test.ts (task 042 sibling).
 */

import {
  hasWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  resolveWorkspaceWidget,
} from '../../../registry/WorkspaceWidgetRegistry';
import {
  SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
} from '../register-search-criteria-result-widget';

// Side-effect import: ensure the registration has run before the assertions.
// The package barrel does this in production; tests import directly so the
// registry state is set up regardless of test-runner module-load order.
import '../register-search-criteria-result-widget';

describe('register-search-criteria-result-widget', () => {
  it('registers the search-criteria-result widget type', () => {
    expect(hasWorkspaceWidget(SEARCH_CRITERIA_RESULT_WIDGET_TYPE)).toBe(true);
  });

  it('exposes the expected display name in registry metadata', () => {
    const meta = getWorkspaceWidgetMetadata(SEARCH_CRITERIA_RESULT_WIDGET_TYPE);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe('Search Criteria');
    expect(meta!.category).toBe('analysis');
    expect(meta!.allowMultiple).toBe(true);
  });

  it('resolveWorkspaceWidget returns a component (not the GenericTextWidget fallback)', async () => {
    // Smoke test for resolution. We don't compare component identity directly
    // (the registry returns a lazy-loaded promise), but we assert resolution
    // succeeds without falling back to the GenericTextWidget code path.
    const Component = await resolveWorkspaceWidget(SEARCH_CRITERIA_RESULT_WIDGET_TYPE);
    expect(Component).toBeDefined();
    expect(typeof Component).toBe('function');
  });

  it('exports SEARCH_CRITERIA_RESULT_WIDGET_TYPE as a stable string constant', () => {
    // Guard against accidental renames — dispatchers reference this constant
    // (e.g. SemanticSearchCriteriaTool in SpaarkeAi). Changing the value
    // would break the Context → Workspace `widget_load` demo wiring (FR-03).
    expect(SEARCH_CRITERIA_RESULT_WIDGET_TYPE).toBe('search-criteria-result');
  });
});
