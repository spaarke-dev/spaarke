/**
 * TabContextMapping.ts — Static mapping from workspace widget types to
 * recommended context widget types for the three-pane shell (R2).
 *
 * When the user switches workspace tabs, the ContextPaneController calls
 * `getContextWidgetForTab()` to determine which context widget should become
 * active. Returning `null` means "keep the current context widget" — no
 * automatic switch for widget types that have no strong context pairing.
 *
 * Mapping rationale:
 *   SearchResults      → 'sources-citations'  Sources and citations back search hits.
 *   AnalysisEditor     → 'findings'            The findings widget shows AI findings/progress alongside analysis.
 *   BudgetDashboard    → 'entity-info'         Entity/matter context is most useful beside budget views.
 *   ContractComparison → 'sources-citations'   Source documents are the primary context for comparisons.
 *   redline-viewer     → 'sources-citations'   Same rationale as ContractComparison.
 *   StatusSummary      → 'entity-info'         Status is entity-scoped.
 *   Recommendation     → 'findings'            Findings support recommendation context.
 *   ActionPlan         → null                  Keep current — action plans are self-contained.
 *   (unknown)          → null                  Keep current — safe default.
 *
 * @see ContextPaneController — consumes this mapping on tab_change events
 * @see register-workspace-widgets.ts — canonical list of workspace widget type strings
 * @see ContextWidgetRegistry — context widget type strings that must be registered
 */

// ---------------------------------------------------------------------------
// Static mapping: workspace widget type → context widget type
//
// Values are context widget type strings as registered in ContextWidgetRegistry
// (e.g. 'sources-citations', 'findings', 'entity-info', 'progress-tracker').
// A null value means "no recommendation — keep the current context widget."
// ---------------------------------------------------------------------------

const TAB_CONTEXT_MAP: Record<string, string | null> = {
  // Document / comparison widget types
  ContractComparison: 'sources-citations',
  'redline-viewer': 'sources-citations',

  // Search widget type
  SearchResults: 'sources-citations',

  // Analysis widget type
  AnalysisEditor: 'findings',
  Recommendation: 'findings',

  // Financial / status widget types
  BudgetDashboard: 'entity-info',
  StatusSummary: 'entity-info',

  // Planning — self-contained, no automatic context switch
  ActionPlan: null,
};

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Returns the recommended context widget type for a given workspace widget
 * type, or `null` if no specific recommendation exists (the Context pane
 * should keep its current widget in that case).
 *
 * @param workspaceWidgetType - The `widgetType` string from a `tab_change` event.
 * @returns The context widget type string to activate, or `null` to keep current.
 *
 * @example
 * const contextType = getContextWidgetForTab('SearchResults');
 * // → 'sources-citations'
 *
 * const contextType = getContextWidgetForTab('ActionPlan');
 * // → null  (keep current context widget)
 *
 * const contextType = getContextWidgetForTab('UnknownWidget');
 * // → null  (unknown type → safe keep-current default)
 */
export function getContextWidgetForTab(workspaceWidgetType: string): string | null {
  if (Object.prototype.hasOwnProperty.call(TAB_CONTEXT_MAP, workspaceWidgetType)) {
    return TAB_CONTEXT_MAP[workspaceWidgetType];
  }
  // Unknown widget type — keep the current context widget rather than clearing it.
  return null;
}

/**
 * The full static mapping from workspace widget types to recommended context
 * widget types. Exposed for testing and for consumers that need to inspect or
 * extend the mapping at application startup.
 *
 * Do not mutate at runtime — create a derived map instead.
 */
export const TAB_CONTEXT_MAPPING: Readonly<Record<string, string | null>> = TAB_CONTEXT_MAP;
