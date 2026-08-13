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
import type { RegistryGetAgentVisibleState } from '../../registry/WorkspaceWidgetRegistry';
import { createWorkspaceWrapper } from './WorkspaceWidgetWrapper';
import { safeRegister } from '@spaarke/ui-components';
import type { WorkspaceWidgetComponent } from '../../types/widget-types';
import type { EmailTabWidgetData } from '../../types/WorkspaceTab';
// Assistant-contract metadata SHAPE (FR-08 + FR-15 SHAPE, R3 task 022):
// context-type (existing `contextType` field, task 020) · overview tool(s) ·
// per-item cards + landing target · interaction pattern. See
// WidgetAssistantContract's JSDoc in types/shared.ts for the full rationale.
import type { WidgetAssistantContract, AssistantContractCard } from '../../types/shared';
import { OVERVIEW_QUERY_TOOL_NAME } from '../../types/shared';
// FR-15 (task 050): assistantContract is now a REQUIRED registration member.
// Widgets with no overview tool / per-item cards declare an EXPLICIT opt-out
// marker + reason (never silent absence). See the opt-out constants below.
import { assistantContractOptOut } from '../../types/shared';
// Pillar 9 visibility derivations (task 073, D-C-28). The Dashboard category
// is attached to the 'workspace' registration (WorkspaceLayoutWidget); the
// Table category is attached to all 5 DataverseEntityViewWidget-backed
// system widgets (documents/matters/projects/invoices/work-assignments). The
// Email category (R2 task 040/042a/042c) is attached to the 'email'
// registration below via the `emailWorkspaceTabVisibility` kind-guard wrapper.
import { dashboardWidgetVisibility, emailWidgetVisibility, tableWidgetVisibility } from './pillar9-visibility';

// ---------------------------------------------------------------------------
// Email category derivation — R2 Phase C (task 042a/042c, FR-C1/C2/C4)
//
// Path 1 "persisted Email carrier" (see `notes/c-architecture-gap.md`): the
// email tab's `widgetData` is expected to structurally match the persisted
// `EmailTabWidgetData` carrier (added to `WorkspaceTabWidgetData` alongside
// this task). This wrapper narrows `widgetData` to that shape (guarding on
// `kind === 'Email'`) before delegating to `emailWidgetVisibility` (task 040)
// for the actual field mapping + `EMAIL_SNIPPET_CAP_CHARS` truncation — reused
// verbatim, not duplicated. Returns `null` for any non-Email `widgetData`,
// including tabs not yet populated by the population task (042b).
//
// `emlDocumentId` (the on-demand `eml-render` fetch handle, FR-C4) is a fetch
// handle only and is intentionally never read here — `emailWidgetVisibility`
// does not surface it, keeping it out of the agent-visible
// `SerializedEmailState`.
// ---------------------------------------------------------------------------

function isEmailTabWidgetData(value: unknown): value is EmailTabWidgetData {
  return typeof value === 'object' && value !== null && (value as { kind?: unknown }).kind === 'Email';
}

const emailWorkspaceTabVisibility: RegistryGetAgentVisibleState = (widgetData: unknown) => {
  if (!isEmailTabWidgetData(widgetData)) return null;
  return emailWidgetVisibility(widgetData);
};

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
// Assistant-contract constants (FR-08 + FR-15 SHAPE, R3 task 022)
//
// Two shared, reused-by-reference contracts cover every in-scope widget in
// THIS file:
//   - OVERVIEW_ONLY_CONTRACT — every grid + the 'workspace' dashboard
//     dispatcher (which hosts the Daily Briefing/Calendar layouts, task 010:
//     both collapse to the same generic Dashboard identity server-side —
//     there is no separate per-layout widget TYPE to tag). FR-06/FR-07: ONE
//     parameterized overview tool, no per-item cards, always answers in chat.
//   - EMAIL_CONTRACT — the 'email' direct widget. FR-09/FR-10: per-item
//     cards Reply/Reply All/Forward/Summarize the thread; no overview tool
//     of its own (overview parity for messages lives on the sibling
//     'communications-list' grid — FR-07's explicit scope is "all grids +
//     Briefing + Calendar", which does not name Email separately).
//
// Declaring these ONCE and reusing by reference (rather than repeating an
// identical object literal at every call site) keeps every overview-only
// registration provably identical — §11 reuse-first.
// ---------------------------------------------------------------------------

