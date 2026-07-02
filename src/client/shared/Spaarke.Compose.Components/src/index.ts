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
export type { ComposeEditorProps, ComposeEditorHandle, ComposeEditorDocumentRef } from './widgets/ComposeEditor';
export { ComposeFormatToolbar } from './widgets/ComposeFormatToolbar';
export type { ComposeFormatToolbarProps } from './widgets/ComposeFormatToolbar';

// -------------------------------------------------------------------------
// Workspace-level widgets (Phase 7 task 091 — moved from SpaarkeAi)
// -------------------------------------------------------------------------
export { ComposeWorkspace } from './widgets/ComposeWorkspace';
export type { ComposeWorkspaceProps } from './widgets/ComposeWorkspace';
export { ComposeToolbar } from './widgets/ComposeToolbar';
export type { ComposeToolbarProps, ComposeSummarizeRequestEvent } from './widgets/ComposeToolbar';
export { ComposeBannerStack } from './widgets/ComposeBannerStack';
export type { ComposeBannerStackProps } from './widgets/ComposeBannerStack';
export { ComposeEmptyState } from './widgets/ComposeEmptyState';
export type { ComposeEmptyStateProps } from './widgets/ComposeEmptyState';
export { ComposeConflictDialog } from './widgets/ComposeConflictDialog';

// Reducer / state types
export {
  composeWorkspaceReducer,
  INITIAL_STATE,
} from './widgets/ComposeWorkspace.types';
export type {
  ComposeWorkspaceStatus,
  ComposeCheckoutStatus,
  ComposeCheckoutLockedByInfo,
  ComposeWorkspaceState,
  ComposeWorkspaceAction,
} from './widgets/ComposeWorkspace.types';

// Hooks
export {
  useComposeBroadcastChannel,
  useComposeCheckoutLifecycle,
  useComposeHeartbeatGate,
} from './widgets/hooks';
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
export {
  ComposeLaunchContext,
  useComposeLaunch,
} from './context/composeLaunchContext';
export type { ComposeLaunchContextValue } from './context/composeLaunchContext';

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
} from './types/compose-contracts';

// DOCX bridge helpers — exported for advanced consumers + R2 tests. Most
// consumers should use ComposeEditor (which orchestrates these internally).
export { docxToTipTapHtml, tipTapToDocxBytes } from './utils/docxBridge';
export type { MammothConversionResult } from './utils/docxBridge';

// -------------------------------------------------------------------------
// SSE orchestrators (Phase 9 task 098 — Assistant-pane streaming)
//
// `executeComposeSummarize` consumes the `POST /api/compose/action/
// compose-summarize` SSE endpoint (task 097 backend) and forwards
// progress / result / error events through caller-supplied callbacks.
// Consumed by ConversationPane in SpaarkeAi (Path A + embedded modes).
// -------------------------------------------------------------------------
export { executeComposeSummarize } from './orchestrators/executeComposeSummarize';
export type {
  ExecuteComposeSummarizeInputs,
  ComposeSummarizeResult,
} from './orchestrators/executeComposeSummarize';
