// @spaarke/ai-widgets — barrel export

// ---------------------------------------------------------------------------
// Side-effect: register all R1 output widgets into WorkspaceWidgetRegistry
// (AIPU2-080 — data-refreshed restore, D-08)
// ---------------------------------------------------------------------------
import { registerWorkspaceWidgets } from './widgets/workspace/register-workspace-widgets';
registerWorkspaceWidgets();

// ---------------------------------------------------------------------------
// Side-effect: register DocumentViewerWidget (R4 task 042 / W-4)
// First end-to-end Assistant → Workspace `widget_load` demo (FR-02).
// ---------------------------------------------------------------------------
import { registerDocumentViewerWidget } from './widgets/workspace/register-document-viewer-widget';
registerDocumentViewerWidget();

// ---------------------------------------------------------------------------
// Side-effect: register SearchCriteriaResultWidget (R4 task 043 / W-5)
// First end-to-end Context → Workspace `widget_load` demo (FR-03).
// ---------------------------------------------------------------------------
import { registerSearchCriteriaResultWidget } from './widgets/workspace/register-search-criteria-result-widget';
registerSearchCriteriaResultWidget();

// ---------------------------------------------------------------------------
// Side-effect: register StructuredOutputStreamWidget (R5 task 017 / D2-07)
// Schema-driven structured AI output renderer — destination for Summarize
// streaming (FR-02) AND Insights playbook static rendering (FR-13 via D2-16).
// ---------------------------------------------------------------------------
import { registerStructuredOutputStreamWidget } from './widgets/workspace/register-structured-output-stream-widget';
registerStructuredOutputStreamWidget();

// ---------------------------------------------------------------------------
// Side-effect: register all 6 R1 source widgets into ContextWidgetRegistry
// (AIPU2-081 — migrate source widgets to context pane)
// ---------------------------------------------------------------------------
import { registerContextWidgets } from './widgets/context/register-context-widgets';
registerContextWidgets();

// ---------------------------------------------------------------------------
// Types — React component prop contracts (tasks AIPU2-072/073)
// ---------------------------------------------------------------------------

export type {
  WorkspaceWidgetProps,
  WorkspaceWidgetComponent,
  ContextWidgetProps,
  ContextWidgetComponent,
  // Re-exports of task-071 types (via widget-types.ts pass-through):
  WidgetRenderContext,
  Selection,
  ActionResult,
  WidgetState,
  WidgetRegistryEntry,
  WidgetRegistryMetadata,
  WorkspaceWidget,
  WidgetActionDescriptor,
  ContextWidget,
} from './types/widget-types';

// WidgetMetadata — canonical definition from shared.ts (task AIPU2-071).
// Required by WorkspaceWidgetRegistry.registerWorkspaceWidget().
export type { WidgetMetadata } from './types/shared';

// ---------------------------------------------------------------------------
// Types — Canonical WorkspaceTab (R6 Pillar 6a gate; FR-31)
//
// Shared contract for Pillars 6a (state model), 6b (chat tools that mutate
// tabs), 6c (workspace events), 7 (memory composition), and 9 (visibility
// contract). See `./types/WorkspaceTab.ts` for the full design rationale.
// ---------------------------------------------------------------------------

export type {
  WorkspaceTab,
  WorkspaceTabWidgetType,
  WorkspaceTabWidgetData,
  SummaryTabWidgetData,
  DocumentViewerTabWidgetData,
  DashboardTabWidgetData,
  TableTabWidgetData,
  WorkspaceTabSourceProvenance,
  WorkspaceTabMatterContext,
} from './types/WorkspaceTab';

// ---------------------------------------------------------------------------
// Types — Pillar 9 Widget Visibility Contract (R6 task 071; FR-55)
//
// Discriminated union (4 variants — Summary, DocumentViewer, Dashboard, Table)
// describing the agent-visible state each widget MAY opt into exposing to
// Pillar 9's prompt builder. Consumed by:
//   - task 072 (WorkspaceWidgetRegistry getVisibleState extension)
//   - task 073 (per-widget implementations)
//   - task 074 (Pillar 9 prompt builder — per-turn system-prompt snippet)
//
// Privacy default per ADR-015: widgets that don't implement
// `getAgentVisibleState()` contribute nothing to the prompt. Opt-in is
// explicit. See `./types/SerializedWidgetState.ts` for full per-variant
// rationale.
// ---------------------------------------------------------------------------