// Object.freeze: these two contracts are shared BY REFERENCE across every
// overview-only registration below (and EMAIL_CONTRACT is a single object
// too) — freezing prevents an accidental `contract.foo = ...` or
// `contract.perItemCards.push(...)` in a future consumer (e.g. task 030's
// pre-filter or task 050's guard) from silently corrupting every OTHER
// widget that shares the same reference.
const OVERVIEW_ONLY_CONTRACT: WidgetAssistantContract = Object.freeze({
  overviewTools: Object.freeze([OVERVIEW_QUERY_TOOL_NAME]),
  perItemCards: Object.freeze([]),
  interactionPattern: 'respond',
});

// Explicit type annotation (rather than inline in EMAIL_CONTRACT below) so
// each card's `landing` narrows to the AssistantCardLanding literal union
// instead of widening to `string`.
const EMAIL_PER_ITEM_CARDS: readonly AssistantContractCard[] = [
  // FR-10: draft_reply auto-drafts a thread-preserving bodyOverride, then
  // opens the existing SendEmailDialog composer pre-filled.
  { label: 'Reply', tool: 'draft_reply', landing: 'composer' },
  // Same backing tool as Reply — FR-09 declares draft_reply(communicationId,
  // mode); Reply vs Reply All is a call-time `mode` argument, not a
  // separate catalog tool.
  { label: 'Reply All', tool: 'draft_reply', landing: 'composer' },
  { label: 'Forward', tool: 'draft_forward', landing: 'composer' },
  // FR-09: summarize_thread answers IN CHAT (plain narrative, identical to
  // the file/document-summarize output) — no composer opens.
  { label: 'Summarize the thread', tool: 'summarize_thread', landing: 'chat' },
];

const EMAIL_CONTRACT: WidgetAssistantContract = Object.freeze({
  overviewTools: Object.freeze([]),
  perItemCards: Object.freeze(EMAIL_PER_ITEM_CARDS),
  // hybrid: Reply/Reply All/Forward open the composer (direct); Summarize
  // answers in chat (respond) — the widget mixes both.
  interactionPattern: 'hybrid',
});

// ---------------------------------------------------------------------------
// Assistant-contract OPT-OUTS (FR-15 ENFORCEMENT, R3 task 050)
//
// Task 050 makes `assistantContract` a REQUIRED registration member. Widgets
// that legitimately have NO Assistant overview tool / per-item cards MUST
// declare an EXPLICIT opt-out marker + reason (never silent absence) so the
// decision is forced + auditable. Three shared, reused-by-reference markers
// cover every non-contract widget class in THIS file (§11 reuse-first):
//
//   - OUTPUT_WIDGET_OPT_OUT   — the 8 R1 analysis-OUTPUT widgets (Budget
//     Dashboard … redline-viewer). Each renders a COMPLETED analysis result
//     payload; none is an Assistant overview/per-item data surface. Outside
//     R3's FR-06/07 (overview) + FR-09/11 (per-item Email/Documents) scope.
//   - DISPATCHER_OPT_OUT      — the intent-dispatcher / embedded-launcher
//     widgets (create-*-wizard, document-upload-wizard, search-select-wizard,
//     email-compose, meeting-schedule, find-similar-wizard, analysis-hub,
//     create-analysis-wizard). Each OPENS a wizard or Code Page flow; none is
//     a data surface the Assistant queries. Outside R3 scope.
//   - METRICS_DASHBOARD_OPT_OUT — the standalone metrics/report dashboards
//     (matters-dashboard, …). No parameterized overview tool + no per-item
//     cards. Outside R3's FR-06/07 scope.
//
// Object.freeze mirrors OVERVIEW_ONLY_CONTRACT/EMAIL_CONTRACT above — these are
// shared by reference across many registrations; freezing guards against an
// accidental in-place mutation corrupting every widget that shares the marker.
// ---------------------------------------------------------------------------

