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
// DOCX→editor read path: server-side projection (Sprk.Bff.Api `ComposeDocxProjectionBuilder`,
// DocumentFormat.OpenXml, MIT). Task 013 (spaarkeai-compose-fidelity-r4.5, F-2 "one reader")
// removed the client-side mammoth read path from Compose; `mammoth` ^1.8.0 (BSD-2-Clause)
// remains a repo dependency for other consumers (SprkChat/Notepad in @spaarke/ui-components).
// DOCX export path (TipTap → structured content model, server-rendered): docx ^9.0.3 (MIT).

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
  // NDA-REVIEW advisory comments (ai-advanced-capabilities-nda-r1 task 031) —
  // ComposeEditorHandle.placeAdvisoryComments' input/output shapes.
  AdvisoryCommentInput,
  AdvisoryCommentFailure,
  AdvisoryCommentPlacementResult,
} from './widgets/ComposeEditor';
export { ComposeFormatToolbar } from './widgets/ComposeFormatToolbar';
export type { ComposeFormatToolbarProps } from './widgets/ComposeFormatToolbar';
export { ComposeFindReplace } from './widgets/ComposeFindReplace';
export type { ComposeFindReplaceProps } from './widgets/ComposeFindReplace';

// FR-22 styles pane (task 043) — apply-existing-document-styles-only (SCOPE GUARD: no
// create/rename/delete/manage affordance; see ComposeStylesPane.tsx file-level JSDoc).
export { ComposeStylesPane } from './widgets/ComposeStylesPane';
export type { ComposeStylesPaneProps } from './widgets/ComposeStylesPane';
export {
  useComposeDocumentStyles,
  ComposePStyleExtension,
  COMPOSE_R3_STYLES,
  deriveDocumentStyles,
  applyComposeDocumentStyle,
} from './widgets/hooks/useComposeDocumentStyles';
export type { ComposeDocumentStyle, UseComposeDocumentStylesResult } from './widgets/hooks/useComposeDocumentStyles';
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

// R4 FR-07 — drift-proof AI generate-window bookmark (task 040, CAPTURE half). Drops a
// request-scoped ProseMirror `SelectionBookmark` at the selection on Generate, rebases it through
// concurrent user edits via the op-log `Mapping`, sends the target paraId as model context, and
// resolves it to the current position on return (the apply/validate half is task 041).
export { useAiGenerateBookmark, extractComposeOperations } from './widgets/hooks/useAiGenerateBookmark';
export type {
  AiGenerateBookmarkController,
  AiGenerateBookmarkContext,
  AiGenerateResolvedBookmark,
  AiGenerateReturnResult,
  AiGenerateOperationsResult,
  AiGenerateRefusedResult,
  AiGenerateUnknownResult,
  AiGenerateReviewReason,
  AiGenerateRefusalReason,
  UseAiGenerateBookmarkOptions,
} from './widgets/hooks/useAiGenerateBookmark';

// R4 FR-07 — validate-before-apply + fuzzy-as-comment last resort (task 041, the APPLY half of
// task 040's generate-window bookmark). Validates every AI-returned operation's paraId/offset
// against the live document; a valid anchor applies cleanly, an unvalidatable one surfaces as a
// review item (never silently placed, never silently dropped) via the shipped
// `AnnotationReanchorService` fuzzy-reanchor route (injected, not reimplemented).
export {
  useAiApplyValidation,
  validateComposeOperationAnchor,
  applyValidatedComposeOperation,
} from './widgets/hooks/useAiApplyValidation';
export type {
  AiApplyValidationController,
  UseAiApplyValidationOptions,
  AiApplyOutcome,
  AiApplyReviewItem,
  AiApplyFuzzyHint,
  AiApplyReviewReason,
  AiApplyReanchorFn,
  AnchorValidationReason,
  AnchorValidationResult,
} from './widgets/hooks/useAiApplyValidation';

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
// Comment threads (FR-23 / task 044) — the render target FR-25 (task 051)
// projects imported `RecoveredComment` threads into.
// -------------------------------------------------------------------------
export { ComposeCommentThread } from './widgets/ComposeCommentThread';
export type { ComposeCommentThreadProps, ComposeCommentPendingRange } from './widgets/ComposeCommentThread';
export { composeCommentThreadsToDocxAnnotations, findCommentAnchorRange } from './widgets/ComposeCommentThread.types';
export type {
  ComposeCommentAuthorStamp,
  ComposeCommentReply,
  ComposeCommentThreadModel,
} from './widgets/ComposeCommentThread.types';
export { useComposeCommentThreads } from './widgets/hooks/useComposeCommentThreads';
export type {
  UseComposeCommentThreadsResult,
  ComposeCommentRange,
  ComposeCommentThreadMetadata,
} from './widgets/hooks/useComposeCommentThreads';

// -------------------------------------------------------------------------
// Right-gutter comment layout (ai-advanced-capabilities-nda-r1 task 032, FR-16) — right-rail cards
// vertically aligned to each thread's LIVE anchor position (coordsAtPos), replacing/superseding the
// docked-list-only presentation for the NDA-REVIEW advisory-comment flow. Mounted inside ComposeEditor.
// -------------------------------------------------------------------------
export {
  ComposeCommentGutter,
  layoutCommentGutterCards,
  COMMENT_GUTTER_WIDTH_PX,
} from './widgets/ComposeCommentGutter';
export type { ComposeCommentGutterProps } from './widgets/ComposeCommentGutter';