export type {
  SerializedWidgetState,
  SerializedSummaryState,
  SerializedDocumentViewerState,
  SerializedDashboardState,
  SerializedTableState,
  GetAgentVisibleState,
  _DiscriminatorAlignment,
} from './types/SerializedWidgetState';

export { assertNeverSerializedState } from './types/SerializedWidgetState';

export * from './types/event-types';

// ---------------------------------------------------------------------------
// Registries: WorkspaceWidgetRegistry and ContextWidgetRegistry
// ---------------------------------------------------------------------------

// WorkspaceWidgetRegistry — lazy-load with GenericTextWidget fallback
export {
  registerWorkspaceWidget,
  replaceWorkspaceWidget,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  getWorkspaceWidgetVisibleStateFn,
  getAllWorkspaceWidgetTypes,
  hasWorkspaceWidget,
  clearWorkspaceRegistry,
} from './registry/WorkspaceWidgetRegistry';

// Task 072 (D-C-27) — Pillar 9 visibility extension.
export type {
  WorkspaceWidgetRegistration,
  RegistryGetAgentVisibleState,
} from './registry/WorkspaceWidgetRegistry';

// ContextWidgetRegistry — lazy-load with null-return for unknown types
export {
  registerContextWidget,
  replaceContextWidget,
  resolveContextWidget,
  hasContextWidget,
  getAllContextWidgetTypes,
  clearContextRegistry,
} from './registry/ContextWidgetRegistry';

export type { ContextWidgetRegistration } from './registry/ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Widgets: GenericTextWidget (fallback for unregistered workspace widget types)
// ---------------------------------------------------------------------------

export { default as GenericTextWidget } from './widgets/GenericTextWidget';

// ---------------------------------------------------------------------------
// Widgets: DocumentViewerWidget — Assistant pane mount-source demo (R4 task 042)
//
// First end-to-end PaneEventBus `widget_load` demo (FR-02): when the user
// attaches a file in the Assistant chat input, ConversationPane dispatches
// `widget_load` on the workspace channel and this widget mounts as a new
// workspace tab showing the file's extracted text preview.
// Registered under 'document-viewer' via register-document-viewer-widget.ts.
// ---------------------------------------------------------------------------

export { default as DocumentViewerWidget } from './widgets/workspace/DocumentViewerWidget';
export type { DocumentViewerWidgetData } from './widgets/workspace/DocumentViewerWidget';
export { DOCUMENT_VIEWER_WIDGET_TYPE } from './widgets/workspace/register-document-viewer-widget';

// ---------------------------------------------------------------------------
// Widgets: SearchCriteriaResultWidget — Context pane mount-source demo (R4 task 043)
//
// First end-to-end Context → Workspace PaneEventBus `widget_load` demo (FR-03):
// when the user checks "Also add to Workspace" in the Semantic Search criteria
// tool and clicks Search, SemanticSearchCriteriaTool dispatches `widget_load`
// on the workspace channel and this widget mounts as a new workspace tab
// showing the captured search criteria summary.
// Registered under 'search-criteria-result' via
// register-search-criteria-result-widget.ts.
// ---------------------------------------------------------------------------

export { default as SearchCriteriaResultWidget } from './widgets/workspace/SearchCriteriaResultWidget';
export type { SearchCriteriaResultWidgetData } from './widgets/workspace/SearchCriteriaResultWidget';
export { SEARCH_CRITERIA_RESULT_WIDGET_TYPE } from './widgets/workspace/register-search-criteria-result-widget';

// ---------------------------------------------------------------------------
// Widgets: StructuredOutputStreamWidget — schema-driven AI output (R5 task 017)
//
// Workspace widget that renders structured AI output PROGRESSIVELY via
// `FieldDelta` SSE events (FR-02 — Summarize streaming) OR statically from a
// pre-filled envelope (FR-13 — Insights playbook rendering via D2-16 / task
// 026). The same widget serves both consumers via different schemas — that
// "schema-driven" design is the load-bearing reuse claim of R5's platform
// extensibility story (risk UR-02 mitigation).
//
// Two CONCRETE schemas are exported (SUMMARIZE_SCHEMA, INSIGHTS_PLAYBOOK_SCHEMA)
// so downstream consumers (chat-pane dispatcher, InsightsResponseRenderer) do
// not redeclare them. Per task 006 spike: schema declaration order = stream
// arrival order = render order (TL;DR first for Summarize).
//
// Registered under 'structured-output-stream' via
// register-structured-output-stream-widget.ts.
// ---------------------------------------------------------------------------

