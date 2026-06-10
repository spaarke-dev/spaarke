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
import { safeRegister } from '@spaarke/ui-components';
import type { WorkspaceWidgetComponent } from '../../types/widget-types';

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase B.5 (2026-06-09):
// Isolate each registration in its own try/catch. Without this, a synchronous
// throw from ANY call below (malformed metadata, factory-expression evaluation
// failure, missing import) would skip all subsequent registrations, leaving
// the registry partially populated and the workspace pane rendering empty
// widget tabs. See safeRegister docblock + brittleness-remediation-plan.md.
function safeRegisterWidget(...args: Parameters<typeof registerWorkspaceWidget>): void {
  safeRegister('WorkspaceWidget', args[0], () => registerWorkspaceWidget(...args));
}

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
  loaderFn: () => Promise<{
    default: React.ComponentType<{ data: T; isLoading?: boolean; error?: string; className?: string }>;
  }>,
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

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/BudgetDashboardWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.BudgetDashboard
  )
);

// ---------------------------------------------------------------------------
// 2. SearchResults
//    Category: search — displays ranked AI search result cards.
//    allowMultiple=true — different queries can produce parallel result tabs.
// ---------------------------------------------------------------------------

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/SearchResultsWidget'
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

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/AnalysisEditorWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.AnalysisEditor
  )
);

// ---------------------------------------------------------------------------
// 4. ContractComparison
//    Category: document — side-by-side contract clause comparison.
//    allowMultiple=true — users may compare multiple document pairs.
// ---------------------------------------------------------------------------

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/ContractComparisonWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.ContractComparison
  )
);

// ---------------------------------------------------------------------------
// 5. StatusSummary
//    Category: status — health/status dashboard with icon-coded category rows.
//    allowMultiple=false — a session has one status overview at a time.
// ---------------------------------------------------------------------------

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/StatusSummaryWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.StatusSummary
  )
);

// ---------------------------------------------------------------------------
// 6. Recommendation
//    Category: recommendation — ranked AI recommendations with priority badges.
//    allowMultiple=false — single recommendation set per session.
// ---------------------------------------------------------------------------

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/RecommendationWidget'
      ) as Promise<{ default: React.ComponentType<any> }>,
    WIDGET_TYPE.Recommendation
  )
);

// ---------------------------------------------------------------------------
// 7. ActionPlan
//    Category: planning — multi-step action plan as an interactive checklist.
//    allowMultiple=false — a session has one active action plan at a time.
// ---------------------------------------------------------------------------

