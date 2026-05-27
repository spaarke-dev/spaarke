/**
 * register-search-criteria-result-widget.ts
 *
 * Registers `SearchCriteriaResultWidget` under the `'search-criteria-result'`
 * workspace widget type key. Imported as a side effect from the package
 * barrel so the widget is available before any shell mounts.
 *
 * Pattern reference: matches `register-document-viewer-widget.ts` (R4 task
 * 042 / W-4 sibling). Each demo-widget gets its own one-file registration so
 * the widget is reversibly removable.
 *
 * Created in R4 task 043 (W-5) — first end-to-end Context → Workspace
 * widget mount demo per FR-03 / OC-R4-08.
 *
 * ADR-012 (shared lib), ADR-022 (React 19).
 */

import { registerWorkspaceWidget } from '../../registry/WorkspaceWidgetRegistry';

/**
 * The widget type ID under which SearchCriteriaResultWidget is registered.
 * Exported so dispatchers (e.g. SemanticSearchCriteriaTool in SpaarkeAi) can
 * reference the string symbolically instead of repeating the literal.
 */
export const SEARCH_CRITERIA_RESULT_WIDGET_TYPE = 'search-criteria-result' as const;

registerWorkspaceWidget(
  SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
  {
    displayName: 'Search Criteria',
    category: 'analysis',
    icon: 'SearchRegular',
    /**
     * allowMultiple=true — a user may run several searches in one session;
     * each criteria snapshot opens as its own workspace tab. The tab manager's
     * FIFO cap (MAX_WORKSPACE_TABS) still applies — the oldest tab evicts when
     * the cap is hit.
     */
    allowMultiple: true,
    /**
     * defaultOrder=160 — positioned after the document-viewer demo widget
     * (150, R4 task 042) so it sorts last among current widgets.
     */
    defaultOrder: 160,
  },
  () =>
    import('./SearchCriteriaResultWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

/**
 * Sentinel export so callers can import this file as a NAMED side effect:
 *
 *   import { registerSearchCriteriaResultWidget } from './widgets/workspace/register-search-criteria-result-widget';
 *   registerSearchCriteriaResultWidget(); // "ensure widget is registered"
 *
 * The actual registration call above runs at module-evaluation time; this
 * function is a no-op that exists for explicitness at the call site.
 */
export function registerSearchCriteriaResultWidget(): void {
  // Top-level side effect already executed when this module was imported.
}