export { default as StructuredOutputStreamWidget } from './widgets/workspace/StructuredOutputStreamWidget';
export type {
  StructuredOutputStreamWidgetData,
  StructuredOutputSchema,
  StructuredOutputField,
  StructuredOutputDisplayHint,
} from './widgets/workspace/StructuredOutputStreamWidget';
export {
  SUMMARIZE_SCHEMA,
  INSIGHTS_PLAYBOOK_SCHEMA,
  SUM_CHAT_OUTPUT_SCHEMA,
} from './widgets/workspace/StructuredOutputStreamWidget';
export { STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE } from './widgets/workspace/register-structured-output-stream-widget';

// ---------------------------------------------------------------------------
// Widgets: RedlineViewerWidget — side-by-side document comparison (AIPU2-085)
// Exported so consumers can reference the component directly and type-check
// DocumentDiff payloads. Registration under 'redline-viewer' occurs via the
// register-workspace-widgets side-effect import at the top of this file.
// ---------------------------------------------------------------------------

export { default as RedlineViewerWidget } from './widgets/workspace/RedlineViewerWidget';
export type {
  RedlineViewerData,
  RedlineViewerActions,
  RedlineViewerQueryParams,
  DiffSection,
  DiffChange,
  DiffChangeType,
} from './widgets/workspace/RedlineViewerWidget';
export { serializeRedlineState } from './widgets/workspace/RedlineViewerWidget';

// ---------------------------------------------------------------------------
// Widgets: CreateMatterWizardWidget — embedded Create Matter flow (AIPU2-104)
//
// Embeds CreateMatterWizard from @spaarke/ui-components in a workspace tab
// without modal chrome. Subscribes to wizard_step PaneEventBus events so
// ConversationPane AI can drive step navigation and field pre-fill.
// Registered under 'create-matter-wizard' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as CreateMatterWizardWidget } from './widgets/workspace/CreateMatterWizardWidget';
export type {
  CreateMatterWizardData,
  CreateMatterWizardQueryParams,
} from './widgets/workspace/CreateMatterWizardWidget';
export { serializeCreateMatterWizardState } from './widgets/workspace/CreateMatterWizardWidget';

// ---------------------------------------------------------------------------
// Widgets: DocumentUploadWizardWidget — embedded document upload (AIPU2-104)
//
// Three-step file upload flow (Select → Details → Review & Upload) embedded
// as a workspace tab. On completion dispatches widget_load for DocumentViewer
// and context_update with the new document entity info.
// Registered under 'document-upload-wizard' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as DocumentUploadWizardWidget } from './widgets/workspace/DocumentUploadWizardWidget';
export type {
  DocumentUploadWizardData,
  DocumentUploadWizardQueryParams,
} from './widgets/workspace/DocumentUploadWizardWidget';
export { serializeDocumentUploadWizardState } from './widgets/workspace/DocumentUploadWizardWidget';

// ---------------------------------------------------------------------------
// Widgets: SearchSelectWizardWidget — embedded search-and-select (AIPU2-104)
//
// Two-step record picker (Search → Confirm) embedded as a workspace tab.
// On selection dispatches context_update with entity id/type/name so the
// Context pane can update its entity chip.
// Registered under 'search-select-wizard' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as SearchSelectWizardWidget } from './widgets/workspace/SearchSelectWizardWidget';
export type {
  SearchSelectWizardData,
  SearchSelectWizardQueryParams,
  SearchResultItem,
} from './widgets/workspace/SearchSelectWizardWidget';
export { serializeSearchSelectWizardState } from './widgets/workspace/SearchSelectWizardWidget';

