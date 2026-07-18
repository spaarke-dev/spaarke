// @spaarke/compose-components — barrel export
//
// TipTap-based ComposeWorkspace + ComposeEditor + ComposeToolbar widgets
// for the spaarkeai-compose-r1 drafting workspace. Mounted by:
//   - SpaarkeAi App.tsx (Path A modal — direct <ComposeWorkspace> mount)
//   - LegalWorkspace section shim `composeEditor.registration.ts` (Pattern D)
//
// Task 091 (Phase 7 pivot, 2026-07-01): promoted from SpaarkeAi-solution-local
// (`src/solutions/SpaarkeAi/src/components/compose/`) to shared lib per
// Spike #2 §11 open item #2 promotion trigger + Calendar Pattern D precedent.
// `compose-contracts.ts` (Flows 1-6) promoted alongside so the dependency
// graph stays unidirectional (shared lib does NOT import from solutions/).
//
// Licensing constraint (LOCKED at Spike #1, 2026-06-29): TipTap core +
// StarterKit + 11 standard MIT extensions ONLY. No TipTap Pro packages.
// DOCX bridge: mammoth ^1.8.0 (BSD-2-Clause) + docx ^9.0.3 (MIT). All OSS.

// -------------------------------------------------------------------------
// Editor (Phase 4 task 045 — pre-Phase 7 origin)
// -------------------------------------------------------------------------
export { ComposeEditor } from './widgets/ComposeEditor';
export type {
  ComposeEditorProps,
  ComposeEditorHandle,
  ComposeEditorDocumentRef,
  ComposeDraftPayload,
  ComposeDraftProvenance,
} from './widgets/ComposeEditor';
export { ComposeFormatToolbar } from './widgets/ComposeFormatToolbar';
export type { ComposeFormatToolbarProps } from './widgets/ComposeFormatToolbar';
export {
  ComposeAiToolbar,
  registerComposeAiToolbarAction,
  getComposeAiToolbarActions,
  subscribeComposeAiToolbarActions,
  __resetComposeAiToolbarActionsForTests,
} from './widgets/ComposeAiToolbar';
export type { ComposeAiToolbarProps, ComposeAiToolbarAction, ComposeActionEnqueue } from './widgets/ComposeAiToolbar';

// AI-action toolbar activation (task 048 — closes e2e-gap 2.2): on Compose mount,
// reads GET /api/ai/capabilities?surface=compose and registers each returned
// capability's real bindingId onto the matching toolbar action.
export { useComposeToolbarActivation } from './widgets/useComposeToolbarActivation';
export type { UseComposeToolbarActivationOptions } from './widgets/useComposeToolbarActivation';

// -------------------------------------------------------------------------
// Workspace-level widgets (Phase 7 task 091 — moved from SpaarkeAi)
// -------------------------------------------------------------------------
export { ComposeWorkspace } from './widgets/ComposeWorkspace';
export type { ComposeWorkspaceProps } from './widgets/ComposeWorkspace';
export { ComposeToolbar } from './widgets/ComposeToolbar';
export type { ComposeToolbarProps } from './widgets/ComposeToolbar';
export { ComposeBannerStack } from './widgets/ComposeBannerStack';
export type { ComposeBannerStackProps } from './widgets/ComposeBannerStack';
export { ComposeEmptyState } from './widgets/ComposeEmptyState';
export type { ComposeEmptyStateProps } from './widgets/ComposeEmptyState';
export { ComposeConflictDialog } from './widgets/ComposeConflictDialog';

// -------------------------------------------------------------------------
// Return-from-Word re-anchoring (FR-27 / task 054)
// -------------------------------------------------------------------------
export { ComposeReanchorBanner } from './widgets/ComposeReanchorBanner';
export type { ComposeReanchorBannerProps } from './widgets/ComposeReanchorBanner';
export { ComposeReanchorConflictPanel } from './widgets/ComposeReanchorConflictPanel';
export type { ComposeReanchorConflictPanelProps } from './widgets/ComposeReanchorConflictPanel';
export { useComposeReanchor } from './widgets/useComposeReanchor';
export type {
  UseComposeReanchorOptions,
  UseComposeReanchorResult,
  ReanchorRequestArgs,
} from './widgets/useComposeReanchor';

