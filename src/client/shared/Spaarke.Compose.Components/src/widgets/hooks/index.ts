/**
 * Spaarke Compose — hooks barrel
 *
 * Project:   spaarkeai-compose-r1
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC)
 *
 * Re-exports the three workspace-level hooks extracted from the orchestrator:
 *   - useComposeBroadcastChannel    — cross-tab "focus-me" / "force-closed" signaling
 *   - useComposeCheckoutLifecycle   — SPE check-out probe + acquire + conflict handlers
 *   - useComposeHeartbeatGate       — 3-min heartbeat, gated on checkoutStatus === 'acquired' (FU-1 fix)
 */

export { useComposeBroadcastChannel } from './useComposeBroadcastChannel';
export type { UseComposeBroadcastChannelResult } from './useComposeBroadcastChannel';

export { useComposeCheckoutLifecycle } from './useComposeCheckoutLifecycle';
export type {
  UseComposeCheckoutLifecycleOptions,
  UseComposeCheckoutLifecycleResult,
} from './useComposeCheckoutLifecycle';

export { useComposeHeartbeatGate } from './useComposeHeartbeatGate';

export { usePendingRedline, resolveTargetSpans, collectMarkedRanges } from './usePendingRedline';
export type {
  UsePendingRedlineResult,
  PendingRedline,
  PendingRedlineError,
  MaterializeStatus,
  RedlineMatchMode,
  RedlineSpan,
  ResolveResult,
} from './usePendingRedline';

// task 072 (FR-35 Doc Q&A stretch) — ephemeral highlight over a cited span.
export { useDocQaHighlight } from './useDocQaHighlight';
export type { UseDocQaHighlightResult, ActiveQaHighlight, QaHighlightStatus } from './useDocQaHighlight';

// task 040 (FR-17) — in-editor find/replace: match search, decoration highlight, mark-preserving
// replace/replace-all.
export {
  useComposeFindReplace,
  ComposeFindReplaceExtension,
  findAllMatches,
  marksForReplacement,
} from './useComposeFindReplace';
export type { UseComposeFindReplaceResult, FindReplaceSpan } from './useComposeFindReplace';

// task 044 (FR-23) — comment-thread state over CommentAnchorMark: create/reply/resolve + the FR-25
// import seam.
export { useComposeCommentThreads } from './useComposeCommentThreads';
export type { UseComposeCommentThreadsResult, ComposeCommentRange } from './useComposeCommentThreads';