// ---------------------------------------------------------------------------
// Widgets: EmailComposeWidget — Analysis Builder intent dispatcher (task 044)
//
// Thin dispatcher that opens the Analysis Builder (Playbook Library Code Page)
// with the `email-compose` intent pre-configured (FR-19: Send Email card).
// Registered under 'email-compose' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as EmailComposeWidget } from './widgets/workspace/EmailComposeWidget';
export type { EmailComposeData } from './widgets/workspace/EmailComposeWidget';
export { serializeEmailComposeState } from './widgets/workspace/EmailComposeWidget';

// ---------------------------------------------------------------------------
// Widgets: MeetingScheduleWidget — Analysis Builder intent dispatcher (task 044)
//
// Thin dispatcher that opens the Analysis Builder (Playbook Library Code Page)
// with the `meeting-schedule` intent pre-configured (FR-19: Schedule Meeting).
// Registered under 'meeting-schedule' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as MeetingScheduleWidget } from './widgets/workspace/MeetingScheduleWidget';
export type { MeetingScheduleData } from './widgets/workspace/MeetingScheduleWidget';
export { serializeMeetingScheduleState } from './widgets/workspace/MeetingScheduleWidget';

// ---------------------------------------------------------------------------
// Widgets: CreateProjectWizardWidget — Existing Code Page dispatcher (task 043)
//
// Thin dispatcher that opens the existing `sprk_createprojectwizard` Code
// Page via `Xrm.Navigation.navigateTo` (FR-19: Create Project card). The
// widget is a launcher only — the wizard UI lives in the existing Code Page
// (REUSE per OC-04 / ADR-012, NOT re-authored).
// Registered under 'create-project-wizard' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as CreateProjectWizardWidget } from './widgets/workspace/CreateProjectWizardWidget';
export type { CreateProjectWizardData } from './widgets/workspace/CreateProjectWizardWidget';
export { serializeCreateProjectWizardState } from './widgets/workspace/CreateProjectWizardWidget';

// ---------------------------------------------------------------------------
// Widgets: FindSimilarWizardWidget — Existing Code Page dispatcher (task 043)
//
// Thin dispatcher that opens the existing `sprk_findsimilar` Code Page via
// `Xrm.Navigation.navigateTo` (FR-19: Find Similar card). The widget is a
// launcher only — the Find Similar UI lives in the existing Code Page
// (REUSE per OC-04 / ADR-012, NOT re-authored).
// Registered under 'find-similar-wizard' via register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

export { default as FindSimilarWizardWidget } from './widgets/workspace/FindSimilarWizardWidget';
export type { FindSimilarWizardData } from './widgets/workspace/FindSimilarWizardWidget';
export { serializeFindSimilarWizardState } from './widgets/workspace/FindSimilarWizardWidget';

// ---------------------------------------------------------------------------
// Launchers: REMOVED in Round 4 Fix 2 (task 085)
//
// The package-local `launchAssignWorkWizard` (task 045) has been superseded by
// the shared `launchAssignWorkWizard` exported from `@spaarke/ui-components`
// (see `WorkspaceShell/wizardLaunchers.ts`). The shared launcher uses the same
// verbatim Xrm.Navigation shape as LegalWorkspace's WorkspaceGrid.tsx and
// applies frame-walking Xrm resolution (the package-local helper only checked
// `window.Xrm`, which missed nested-iframe cases).
//
// Migration:
//   - Old: `import { launchAssignWorkWizard } from '@spaarke/ai-widgets'`
//   - New: `import { launchAssignWorkWizard } from '@spaarke/ui-components'`
//
// The shared module also exports launchers for the other six Get Started
// wizards (Create Matter, Create Project, Summarize Files, Find Similar,
// Email Compose, Schedule Meeting) — see `@spaarke/ui-components` exports.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Widgets: ProgressTrackerWidget (context pane — workflow step progress)
//
// Exported so consumers can reference the component directly and type-check
// context_update payloads. The registerContextWidget() call below registers
// the widget at module-load time under the 'progress-tracker' type key.
// ---------------------------------------------------------------------------

export { default as ProgressTrackerWidget } from './widgets/context/ProgressTrackerWidget';
export type { ProgressTrackerData, WorkflowStep } from './widgets/context/ProgressTrackerWidget';