// -------------------------------------------------------------------------
// Review-summary docked panel (ai-advanced-capabilities-nda-r1 task 030, FR-07) — the fuller
// advisory digest (overallRisk + cited flagged-section findings) rendered inside Compose. Single
// surface: no separate Analysis widget consumes this — mounted directly by ComposeWorkspace.
// Renamed NdaReviewSummaryPanel -> AgreementReviewSummaryPanel (ai-advanced-capabilities-agreements-r1
// task 010, pure rename); the old name is re-exported below as a deprecated alias (ADR-012 export
// stability) so any existing consumer import keeps resolving.
// -------------------------------------------------------------------------
export {
  AgreementReviewSummaryPanel,
  deriveOverallRisk,
  riskBadgeColor,
  NDA_REVIEW_DISCLAIMER_TEXT,
  /** @deprecated Use `AgreementReviewSummaryPanel` instead. */
  NdaReviewSummaryPanel,
} from './widgets/AgreementReviewSummaryPanel';
export type { AgreementReviewSummaryPanelProps, NdaReviewFindingSummary } from './widgets/AgreementReviewSummaryPanel';
/** @deprecated Use `AgreementReviewSummaryPanelProps` instead. */
export type { NdaReviewSummaryPanelProps } from './widgets/AgreementReviewSummaryPanel';

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
  // R3 FR-24 — import round-trip: existing Word revisions projected on Load (task 050)
  ImportedRevision,
  ImportedRevisionKind,
  // R3 FR-25 — import round-trip: existing Word comments projected on Load (task 051)
  ImportedComment,
} from './types/compose-contracts';

// -------------------------------------------------------------------------
// R4 FR-11 — the shared, versioned operation schema (task 003)
//
// The op-schema spine both client (ProseMirror step interceptor, task 020) and
// server (ComposeShadowPatchEngine, task 030) implement identically. Mirrors
// `Sprk.Bff.Api.Services.Compose.Operations.ComposeOperation`. Round-trips
// client → server → client without loss.
// -------------------------------------------------------------------------
export {
  COMPOSE_OPERATION_SCHEMA_VERSION,
  COMPOSE_OPERATION_TYPES,
  isComposeOperation,
  isComposeOperationLog,
} from './types/compose-operations';
export type {
  ComposeOperationType,
  ComposeRunPoint,
  ComposeRunRange,
  ComposeMarkType,
  ComposeBlockAttr,
  ComposeParagraphPosition,
  InsertTextOperation,
  DeleteRangeOperation,
  ReplaceRangeOperation,
  SetMarkOperation,
  ClearMarkOperation,
  SplitParagraphOperation,
  MergeParagraphOperation,
  InsertParagraphOperation,
  DeleteParagraphOperation,
  SetBlockAttrOperation,
  ComposeOperation,
  ComposeOperationLog,
  // ai-advanced-capabilities-nda-r1 task 040 — comment-export wiring fix
  ComposeAnchoredComment,
} from './types/compose-operations';

// -------------------------------------------------------------------------
// R4 FR-03 — the ProseMirror step→operation interceptor (task 020)
//
// The CLIENT half of the bridge: a read-only ProseMirror plugin (headless TipTap
// extension) that captures transaction STEPS as task-003 operations anchored
// `(paraId, runIndex, run-local-offset)` (D1/D2, I-6). Registered additively in
// ComposeEditor; the save/rebase path (task 022+) supplies `onOperations`.
// -------------------------------------------------------------------------
export {
  StepOperationInterceptor,
  COMPOSE_R4_STEP_INTERCEPTOR,
  stepOperationInterceptorPluginKey,
  STEP_INTERCEPTOR_IGNORE_META,
  resolveRunAnchor,
  runsOfBlock,
  runLocalPoint,
  classifyStep,
  buildBatchRevisionOp,
} from './widgets/stepOperationInterceptor';
export type {
  StepOperationInterceptorOptions,
  StepClassification,
  ComposeAnchor,
  OperationEmitContext,
} from './widgets/stepOperationInterceptor';

// R3 FR-24 import round-trip (task 050) — render recovered Word revisions as first-class,
// accept/reject-able insertion/deletion marks anchored by paraId (design §7). Exported for the
// ComposeEditor mount + direct tests; most consumers pass `importedRevisions` to <ComposeEditor>.
export { applyImportedRevisions, IMPORTED_LEDGER_PREFIX } from './widgets/importedRevisions';
export type { ApplyImportedRevisionsResult } from './widgets/importedRevisions';

// R3 FR-25 import round-trip (task 051) — group recovered Word comments into FR-23 comment threads +
// anchor them with the commentAnchor mark (design §7). Exported for the ComposeEditor mount + direct
// tests; most consumers pass `importedComments` to <ComposeEditor>.
export {
  groupImportedComments,
  applyImportedCommentAnchors,
  IMPORTED_COMMENT_THREAD_PREFIX,
} from './widgets/importedComments';
export type { ApplyImportedCommentAnchorsResult } from './widgets/importedComments';

// DOCX bridge helpers — exported for advanced consumers + tests. Most consumers should use ComposeEditor
// (which orchestrates these internally). R3 task 027: the `docx.js` byte-authoring exporters
// (`tipTapToDocxBytes`/`tipTapJsonToDocxBytes`/`buildRejectBaselineJson`) are REMOVED — the server owns
// all `.docx` authoring; the client sends the structured content model / operation-log deltas below.
// R4 task 023: `collectEditedParagraphs` (the paragraph-diff export) is REMOVED — dirty-save capture
// routes only through the step interceptor's operation log (`serializeOperationLog` on the editor
// handle); `buildContentModel` (the born-in-editor full-render path) is unchanged here (task 033).
// Task 013 (spaarkeai-compose-fidelity-r4.5, F-2): `docxToTipTapHtml` (the client mammoth reader) and
// its `MammothConversionResult` type are REMOVED — every entry path now hydrates a server projection.
export { stampParaIds, captureParaIdSnapshot, buildContentModel } from './utils/docxBridge';
export type { TipTapNode } from './utils/docxBridge';