// Word round-trip shuttle client callers (task 103 — gaps 3.1 / 3.4 / poll half of 3.5)
export {
  useComposePushAnnotations,
  useComposePullAnnotations,
  useComposeCheckChanges,
  anchoredAnnotationsToPriorAnchors,
  anchoredAnnotationsToDocxAnnotations,
  DocxTrackChangeKind,
} from './widgets/useComposeWordShuttle';
export type {
  UseComposeWordShuttleOptions,
  DocxAnnotationInput,
  RecoveredComment,
  RecoveredRevision,
  PullAnnotationsResult,
  CheckChangesResult,
  PushAnnotationsArgs,
  UseComposePushAnnotationsResult,
  PullAnnotationsArgs,
  UseComposePullAnnotationsResult,
  CheckChangesArgs,
  UseComposeCheckChangesResult,
} from './widgets/useComposeWordShuttle';
export type {
  ReanchorBand,
  ReanchoredAnnotation,
  ReanchorSummary,
  ReanchorAnnotationsResponse,
  PriorAnchorInput,
  ComposeReanchorReadyEvent,
  ReanchorResolution,
  ReanchorResolutionDecision,
} from './widgets/ComposeReanchor.types';

// Reducer / state types
export { composeWorkspaceReducer, INITIAL_STATE } from './widgets/ComposeWorkspace.types';
export type {
  ComposeWorkspaceStatus,
  ComposeCheckoutStatus,
  ComposeCheckoutLockedByInfo,
  ComposeWorkspaceState,
  ComposeWorkspaceAction,
} from './widgets/ComposeWorkspace.types';

// Hooks
export { useComposeBroadcastChannel, useComposeCheckoutLifecycle, useComposeHeartbeatGate } from './widgets/hooks';
export type {
  UseComposeBroadcastChannelResult,
  UseComposeCheckoutLifecycleOptions,
  UseComposeCheckoutLifecycleResult,
} from './widgets/hooks';

// -------------------------------------------------------------------------
// Compose launch context (Phase 7 task 092 → task 093 hoisted here from
// SpaarkeAi ThreePaneShell so LegalWorkspace's compose section factory can
// consume it via a unidirectional dependency).
// -------------------------------------------------------------------------
export { ComposeLaunchContext, useComposeLaunch } from './context/composeLaunchContext';
export type { ComposeLaunchContextValue } from './context/composeLaunchContext';

// -------------------------------------------------------------------------
// Compose action bridge (spaarkeai-compose-r2 task 046 / FR-13) — the
// cross-pane conduit that lets the inline AI toolbar (workspace pane) reach
// the Assistant pane's FIFO serial dispatch queue (FR-18) via a DIRECT
// `dispatchConsumer` call, NOT a PaneEventBus event (Spike 0 / design §7.2).
// Zero new PaneEventBus discriminants (ADR-030 closed union untouched).
// -------------------------------------------------------------------------
export {
  ComposeActionBridgeContext,
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  useRegisterComposeActionDispatcher,
} from './context/composeActionBridge';
export type { ComposeActionBridgeValue, ComposeActionBridgeProviderProps } from './context/composeActionBridge';

// -------------------------------------------------------------------------
// Data contracts (Phase 4 task 041 — promoted from SpaarkeAi in task 091)
//
// Flow contracts for the three-pane PaneEventBus wiring. Additive
// discriminants layered onto the existing four-channel bus in
// `@spaarke/ai-widgets`. Consumers dispatching / receiving Compose flows
// import the types from this barrel.
// -------------------------------------------------------------------------
export type {
  // Document pointer
  ComposeDocumentRef,
  // Transient upload-mount pointer (FR-03 / task 012)
  ComposeUploadRef,
  // Flow 1 — workspace → context (selection change)
  ComposeWorkspaceToContextFlow,
  ComposeSelection,
  // Flow 2 — workspace → assistant (selection-scoped)
  ComposeWorkspaceToAssistantFlow,
  // Flow 3 — context → workspace (jump-to-selection)
  ComposeContextToWorkspaceFlow,
  // Flow 4 — context → assistant (research prompts)
  ComposeContextToAssistantFlow,
  // Flow 5 — assistant → workspace (staged draft / apply intent)
  ComposeAssistantToWorkspaceFlow,
  // Flow 6 — assistant → context (citations / suggested clauses)
  ComposeAssistantToContextFlow,
  // R2 FR-29 — Compose-domain session-payload annotations (design.md §8; task 060)
  AnchoredAnnotationAnchor,
  AnchoredAnnotationProvenance,
  AnchoredAnnotation,
  DefinedTerm,
} from './types/compose-contracts';

// DOCX bridge helpers — exported for advanced consumers + tests. Most consumers should use ComposeEditor
// (which orchestrates these internally). R3 task 027: the `docx.js` byte-authoring exporters
// (`tipTapToDocxBytes`/`tipTapJsonToDocxBytes`/`buildRejectBaselineJson`) are REMOVED — the server owns
// all `.docx` authoring; the client sends the structured content model / edited-paragraph deltas below.
export {
  docxToTipTapHtml,
  stampParaIds,
  captureParaIdSnapshot,
  collectEditedParagraphs,
  buildContentModel,
} from './utils/docxBridge';
export type { MammothConversionResult, TipTapNode } from './utils/docxBridge';