import { registerContextWidget } from './registry/ContextWidgetRegistry';
import type { ContextWidgetComponent } from './types/widget-types';
registerContextWidget('progress-tracker', {
  factory: () => import('./widgets/context/ProgressTrackerWidget').then(m => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// Widgets: PlaybookGalleryWidget (context pane — Welcome / playbook-gallery stage)
//
// Exported so consumers can reference the component directly and type-check
// PlaybookGalleryData payloads. Registered under 'playbook-gallery'.
// ---------------------------------------------------------------------------

export { default as PlaybookGalleryWidget } from './widgets/context/PlaybookGalleryWidget';
export type { PlaybookGalleryData, PlaybookSummary } from './widgets/context/PlaybookGalleryWidget';

registerContextWidget('playbook-gallery', {
  factory: () =>
    // Type-erasure cast: registry stores ContextWidgetComponent<unknown>; the
    // widget's default export is typed ContextWidgetComponent<PlaybookGalleryData>.
    // The generic variance is unavoidable at the registry boundary — at render
    // time the widget receives its typed data via the registry contract.
    import('./widgets/context/PlaybookGalleryWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Widgets: GetStartedCardsWidget (context pane — Welcome stage, FR-18)
//
// Exported so consumers (ContextPaneController in SpaarkeAi) can render it
// directly with an `onCardClick` callback prop. Also registered under
// 'get-started-cards' for symmetry with the other context widgets and so
// the registry stays the single source of truth for "what can render in
// the Context pane".
//
// Note: GetStartedCardsWidget's props (`onCardClick`, `className`) are NOT
// the standard `ContextWidgetProps` shape — it is a client-driven welcome
// widget, not a server-driven `context_update` target. The registry factory
// uses a type cast at the boundary so the widget can still be discovered by
// `resolveContextWidget('get-started-cards')` if needed; callers that need
// to wire `onCardClick` should import the named export directly and render
// it themselves (this is what ContextPaneController does for the welcome
// stage). PlaybookGalleryWidget registration is RETAINED above for FR-21
// (non-welcome stage resolution).
// ---------------------------------------------------------------------------

export { GetStartedCardsWidget } from './widgets/context/GetStartedCardsWidget';
export type { GetStartedCardId, GetStartedCardsWidgetProps } from './widgets/context/GetStartedCardsWidget';

registerContextWidget('get-started-cards', {
  factory: () =>
    import('./widgets/context/GetStartedCardsWidget').then(m => ({
      // Intentional cast: GetStartedCardsWidget's prop shape differs from
      // ContextWidgetComponent's (it takes `onCardClick` + `className` instead
      // of `data` + `widgetType` + `isLoading`). The registry entry exists
      // for discoverability + symmetry; the actual render uses the named
      // export directly so the callback is wirable.
      default: m.GetStartedCardsWidget as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Widgets: FindingsWidget (context pane — sources-citations stage)
//
// Exported so consumers can reference the component directly and type-check
// FindingsData payloads. Registered under 'findings'.
// Citation link clicks dispatch context_highlight to the 'context' channel
// so the active DocumentViewer scrolls to / highlights the cited passage.
// ---------------------------------------------------------------------------

export { default as FindingsWidget } from './widgets/context/FindingsWidget';
export type { FindingsData, Finding, Citation, RiskLevel } from './widgets/context/FindingsWidget';

registerContextWidget('findings', {
  factory: () =>
    // Type-erasure cast: registry stores ContextWidgetComponent<unknown>; the
    // widget's default export is typed ContextWidgetComponent<FindingsData>.
    // Generic variance at the registry boundary — see PlaybookGalleryWidget
    // registration above for the same pattern.
    import('./widgets/context/FindingsWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Widgets: FilePreviewContextWidget (context pane — chat-driven Summarize)
//
// R5 task 018 / D2-09. Inline (non-modal) file preview for files uploaded
// into the active chat session. Wraps the extracted `RichFilePreview`
// renderer (task 013 / D2-08) instead of rebuilding iframe/metadata/menu
// UI (R5 CLAUDE.md §3.1 reuse mandate). Per-file 3-dot menu reuses the
// canonical `DocumentRowMenu` 12-action component. Dispatches additive
// `context.file_selected` events on the `context` channel (R5 task 016 /
// D2-06; ADR-030 additive-types rule). Registered under 'file-preview'.
// ---------------------------------------------------------------------------

export { default as FilePreviewContextWidget } from './widgets/context/FilePreviewContextWidget';
export type {
  FilePreviewContextData,
  FilePreviewContextFile,
  FilePreviewContextRenderProps,
  FilePreviewContextWidgetProps,
  FilePreviewFileActionHandler,
  UseSummarizeOnlyResult,
  DispatchSummarizeOnlyResult,
} from './widgets/context/FilePreviewContextWidget';
export {
  FILE_PREVIEW_CONTEXT_WIDGET_TYPE,
  useSummarizeOnly,
  dispatchSummarizeOnly,
} from './widgets/context/FilePreviewContextWidget';

registerContextWidget('file-preview', {
  factory: () =>
    // Type-erasure cast: registry stores ContextWidgetComponent<unknown>; the
    // widget's default export is typed ContextWidgetComponent<FilePreviewContextData>.
    // Generic variance at the registry boundary — see PlaybookGalleryWidget
    // registration above for the same pattern.
    import('./widgets/context/FilePreviewContextWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Widgets: ExecutionTraceWidget (context pane — Claude-Code-like trace)
//
// R6 task 061 / D-C-14. Subscribes to the six `context.*` trace event types
// added by R6 task 059 (D-C-12) and renders an ordered timeline of the chat
// agent's deterministic activity (tool calls, knowledge retrievals,
// playbook-node executions, decisions). Per ADR-015 BINDING: renders only
// the typed enumerated fields from each event payload (tool name + decision
// + timestamp + numeric metrics) — NEVER user message text or document
// content. Per ADR-030 + NFR-05: subscribes to the existing `context`
// channel — no new channel introduced.
//
// NOTE: registration in ContextWidgetRegistry is performed by task 062 — this
// task only exposes the widget + its types via the package barrel.
// ---------------------------------------------------------------------------

export { default as ExecutionTraceWidget } from './widgets/context/ExecutionTraceWidget';
export type {
  ExecutionTraceData,
  ExecutionTraceWidgetProps,
} from './widgets/context/ExecutionTraceWidget';
export {
  EXECUTION_TRACE_WIDGET_TYPE,
  MAX_TRACE_ENTRIES,
} from './widgets/context/ExecutionTraceWidget';

// R6 task 062 / D-C-15: register the widget so the SpaarkeAi shell can mount it
// as the Context-pane primary widget via `resolveContextWidget('execution-trace')`.
// Registration is idempotent (the registry is first-wins; the parallel inline
// path in `src/registry/register-context-widgets.ts` is the mirror call for
// shell entry points that bypass this barrel — both call sites are deliberate
// per the FilePreviewContextWidget pattern above).
registerContextWidget('execution-trace', {
  factory: () =>
    // Type-erasure cast: registry stores ContextWidgetComponent<unknown>; the
    // widget's default export is typed ContextWidgetComponent<ExecutionTraceData>.
    // Generic variance at the registry boundary — see PlaybookGalleryWidget
    // registration above for the same pattern.
    import('./widgets/context/ExecutionTraceWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Hooks: useWorkspaceLayouts (R4 task 051 / C-3 — consolidated workspace-layouts hook)
//
// Single shared-lib hook replacing the two divergent copies that previously
// lived in src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts and
// src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts. Both consumers
// now import from here per FR-13 + ADR-012 (shared lib) + ADR-028 (function-
// based auth, injected deps).
// ---------------------------------------------------------------------------

export { useWorkspaceLayouts, invalidateLayoutCache } from './hooks/useWorkspaceLayouts';
export type {
  WorkspaceLayoutDto,
  WorkspaceLoadingStatus,
  AuthenticatedFetch,
  UseWorkspaceLayoutsOptions,
  UseWorkspaceLayoutsResult,
} from './hooks/useWorkspaceLayouts';

// ---------------------------------------------------------------------------
// Providers: AiSessionProvider (R2 session state + PaneEventBus routing)
// ---------------------------------------------------------------------------

// AiSessionProvider — replaces R1 StandaloneAiProvider; routes SSE events to PaneEventBus
export { AiSessionProvider } from './providers/AiSessionProvider';
export type { AiSessionContextValue, AiSessionProviderProps, AiContextMapping } from './providers/AiSessionProvider';
export { AI_SESSION_CHAT_SESSION_KEY, AI_SESSION_PLAYBOOK_KEY } from './providers/AiSessionProvider';

// useAiSession — consumer hook for AiSessionContext (replaces R1 useStandaloneAi)
export { useAiSession } from './providers/useAiSession';

// ---------------------------------------------------------------------------
// Components: InsightSummaryCard (Insights Engine Widgets r1 — Task 030 scaffold)
//
// Per-record AI insight surface composed of a Fluent v9 Card (inline) with an
// optional manual modal expand (Dialog — wired in Task 031). FR-01 contract:
//   { topic, subject, mode?, parameters?, kpiSlot?, onCitationClick? }
//
// Q-U3 (owner deferral): NO `onFeedback` prop. Feedback affordance deferred to
// r2+ pending AIPU2 Cosmos `feedback` container landing on master (ADR-015).
//
// See projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md
// for the package-home + reuse-anchors rationale (ratified 2026-06-10).
// ---------------------------------------------------------------------------

export { InsightSummaryCard } from './components/InsightSummaryCard';
export type { InsightSummaryCardProps, InsightCitationRef } from './components/InsightSummaryCard';

// Task 035 — SC-01 dev sandbox (Storybook-equivalent). Renders all 6 FR-06
// states in a responsive grid with a light/dark theme toggle and an inline
// props table. Importable by any host (dev playground, internal admin page).
// See src/components/InsightSummaryCard/README.md for the "why no Storybook"
// rationale (DR-001 §Negative).
export { InsightSummaryCardSandbox } from './components/InsightSummaryCard';

// ---------------------------------------------------------------------------
// Components: ConfidenceIndicator (AIPU2-091)
//
// Per-response confidence bar rendered below AI messages. Driven by the safety
// pipeline confidence score; color-coded high/medium/low using Fluent v9
// semantic status tokens. Low confidence adds a disclaimer text.
// ---------------------------------------------------------------------------

export { ConfidenceIndicator } from './components/ConfidenceIndicator';
export type { ConfidenceIndicatorProps, ConfidenceLevel } from './components/ConfidenceIndicator';

// ---------------------------------------------------------------------------
// Components: FeedbackButtons (thumbs up/down + optional comment — AIPU2-092)
//
// Non-intrusive rating control rendered beneath each completed AI message.
// Thumbs-up submits immediately; thumbs-down reveals an optional Textarea.
// Calls POST /api/ai/feedback and shows a brief checkmark confirmation.
// Only render when streaming is complete (isStreaming === false).
// ---------------------------------------------------------------------------

export { FeedbackButtons } from './components/FeedbackButtons';
export type { FeedbackButtonsProps, FeedbackRating } from './components/FeedbackButtons';

// ---------------------------------------------------------------------------
// Interactions: text-selection cross-pane integration (AIPU2-101)
//
// TextSelectionListener — declarative wrapper component for workspace widgets.
//   Listens for mouseup/selectionchange, debounces 300 ms, enforces minimum
//   selection length, and dispatches selection_changed on the workspace channel.
//
// useTextSelection — imperative hook for advanced widget authors who need direct
//   control over when selection events are dispatched (e.g. virtual-scroll canvases).
// ---------------------------------------------------------------------------

export { TextSelectionListener } from './interactions/TextSelectionListener';
export type { TextSelectionListenerProps } from './interactions/TextSelectionListener';

export { useTextSelection } from './interactions/useTextSelection';
export type { UseTextSelectionResult } from './interactions/useTextSelection';

// ---------------------------------------------------------------------------
// Events: PaneEventBus, typed channels, React context + hooks
// ---------------------------------------------------------------------------

// Core bus class (for advanced usage and testing — most consumers use the hooks)
export { PaneEventBus } from './events/PaneEventBus';

// Typed event definitions
export type {
  PaneChannel,
  PaneChannelEventMap,
  PaneEventHandler,
  WorkspacePaneEvent,
  WorkspaceWidgetLoadEvent,
  ContextPaneEvent,
  ConversationPaneEvent,
  SafetyPaneEvent,
  PlaybookWidgetConfig,
  WizardStepEvent,
} from './events/PaneEventTypes';

// React context provider — wrap the three-pane shell root with this
export { PaneEventBusProvider } from './events/PaneEventBusContext';
export type { PaneEventBusProviderProps } from './events/PaneEventBusContext';

// React hooks — primary API for components
export { usePaneEvent } from './events/usePaneEvent';
export { useDispatchPaneEvent } from './events/useDispatchPaneEvent';
export type { DispatchPaneEvent } from './events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Safety annotation UI (AIPU2-090 — FR-402, FR-403)
//
// SafetyAnnotationOverlay — subscribes to 'safety' PaneEventBus channel and
// applies retroactive groundedness highlights + citation verification badges
// ~200 ms after the last streaming token (D-03: stream + retroactive).
//
// AnnotatedMessageContent — stateless unified render of groundedness +
// citation badge layers; exported for direct use when annotation state is
// already available (e.g. in tests or server-side pre-annotated payloads).
//
// CitationBadge — inline Fluent v9 Badge for citation verification status.
//   Variants: verified (green CheckmarkCircle), unverified (orange Warning),
//             partial (blue ArrowSwap).
//
// GroundednessHighlight — wraps text with visual indicators for ungrounded
//   segments: dashed underline + colorStatusWarningBackground1 fill.
// ---------------------------------------------------------------------------

export { default as SafetyAnnotationOverlay } from './components/SafetyAnnotationOverlay';
export { AnnotatedMessageContent } from './components/SafetyAnnotationOverlay';
export type { SafetyAnnotationOverlayProps, AnnotatedMessageContentProps } from './components/SafetyAnnotationOverlay';

export { CitationBadge } from './components/CitationBadge';
export type {
  CitationBadgeProps,
  CitationVerificationResult,
  CitationVerificationStatus,
} from './components/CitationBadge';

export { GroundednessHighlight } from './components/GroundednessHighlight';
export type { GroundednessHighlightProps, GroundednessSegment } from './components/GroundednessHighlight';

// ---------------------------------------------------------------------------
// Interactions: TabContextMapping (AIPU2-103 — cross-pane tab/context adapter)
//
// getContextWidgetForTab — maps a workspace widget type to the recommended
//   context widget type. Returns null when no recommendation exists (keep
//   the current context widget unchanged).
//
// TAB_CONTEXT_MAPPING — the underlying readonly mapping Record, exported for
//   testing and for consumers that need to inspect or extend the mapping.
// ---------------------------------------------------------------------------

export { getContextWidgetForTab, TAB_CONTEXT_MAPPING } from './interactions/TabContextMapping';

// ---------------------------------------------------------------------------
// Interactions: StageTransitionRules (AIPU2-105 — four-stage pane lifecycle)
//
// determineStage — pure function: SessionState → PaneStage. Centralises all
//   stage transition logic so every pane and the ShellStageManager compute
//   the current stage consistently from the same inputs.
//
// shouldReset — convenience predicate for the "any → welcome" hard reset
//   (session cleared / deleted).
//
// PaneStage / SessionState — exported types for consumer type-safety.
// ---------------------------------------------------------------------------

export type { PaneStage, SessionState } from './interactions/StageTransitionRules';
export { determineStage, shouldReset } from './interactions/StageTransitionRules';

// ---------------------------------------------------------------------------
// Interactions: Citation link cross-pane highlight flow (AIPU2-100)
//
// handleCitationClick — pure utility dispatching context_highlight to the
//   'context' PaneEventBus channel. Accepts a pre-resolved dispatch function so
//   it is usable outside React (event callbacks, tests, imperative code).
//
// useCitationLink — React hook returning a stable handleCitationClick callback.
//   Wire the returned function to citation anchor click handlers in workspace
//   widgets. Dispatches context_highlight synchronously (<50 ms, AC-1).
//   Requires a PaneEventBusProvider ancestor.
//
// CitationClickPayload — payload type for handleCitationClick.
// CitationClickHandler — function type returned by useCitationLink.
// ---------------------------------------------------------------------------

export { handleCitationClick } from './interactions/CitationLinkHandler';
export type { CitationClickPayload } from './interactions/CitationLinkHandler';

export { useCitationLink } from './interactions/useCitationLink';
export type { CitationClickHandler } from './interactions/useCitationLink';
