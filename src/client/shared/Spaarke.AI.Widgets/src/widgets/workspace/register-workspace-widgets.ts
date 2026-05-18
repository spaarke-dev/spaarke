/**
 * register-workspace-widgets.ts
 *
 * Registers all 7 R1 output widgets migrated to the R2 WorkspaceWidgetRegistry.
 *
 * Each registration:
 *   - Uses the OutputWidgetType string value as the registry key so that
 *     workspace_widget SSE events (which carry the same string) resolve to
 *     the correct component without a mapping layer.
 *   - Provides WidgetMetadata (displayName, category, allowMultiple, defaultOrder).
 *   - Lazily imports the R1 component from @spaarke/ai-outputs via
 *     createWorkspaceWrapper, which adds serialize/restore without touching
 *     the original widget files.
 *
 * DATA-REFRESHED RESTORE (D-08):
 *   WorkspaceWidgetWrapper.serializeState() stores only the query params
 *   (sessionId, turnId, plus any widget-specific identifiers). On restore
 *   the shell re-fetches fresh data using those params — stale snapshots
 *   are never rehydrated.
 *
 * SIDE-EFFECT IMPORT:
 *   This file is imported once (as a side effect) from
 *   src/client/shared/Spaarke.AI.Widgets/src/index.ts. The import registers
 *   all 7 types before any component tree mounts.
 *
 * React 19, NOT PCF-safe.
 *
 * @see WorkspaceWidgetWrapper.tsx — HOC that adapts R1 widgets to R2 interface
 * @see WorkspaceWidgetRegistry.ts — registry this file populates
 * @see ADR-012 — shared component library
 * @see ADR-013 — AI Architecture: extend BFF
 * @see D-08    — data-refreshed restore
 */

import { registerWorkspaceWidget } from '../../registry/WorkspaceWidgetRegistry';
import { createWorkspaceWrapper } from './WorkspaceWidgetWrapper';
import type { WorkspaceWidgetComponent } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Widget type string constants
// These MUST match the OutputWidgetType enum values from @spaarke/ai-outputs
// so that workspace_widget SSE events resolve correctly. We duplicate them
// here as string literals to avoid a hard dependency on the enum at runtime.
// ---------------------------------------------------------------------------

const WIDGET_TYPE = {
  BudgetDashboard: 'BudgetDashboard',
  SearchResults: 'SearchResults',
  AnalysisEditor: 'AnalysisEditor',
  ContractComparison: 'ContractComparison',
  StatusSummary: 'StatusSummary',
  Recommendation: 'Recommendation',
  ActionPlan: 'ActionPlan',
} as const;

// ---------------------------------------------------------------------------
// Registration helper
// ---------------------------------------------------------------------------

/**
 * Wrap a factory that returns an R1 OutputWidget module so that the result
 * satisfies WorkspaceWidgetComponent. createWorkspaceWrapper produces an HOC
 * that translates WorkspaceWidgetProps → OutputWidgetProps and adds
 * serialize/restore.
 */
function wrapFactory<T>(
  loaderFn: () => Promise<{ default: React.ComponentType<{ data: T; isLoading?: boolean; error?: string; className?: string }> }>,
  widgetType: string
): () => Promise<{ default: WorkspaceWidgetComponent }> {
  return () =>
    Promise.resolve({
      default: createWorkspaceWrapper<T>(loaderFn, widgetType) as WorkspaceWidgetComponent,
    });
}

// ---------------------------------------------------------------------------
// 1. BudgetDashboard
//    Category: financial — displays budget line items as progress bars.
//    allowMultiple=false — a session has one budget view at a time.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.BudgetDashboard,
  {
    displayName: 'Budget Dashboard',
    category: 'financial',
    icon: 'MoneyRegular',
    allowMultiple: false,
    defaultOrder: 10,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-budget-dashboard" */
        '@spaarke/ai-outputs/src/output-widgets/BudgetDashboardWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.BudgetDashboard
  )
);

// ---------------------------------------------------------------------------
// 2. SearchResults
//    Category: search — displays ranked AI search result cards.
//    allowMultiple=true — different queries can produce parallel result tabs.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.SearchResults,
  {
    displayName: 'Search Results',
    category: 'search',
    icon: 'SearchRegular',
    allowMultiple: true,
    defaultOrder: 20,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-search-results" */
        '@spaarke/ai-outputs/src/output-widgets/SearchResultsWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.SearchResults
  )
);