safeRegisterWidget(
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
        '@spaarke/ai-outputs/output-widgets/ActionPlanWidget'
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
// 9. create-matter-wizard — Embedded CreateMatterWizard (task AIPU2-104)
//    Category: wizard — multi-step Create Matter flow embedded as a workspace tab.
//    allowMultiple=false — only one Create Matter wizard at a time per session.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string used by the AI router when it triggers the Create Matter
   * flow programmatically (e.g. from a playbook action or chat intent).
   * Must match the widgetType value in any server-side workspace_widget SSE
   * events that request this wizard.
   */
  'create-matter-wizard',
  {
    displayName: 'Create Matter Wizard',
    category: 'wizard',
    icon: 'DocumentAdd24Regular',
    /**
     * allowMultiple=false: a session should not have two simultaneous
     * "Create Matter" wizards — opening a second replaces the first tab.
     */
    allowMultiple: false,
    /**
     * defaultOrder=80: wizards appear after all output widgets (10–70) and
     * the redline viewer (25) so they don't crowd the primary output area.
     */
    defaultOrder: 80,
  },
  () =>
    import('./CreateMatterWizardWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 10. document-upload-wizard — Embedded DocumentUpload flow (task AIPU2-104)
//     Category: wizard — three-step file upload flow embedded as a workspace tab.
//     allowMultiple=true — users may upload multiple batches of documents in
//     parallel tabs within a single session.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  'document-upload-wizard',
  {
    displayName: 'Upload Documents',
    category: 'wizard',
    icon: 'CloudArrowUp24Regular',
    /**
     * allowMultiple=true: different document upload sessions may coexist;
     * e.g. uploading contract exhibits while a matter upload is in progress.
     */
    allowMultiple: true,
    /**
     * defaultOrder=85: positioned just after the Create Matter wizard.
     */
    defaultOrder: 85,
  },
  () =>
    import('./DocumentUploadWizardWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 11. search-select-wizard — Embedded Search & Select flow (task AIPU2-104)
//     Category: wizard — two-step record picker embedded as a workspace tab.
//     allowMultiple=true — multiple entity-type pickers may coexist (e.g.
//     searching for a matter and an account simultaneously).
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  'search-select-wizard',
  {
    displayName: 'Search & Select',
    category: 'wizard',
    icon: 'Search24Regular',
    /**
     * allowMultiple=true: callers may open separate search-select wizards
     * for different entity types in the same session.
     */
    allowMultiple: true,
    /**
     * defaultOrder=90: positioned after the upload wizard.
     */
    defaultOrder: 90,
  },
  () =>
    import('./SearchSelectWizardWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 12. email-compose — Analysis Builder intent dispatcher (task 044, FR-19)
//     Category: ai — opens the Analysis Builder (Playbook Library Code Page)
//     pre-configured for the compose-email flow.
//     Widget type string MUST match task 042's dispatched `widget_load` event
//     payload exactly — do NOT rename.
//     allowMultiple=true — users may compose multiple emails concurrently.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the FR-19 mapping (task 042's onCardClick dispatch
   * for "Send Email Message" card). Renaming this string would break the
   * GetStartedCards → Analysis Builder routing path.
   */
  'email-compose',
  {
    displayName: 'Send Email',
    category: 'ai',
    icon: 'Mail24Regular',
    /**
     * allowMultiple=true: users may have several email-compose dispatcher tabs
     * if they re-launched the card multiple times.
     */
    allowMultiple: true,
    /**
     * defaultOrder=100: positioned after the existing wizards (80–90).
     */
    defaultOrder: 100,
  },
  () =>
    import('./EmailComposeWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 13. meeting-schedule — Analysis Builder intent dispatcher (task 044, FR-19)
//     Category: ai — opens the Analysis Builder (Playbook Library Code Page)
//     pre-configured for the schedule-meeting flow.
//     Widget type string MUST match task 042's dispatched `widget_load` event
//     payload exactly — do NOT rename.
//     allowMultiple=true — users may schedule multiple meetings concurrently.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the FR-19 mapping (task 042's onCardClick dispatch
   * for "Schedule New Meeting" card). Renaming this string would break the
   * GetStartedCards → Analysis Builder routing path.
   */
  'meeting-schedule',
  {
    displayName: 'Schedule Meeting',
    category: 'ai',
    icon: 'CalendarAdd24Regular',
    /**
     * allowMultiple=true: users may have several meeting-schedule dispatcher
     * tabs if they re-launched the card multiple times.
     */
    allowMultiple: true,
    /**
     * defaultOrder=110: positioned just after email-compose.
     */
    defaultOrder: 110,
  },
  () =>
    import('./MeetingScheduleWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 14. create-project-wizard — Existing Create Project Code Page dispatcher (task 043, FR-19)
//     Category: wizard — opens the existing `sprk_createprojectwizard` Code Page
//     via Xrm.Navigation.navigateTo (REUSE per OC-04 / ADR-012, NOT re-authored).
//     Widget type string MUST match task 042's dispatched `widget_load` event
//     payload exactly — do NOT rename.
//     allowMultiple=true — users may launch multiple Create Project dialogs
//     in distinct workspace tabs.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the FR-19 mapping (task 042's onCardClick dispatch
   * for "Create New Project" card). Renaming this string would break the
   * GetStartedCards → Create Project routing path.
   */
  'create-project-wizard',
  {
    displayName: 'Create New Project',
    category: 'wizard',
    icon: 'FolderAdd24Regular',
    /**
     * allowMultiple=true: users may have several Create Project dispatcher tabs
     * if they re-launched the card multiple times. The underlying Code Page
     * itself is a singleton modal — only one dialog is visible at a time —
     * but the widget tab persists for relaunch.
     */
    allowMultiple: true,
    /**
     * defaultOrder=120: positioned just after meeting-schedule (110).
     */
    defaultOrder: 120,
  },
  () =>
    import('./CreateProjectWizardWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 15. find-similar-wizard — Existing Find Similar Code Page dispatcher (task 043, FR-19)
//     Category: wizard — opens the existing `sprk_findsimilar` Code Page
//     via Xrm.Navigation.navigateTo (REUSE per OC-04 / ADR-012, NOT re-authored).
//     Widget type string MUST match task 042's dispatched `widget_load` event
//     payload exactly — do NOT rename.
//     allowMultiple=true — users may launch multiple Find Similar searches
//     in distinct workspace tabs.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the FR-19 mapping (task 042's onCardClick dispatch
   * for "Find Similar" card). Renaming this string would break the
   * GetStartedCards → Find Similar routing path.
   */
  'find-similar-wizard',
  {
    displayName: 'Find Similar Documents',
    category: 'wizard',
    icon: 'DocumentSearch24Regular',
    /**
     * allowMultiple=true: users may have several Find Similar dispatcher tabs
     * if they re-launched the card multiple times (e.g. comparing different
     * source documents in parallel).
     */
    allowMultiple: true,
    /**
     * defaultOrder=130: positioned just after create-project-wizard (120).
     */
    defaultOrder: 130,
  },
  () =>
    import('./FindSimilarWizardWidget') as Promise<{
      default: import('../../types/widget-types').WorkspaceWidgetComponent;
    }>
);

// ---------------------------------------------------------------------------
// 16. workspace — Embedded LegalWorkspaceApp (Round 4 Fix 4, 2026-05-21)
//     Category: workspace — opens the chosen workspace layout as a single
//     workspace tab via the embedded LegalWorkspaceApp surface
//     (`embedded={true}`). Triggered by WorkspacePaneMenu's "Switch Workspace"
//     handler in SpaarkeAi. The widget data carries `{ layoutId, layoutName }`
//     — `layoutId` is passed as `initialWorkspaceId` so the embedded
//     useWorkspaceLayouts hook activates the chosen layout on mount.
//
//     allowMultiple=true — a session may have multiple workspace tabs open
//     (e.g. Corporate Workspace + Litigation Workspace side-by-side via the
//     tab manager). The FIFO cap on MAX_WORKSPACE_TABS still applies.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Widget type string MUST match the value dispatched by
   * `WorkspacePaneMenu.tsx` when the user selects a layout from "Switch
   * Workspace". The string is intentionally plain "workspace" — there is one
   * workspace widget type and it always renders LegalWorkspaceApp.
   */
  'workspace',
  {
    displayName: 'Workspace',
    category: 'workspace',
    icon: 'AppsListRegular',
    allowMultiple: true,
    /**
     * defaultOrder=140: positioned after the existing intent dispatchers
     * (email-compose 100, meeting-schedule 110, create-project 120,
     * find-similar 130).
     */
    defaultOrder: 140,
  },
  () =>
    import('./WorkspaceLayoutWidget').then(m => ({
      default: m.WorkspaceLayoutWidget as import('../../types/widget-types').WorkspaceWidgetComponent,
    }))
);

// ---------------------------------------------------------------------------
// ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08) — Dataverse entity-view widgets
//
// Four system widgets backed by the shared <DataverseEntityViewWidget>: a thin
// wrapper around the Spaarke DataGrid framework. Each registration baked a
// specific `configId` (the operator-created `sprk_gridconfiguration` row) into
// the resolved component via the factory wrapper below.
//
// **DEPLOYMENT REQUIREMENT** — before these widgets render correctly, the
// operator MUST create one `sprk_gridconfiguration` row per entity and replace
// the placeholder constants below with the real GUIDs. See
// `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md`
// for the seed instructions. The widget falls back to a clear empty state when
// `data.configId` resolves to an unknown record (DataGrid's invalid-config
// guard); no production crash.
// ---------------------------------------------------------------------------

// Real sprk_gridconfiguration GUIDs in spaarkedev1:
//  - documents:        'Active Documents (Workspace)'    (created 2026-06-09; replaces
//                       the legacy 'Semantic Search Documents View' row d99a4352-…,
//                       which was authored for SemanticSearchControl PCF and lacks
//                       the DataGrid framework's source.savedQueryId field).
//  - matters:          'Active Matters (Workspace)'      (created 2026-06-08)
//  - projects:         'Active Projects (Workspace)'     (created 2026-06-08)
//  - invoices:         'Invoice Matter Budget Performance' (pre-existing)
//  - workAssignments:  'Active Work Assignments (Workspace)' (created 2026-06-08)
const ENTITY_VIEW_CONFIG_IDS = {
  documents: '1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6',
  matters: '113ad380-9e63-f111-ab0c-70a8a53ec687',
  projects: '97ee98e7-7a63-f111-ab0c-70a8a53ec687',
  invoices: 'd021827b-9b5e-f111-ab0c-7c1e521545d7',
  workAssignments: '9c5b0ee7-7a63-f111-ab0c-000d3a4d8152',
} as const;

/**
 * Build a lazy factory that resolves the shared `DataverseEntityViewWidget`
 * pre-configured with a specific `configId`. The wrapper accepts the standard
 * `WorkspaceWidgetProps<DataverseEntityViewWidgetData>` and merges the baked
 * `configId` into `data` (caller-supplied `configId` still wins, which keeps
 * the dispatcher path open for future overrides).
 */
function createEntityViewFactory(configId: string) {
  return () =>
    import('./DataverseEntityViewWidget').then(m => {
      const Base = m.DataverseEntityViewWidget;
      const Wrapped = (
        props: import('../../types/widget-types').WorkspaceWidgetProps<
          import('./DataverseEntityViewWidget').DataverseEntityViewWidgetData
        >
      ): ReturnType<typeof Base> => {
        // Caller-supplied configId wins; baked-in configId is the default.
        const mergedData = {
          ...(props.data ?? {}),
          configId: props.data?.configId ?? configId,
        };
        return Base({ ...props, data: mergedData });
      };
      Wrapped.displayName = `DataverseEntityViewWidget(${configId})`;
      return {
        default: Wrapped as unknown as import('../../types/widget-types').WorkspaceWidgetComponent,
      };
    });
}

registerWorkspaceWidget(
  'documents-list',
  {
    displayName: 'Documents',
    category: 'data',
    icon: 'DocumentRegular',
    allowMultiple: true,
    defaultOrder: 200,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.documents)
);

safeRegisterWidget(
  'matters-list',
  {
    displayName: 'Matters',
    category: 'data',
    icon: 'BriefcaseSearchRegular',
    allowMultiple: true,
    defaultOrder: 205,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.matters)
);

registerWorkspaceWidget(
  'projects-list',
  {
    displayName: 'Projects',
    category: 'data',
    icon: 'FolderRegular',
    allowMultiple: true,
    defaultOrder: 210,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.projects)
);

registerWorkspaceWidget(
  'invoices-list',
  {
    displayName: 'Invoices',
    category: 'data',
    icon: 'ReceiptRegular',
    allowMultiple: true,
    defaultOrder: 220,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.invoices)
);

registerWorkspaceWidget(
  'work-assignments-list',
  {
    displayName: 'Work Assignments',
    category: 'data',
    icon: 'BriefcaseRegular',
    allowMultiple: true,
    defaultOrder: 230,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.workAssignments)
);

// ---------------------------------------------------------------------------
// ai-spaarke-ai-workspace-UI-r1 #7 (2026-06-08) — Metrics dashboards
//
// Each dashboard ("Matters Report", "Invoice Report", "Project Report", …) is
// a STANDALONE direct widget — not a composable Dashboard section. Operators
// confirmed (2026-06-08) that these reports are not added to consolidated
// workspaces; each owns its full tab.
//
// Configs live in `metricsDashboardConfigs.ts` (in-code per the same
// 2026-06-08 decision; promote to a `sprk_dashboardconfiguration` Dataverse
// entity later if maker-authored dashboards become a requirement).
//
// To add a new dashboard:
//   1. Add a MetricsDashboardConfig entry in `metricsDashboardConfigs.ts`.
//   2. Add a registerWorkspaceWidget call below using
//      `createMetricsDashboardFactory(dashboardId)`.
// ---------------------------------------------------------------------------

function createMetricsDashboardFactory(dashboardId: string) {
  return () =>
    import('./MetricsDashboardWidget').then(m => {
      const Base = m.MetricsDashboardWidget;
      const Wrapped = (
        props: import('../../types/widget-types').WorkspaceWidgetProps<
          import('./MetricsDashboardWidget').MetricsDashboardWidgetData
        >
      ): ReturnType<typeof Base> => {
        const mergedData = {
          ...(props.data ?? {}),
          dashboardId: props.data?.dashboardId ?? dashboardId,
        };
        return Base({ ...props, data: mergedData });
      };
      Wrapped.displayName = `MetricsDashboardWidget(${dashboardId})`;
      return {
        default: Wrapped as unknown as import('../../types/widget-types').WorkspaceWidgetComponent,
      };
    });
}

registerWorkspaceWidget(
  'matters-dashboard',
  {
    displayName: 'Matters Report',
    category: 'ai',
    icon: 'DataBarVerticalRegular',
    allowMultiple: false,
    defaultOrder: 300,
  },
  createMetricsDashboardFactory('matters-dashboard')
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