const OUTPUT_WIDGET_OPT_OUT = assistantContractOptOut(
  'R1 analysis-output widget — renders a completed analysis result payload; not an Assistant ' +
    'overview or per-item data surface (outside R3 FR-06/07 + FR-09/11 scope).'
);

const DISPATCHER_OPT_OUT = assistantContractOptOut(
  'Intent dispatcher / embedded launcher — opens a wizard or Code Page flow; not an Assistant ' +
    'overview or per-item data surface (outside R3 FR-06/07 + FR-09/11 scope).'
);

const METRICS_DASHBOARD_OPT_OUT = assistantContractOptOut(
  'Standalone metrics/report dashboard — no parameterized overview tool and no per-item cards ' +
    '(outside R3 FR-06/07 scope).'
);

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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-B1/FR-C3 (task 020): side-by-side document clause comparison.
    contextType: 'document',
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): R1 output widget — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
//    Category: document — side-by-side section diff from the retired compare-documents chat tool.
//    allowMultiple=true — each comparison pair can occupy a separate tab.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Type string MUST match the widgetType value sent by the server-side
   * the retired compare-documents chat tool (task AIPU2-042). The AI router emits
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
    // FR-B1/FR-C3 (task 020): a document diff is a document-viewing surface.
    contextType: 'document',
    // FR-15 (task 050): R1 output widget (document-diff result) — see OUTPUT_WIDGET_OPT_OUT.
    assistantContract: OUTPUT_WIDGET_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher / embedded wizard — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher / embedded wizard — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher / embedded wizard — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher (opens Analysis Builder) — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher (opens Analysis Builder) — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher (opens Create Project Code Page) — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-15 (task 050): intent dispatcher (opens Find Similar Code Page) — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
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
    // FR-B1/FR-C3 (task 020): the workspace-layout dispatcher mounts the
    // embedded LegalWorkspaceApp dashboard surface.
    contextType: 'dashboard',
    // FR-08 (task 022): hosts the Daily Briefing + Calendar layouts (both
    // collapse to the generic Dashboard identity server-side, task 010 —
    // there is no separate per-layout widget type to tag). Overview-only.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  () =>
    import('./WorkspaceLayoutWidget').then(m => ({
      default: m.WorkspaceLayoutWidget as import('../../types/widget-types').WorkspaceWidgetComponent,
    })),
  // Pillar 9 visibility opt-in (task 073, D-C-28). Dashboard category:
  // exposes `dashboardName` + optional `lastViewedSection` ONLY. Never
  // chart data / section payloads (token economy + privacy per ADR-015).
  // See `pillar9-visibility.ts` for the derivation rationale.
  dashboardWidgetVisibility
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
//  - communications:   'Active Communications (Workspace)' (created 2026-07-01,
//                       ai-spaarke-ai-workspace-UI-r2 task 010)
const ENTITY_VIEW_CONFIG_IDS = {
  documents: '1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6',
  matters: '113ad380-9e63-f111-ab0c-70a8a53ec687',
  projects: '97ee98e7-7a63-f111-ab0c-70a8a53ec687',
  invoices: 'd021827b-9b5e-f111-ab0c-7c1e521545d7',
  workAssignments: '9c5b0ee7-7a63-f111-ab0c-000d3a4d8152',
  communications: 'e1826c4c-9575-f111-ab0e-7ced8ddc4a05',
  // spaarkeai-assistant-enhancements-r1 task 050 (2026-07-22): "My Tasks (Assistant)" — opened by the
  // `list-tasks` capability when the user asks "what are my tasks?". Sources the "My Tasks Open"
  // saved query (12a510e4; Deadline+Task+Reminder, eventstatus=Open, NO owner filter) and scopes it
  // to the caller via the DataGrid `behavior.membershipFilter` feature — "records I'm on" (owner +
  // every assigned-person role), broader than `ownerid eq-userid`. Membership is resolved by the
  // DataverseEntityViewWidget from the AI session's authenticatedFetch.
  myTasks: 'ac05e4f1-8d85-f111-8075-7c1e5268570d',
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

// Pillar 9 visibility opt-in (task 073, D-C-28). Table category: exposes
// structural state (rowCount + sort + filter + selection CARDINALITY) for
// all 5 system table widgets. selectedRows is converted from row IDs to a
// COUNT per SerializedTableState privacy contract — row IDs / cell content
// never reach the agent prompt. See `pillar9-visibility.ts`.

registerWorkspaceWidget(
  'documents-list',
  {
    displayName: 'Documents',
    category: 'data',
    icon: 'DocumentRegular',
    allowMultiple: true,
    defaultOrder: 200,
    // FR-B1/FR-C3 (task 020): Dataverse entity-view grid.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards (per-item document actions live on the sibling
    // 'document-viewer' tab, FR-11 — not on this list).
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.documents),
  tableWidgetVisibility
);

safeRegisterWidget(
  'matters-list',
  {
    displayName: 'Matters',
    category: 'data',
    icon: 'BriefcaseSearchRegular',
    allowMultiple: true,
    defaultOrder: 205,
    // FR-B1/FR-C3 (task 020): the canonical matter-grid entity-view widget.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.matters),
  tableWidgetVisibility
);

registerWorkspaceWidget(
  'projects-list',
  {
    displayName: 'Projects',
    category: 'data',
    icon: 'FolderRegular',
    allowMultiple: true,
    defaultOrder: 210,
    // FR-B1/FR-C3 (task 020): Dataverse entity-view grid.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.projects),
  tableWidgetVisibility
);

registerWorkspaceWidget(
  'invoices-list',
  {
    displayName: 'Invoices',
    category: 'data',
    icon: 'ReceiptRegular',
    allowMultiple: true,
    defaultOrder: 220,
    // FR-B1/FR-C3 (task 020): Dataverse entity-view grid.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.invoices),
  tableWidgetVisibility
);

registerWorkspaceWidget(
  'work-assignments-list',
  {
    displayName: 'Work Assignments',
    category: 'data',
    icon: 'BriefcaseRegular',
    allowMultiple: true,
    defaultOrder: 230,
    // FR-B1/FR-C3 (task 020): Dataverse entity-view grid.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.workAssignments),
  tableWidgetVisibility
);

// spaarkeai-assistant-enhancements-r1 task 050 (2026-07-22): "My Tasks" — the user's open task-type
// Event records they are a member of. Opened by the `list-tasks` capability's client surface-launch
// branch (ConversationPane.handleSurfaceLaunch). Reuses the shared DataverseEntityViewWidget with the
// "My Tasks (Assistant)" config, which sources the "My Tasks Open" saved query and applies the
// DataGrid `behavior.membershipFilter` overlay ("records I'm on", broader than ownerid eq-userid).
registerWorkspaceWidget(
  'my-tasks-list',
  {
    displayName: 'My Tasks',
    category: 'data',
    icon: 'TaskListSquareLtrRegular',
    allowMultiple: false,
    defaultOrder: 235,
    // FR-B1/FR-C3 (task 020): Dataverse entity-view grid (DataverseEntityViewWidget-backed).
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards.
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  createEntityViewFactory(ENTITY_VIEW_CONFIG_IDS.myTasks),
  tableWidgetVisibility
);

// ai-spaarke-ai-workspace-UI-r2 FR-10 (2026-07-01): Communications direct widget.
// Pattern D dual-use with the `communications` section in LegalWorkspace.
// Row-click opens Layout 1 (OOB modal via `Xrm.Navigation.navigateTo` at 85% × 85%)
// per the Phase-1 framework unification (FR-03/FR-20).
//
// messaging-communication-app-r2 task 030 (FR-12, 2026-07-19): UPGRADED IN
// PLACE from the bare `DataverseEntityViewWidget` wrapper to the rich Pattern D
// `CommunicationsWorkspaceWidget` (filter-chip toolbar + card strip + embedded
// DataGrid), authored in the NEW shared lib `@spaarke/communication-components`
// (§11: neither the entity-coupled `@spaarke/events-components` nor the
// thin-generic `@spaarke/ai-widgets` layer is the right home for rich
// communication-widget content). The type string `communications-list` is
// UNCHANGED (dispatch unbroken) — this is the SAME registration, upgraded,
// not a second widget (NFR-05). The reused `sprk_gridconfiguration` GUID
// (`ENTITY_VIEW_CONFIG_IDS.communications`) now lives as the widget's own
// default `configId` inside `CommunicationsWorkspaceWidget.tsx`.
registerWorkspaceWidget(
  'communications-list',
  {
    // Human-facing label (§B UAT 2026-07-27 item 1). The widget TYPE string
    // 'communications-list' is the dispatch identity and MUST stay unchanged.
    displayName: 'Messages',
    category: 'data',
    icon: 'MailRegular',
    allowMultiple: true,
    defaultOrder: 240,
    // FR-B1/FR-C3 (task 020): rich Pattern D widget embeds a DataGrid over
    // communication records — the entity-grid bucket is the closest honest
    // fit among the six values.
    contextType: 'matter-grid',
    // FR-06/FR-07 (task 022): overview-only grid — ONE parameterized tool,
    // no per-item cards (per-item email actions live on the sibling 'email'
    // direct widget, FR-09/FR-10 — not on this list).
    assistantContract: OVERVIEW_ONLY_CONTRACT,
  },
  () =>
    import('@spaarke/communication-components').then(m => ({
      default:
        m.CommunicationsWorkspaceWidget as unknown as import('../../types/widget-types').WorkspaceWidgetComponent,
    })),
  tableWidgetVisibility
);

// ---------------------------------------------------------------------------
// email-communication-solution-r5 task 041 (FR-01, 2026-07-28): Email direct
// widget. Pattern D dual-use with the `email` section in LegalWorkspace
// (`email.registration.ts`). BOTH mounts render the SAME shared
// `EmailWorkspace` composition root (task 040) from
// `@spaarke/communication-components`, unchanged — dual-mount-parity per that
// component's own docblock. `EmailWorkspaceWidget.tsx` (this package) is a
// thin host-adapter wrapper: Xrm-backed `dataverseClient`/`dataService`/
// `navigationService`/`webApi` + `useAiSession()` for `authenticatedFetch`/
// `bffBaseUrl` — mirrors `DataverseEntityViewWidget.tsx`'s Xrm-adapter
// pattern. Type string `email` is distinct from `communications-list`,
// `email-compose`, and the LegalWorkspace section id `communications`
// (ADR-039 Path C — no collision, no server-side surface identity).
safeRegisterWidget(
  'email',
  {
    displayName: 'Email',
    category: 'data',
    icon: 'MailRegular',
    allowMultiple: true,
    // defaultOrder=245: positioned immediately after Communications (240),
    // before the metrics dashboards (300+).
    defaultOrder: 245,
    // FR-B1/FR-C3 (task 020, BINDING): the email direct widget declares the
    // 'email' contextType — required so proactive-chip scoping (Workstream B)
    // can recognize an email tab as the active surface.
    contextType: 'email',
    // FR-09/FR-10 (task 022): per-item cards Reply/Reply All/Forward/
    // Summarize the thread — see EMAIL_CONTRACT above.
    assistantContract: EMAIL_CONTRACT,
  },
  () =>
    import('./EmailWorkspaceWidget').then(m => ({
      default: m.EmailWorkspaceWidget as unknown as import('../../types/widget-types').WorkspaceWidgetComponent,
    })),
  emailWorkspaceTabVisibility
);

// ---------------------------------------------------------------------------
// email-communication-intelligence-r2 task 062 (Pillar E): Communications
// Reconciliation direct widget. Dual-mount with the standalone
// `sprk_communicationreconciliation` Code Page (`src/solutions/CommunicationReconciliation`)
// — BOTH mounts render the SAME shared `ReconciliationWorkspace` from
// `@spaarke/communication-components` (task 061) unchanged; only the host-adapter
// resolution differs. `ReconciliationWorkspaceWidget.tsx` (this package) is a thin
// host-adapter wrapper: Xrm-backed `dataverseClient`/`webApi` + `useAiSession()`
// for `authenticatedFetch`, mirroring `EmailWorkspaceWidget.tsx`. Type string
// `communications-reconciliation` is distinct from `communications-list` /
// `email` / `email-compose` (ADR-039 Path C — no collision, no server-side
// surface identity). MOUNT only (§11): no forked grid/shell/tabs.
safeRegisterWidget(
  'communications-reconciliation',
  {
    displayName: 'Reconciliation',
    category: 'data',
    icon: 'TaskListSquareLtrRegular',
    allowMultiple: true,
    // defaultOrder=246: positioned immediately after Email (245), before the
    // metrics dashboards (300+).
    defaultOrder: 246,
    // FR-B1/FR-C3 (task 020): the reconciliation widget embeds a DataGrid over
    // communication records — the entity-grid bucket is the closest honest fit.
    contextType: 'matter-grid',
  },
  () =>
    import('./ReconciliationWorkspaceWidget').then(m => ({
      default:
        m.ReconciliationWorkspaceWidget as unknown as import('../../types/widget-types').WorkspaceWidgetComponent,
    }))
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
    // FR-B1/FR-C3 (task 020): metrics/report dashboard surface.
    contextType: 'dashboard',
    // FR-15 (task 050): standalone metrics dashboard — see METRICS_DASHBOARD_OPT_OUT.
    assistantContract: METRICS_DASHBOARD_OPT_OUT,
  },
  createMetricsDashboardFactory('matters-dashboard')
);

// ---------------------------------------------------------------------------
// ai-advanced-capabilities-analysis-hub-r1 task 030 (FR-10) — Analysis hub +
// per-type creation wizard.
//
// Two registrations, both owned by task 030 per the task-040/030
// parallel-execution handoff (task 040 built CreateAnalysisWizardWidget but
// deliberately did NOT self-register it — see that file's header doc + this
// project's `notes/task-040-deviations.md` §6):
//   17. 'analysis-hub'          — the platform home/launcher tab (this task).
//   18. 'create-analysis-wizard' — the per-type creation wizard (task 040),
//        opened by the hub's Agreement Review card via a `widget_load` dispatch.
// ---------------------------------------------------------------------------

registerWorkspaceWidget(
  /**
   * Widget type for the Analysis platform's home/launcher surface: three
   * "Create new" work-type cards (Agreement Review live; Legal Research +
   * Patent Application coming-soon) above a DataGrid of existing
   * `sprk_analysis` records. Task 050 (entry routing) is expected to be the
   * primary dispatcher of this type; it is also directly mountable for tests.
   */
  'analysis-hub',
  {
    displayName: 'Analysis',
    category: 'analysis',
    icon: 'DocumentSearchRegular',
    // allowMultiple=false: the hub is a singleton home surface — a second
    // "Create new" launcher tab would be confusing alongside the first.
    allowMultiple: false,
    /**
     * defaultOrder=150: sits after the workspace/wizard dispatchers (80–140,
     * since the hub is itself a launcher) and before the metrics dashboards
     * group (300).
     */
    defaultOrder: 150,
    // FR-15 (task 050): Analysis platform launcher surface — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
  },
  () =>
    import('./AnalysisHubWidget').then(m => ({
      default: m.AnalysisHubWidget as import('../../types/widget-types').WorkspaceWidgetComponent,
    }))
);

registerWorkspaceWidget(
  /**
   * Type string dispatched by AnalysisHubWidget's Agreement Review card
   * (`widget_load` on the `workspace` channel) and, per task 050, any other
   * entry point that starts a per-type Analysis creation flow. MUST match
   * task 040's `CreateAnalysisWizardWidget` export — do not rename without
   * updating both the hub's dispatch call site and this registration.
   */
  'create-analysis-wizard',
  {
    displayName: 'Create Analysis',
    category: 'wizard',
    icon: 'DocumentAdd24Regular',
    // allowMultiple=false: mirrors 'create-matter-wizard' — opening a second
    // Create Analysis wizard replaces the first tab rather than stacking.
    allowMultiple: false,
    /**
     * defaultOrder=87: grouped with the other embedded-wizard dispatchers
     * (create-matter-wizard 80, document-upload-wizard 85, search-select-wizard 90).
     */
    defaultOrder: 87,
    // FR-15 (task 050): per-type Analysis creation wizard — see DISPATCHER_OPT_OUT.
    assistantContract: DISPATCHER_OPT_OUT,
  },
  () =>
    import('./CreateAnalysisWizardWidget').then(m => ({
      default: m.CreateAnalysisWizardWidget as import('../../types/widget-types').WorkspaceWidgetComponent,
    }))
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