// ---------------------------------------------------------------------------
// 3. AnalysisEditor
//    Category: analysis — AI-generated analysis as titled sections with
//    optional edit mode.
//    allowMultiple=true — different documents/turns can each have an analysis tab.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.AnalysisEditor,
  {
    displayName: 'Analysis Editor',
    category: 'analysis',
    icon: 'DocumentEditRegular',
    allowMultiple: true,
    defaultOrder: 30,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-analysis-editor" */
        '@spaarke/ai-outputs/src/output-widgets/AnalysisEditorWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.AnalysisEditor
  )
);

// ---------------------------------------------------------------------------
// 4. ContractComparison
//    Category: document — side-by-side contract clause comparison.
//    allowMultiple=true — users may compare multiple document pairs.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.ContractComparison,
  {
    displayName: 'Contract Comparison',
    category: 'document',
    icon: 'DocumentCompareRegular',
    allowMultiple: true,
    defaultOrder: 40,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-contract-comparison" */
        '@spaarke/ai-outputs/src/output-widgets/ContractComparisonWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.ContractComparison
  )
);

// ---------------------------------------------------------------------------
// 5. StatusSummary
//    Category: status — health/status dashboard with icon-coded category rows.
//    allowMultiple=false — a session has one status overview at a time.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.StatusSummary,
  {
    displayName: 'Status Summary',
    category: 'status',
    icon: 'CheckmarkCircleRegular',
    allowMultiple: false,
    defaultOrder: 50,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-status-summary" */
        '@spaarke/ai-outputs/src/output-widgets/StatusSummaryWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.StatusSummary
  )
);

// ---------------------------------------------------------------------------
// 6. Recommendation
//    Category: recommendation — ranked AI recommendations with priority badges.
//    allowMultiple=false — single recommendation set per session.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.Recommendation,
  {
    displayName: 'Recommendations',
    category: 'recommendation',
    icon: 'LightbulbRegular',
    allowMultiple: false,
    defaultOrder: 60,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-recommendation" */
        '@spaarke/ai-outputs/src/output-widgets/RecommendationWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.Recommendation
  )
);

// ---------------------------------------------------------------------------
// 7. ActionPlan
//    Category: planning — multi-step action plan as an interactive checklist.
//    allowMultiple=false — a session has one active action plan at a time.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  WIDGET_TYPE.ActionPlan,
  {
    displayName: 'Action Plan',
    category: 'planning',
    icon: 'TaskListSquareLtrRegular',
    allowMultiple: false,
    defaultOrder: 70,
  },
  wrapFactory(
    () =>
      import(
        /* webpackChunkName: "widget-action-plan" */
        '@spaarke/ai-outputs/src/output-widgets/ActionPlanWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.ActionPlan
  )
);

// ---------------------------------------------------------------------------
// 8. redline-viewer — Document Comparison (task AIPU2-085)
//    Category: document — side-by-side section diff from CompareDocumentsTool.
//    allowMultiple=true — each comparison pair can occupy a separate tab.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the widgetType value sent by the server-side
   * CompareDocumentsTool (task AIPU2-042). The AI router emits
   * `{ widgetType: "redline-viewer", data: DocumentDiff }` after a comparison.
   */
  'redline-viewer',
  {
    displayName: 'Document Comparison',
    category: 'document',
    icon: 'DocumentCompare24Regular',
    /**
     * allowMultiple=true: users may compare multiple document pairs within the
     * same session, each appearing as a separate workspace tab.
     */
    allowMultiple: true,
    /**
     * defaultOrder=25: positions the comparison view after BudgetDashboard (10)
     * and SearchResults (20) but before AnalysisEditor (30).
     */
    defaultOrder: 25,
  },
  () =>
    import('./RedlineViewerWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// Public registration function (called from index.ts side-effect import)
// ---------------------------------------------------------------------------

/**
 * registerWorkspaceWidgets
 *
 * No-op sentinel function — all registrations above execute as top-level
 * side effects when this module is imported. The function exists so that
 * callers can use a named import that makes the side-effect intent explicit:
 *
 *   import { registerWorkspaceWidgets } from './widgets/workspace/register-workspace-widgets';
 *   registerWorkspaceWidgets(); // reads as: "ensure widgets are registered"
 *
 * The function body is intentionally empty — the registrations already ran.
 */
export function registerWorkspaceWidgets(): void {
  // All registrations execute at module evaluation time (top-level side effects above).
  // This function is a no-op that exists for explicitness at the call site.
}
