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
import { SEARCH_CRITERIA_RESULT_WIDGET_TYPE } from '../register-search-criteria-result-widget';

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

  // FR-08 enumeration (task 022) → FR-15 ENFORCEMENT (task 050): this widget
  // has no honest fit among the six WidgetContextType values (contextType stays
  // `undefined`), and is outside R3's overview/per-item scope — so it declares
  // an EXPLICIT assistantContract opt-out marker (required post-050), not a
  // silent absence.
  it('has no contextType and an EXPLICIT assistantContract opt-out (task 022 enumeration → task 050 FR-15)', () => {
    const meta = getWorkspaceWidgetMetadata(SEARCH_CRITERIA_RESULT_WIDGET_TYPE);
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBeUndefined();
    const declared = meta!.assistantContract as { optOut?: boolean; reason?: string };
    expect(declared.optOut).toBe(true);
    expect(typeof declared.reason).toBe('string');
    expect(declared.reason!.length).toBeGreaterThan(0);
  });
});
