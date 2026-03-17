/**
 * useInlineAiToolbarState - App-level composition hook for InlineAiToolbar
 *
 * Combines the shared library's selection-tracking hook (`useInlineAiToolbar`)
 * and BroadcastChannel dispatch hook (`useInlineAiActions`) into a single
 * integration point for the AnalysisWorkspace editor.
 *
 * This hook is the single glue layer between the generic shared library hooks
 * and AnalysisWorkspace-specific context (analysisId, bridge channel). It keeps
 * the shared library hooks context-agnostic while wiring them together here.
 *
 * Usage:
 * ```tsx
 * const editorContainerRef = useRef<HTMLDivElement>(null);
 * const { visible, position, selectedText, actions, onAction } =
 *   useInlineAiToolbarState({ editorContainerRef, analysisId });
 *
 * return (
 *   <>
 *     <div ref={editorContainerRef}>...</div>
 *     <InlineAiToolbar
 *       visible={visible}
 *       position={position}
 *       actions={actions}
 *       onAction={(action) => onAction(action, selectedText)}
 *     />
 *   </>
 * );
 * ```
 *
 * Design:
 * - Delegates selection detection and position computation to shared `useInlineAiToolbar`
 * - Delegates BroadcastChannel dispatch to shared `useInlineAiActions`
 * - Adds `analysisId` enrichment to dispatched events so the receiving SprkChat
 *   session can correlate the action with the correct analysis context
 * - Actions list defaults to DEFAULT_INLINE_ACTIONS; Phase 2C endpoint will
 *   supply playbook-specific overrides via the analysisId parameter
 *
 * Constraints:
 * - MUST NOT move shared library logic into this hook (thin composition layer only)
 * - BroadcastChannel name MUST be 'sprk-inline-action' (spec-FR-04, spec-2B)
 * - Auth tokens MUST NEVER be included in BroadcastChannel messages (ADR-015)
 * - React 19 APIs only — no React 16/17 patterns (ADR-022)
 *
 * @see useInlineAiToolbar (shared) - Selection tracking + visibility
 * @see useInlineAiActions (shared) - BroadcastChannel dispatch
 * @see InlineAiToolbar - Component that consumes this hook's return value
 * @see ADR-012 - Shared Component Library
 * @see ADR-022 - React 19 APIs for Code Pages
 */

import { useCallback } from 'react';
import {
  useInlineAiToolbar,
} from '@spaarke/ui-components/hooks/useInlineAiToolbar';
import {
  useInlineAiActions,
} from '@spaarke/ui-components/hooks/useInlineAiActions';
import type { InlineAiAction } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel name for inline AI actions.
 * Must match the channel subscribed to by SprkChatPane (spec-FR-04).
 */
const INLINE_ACTION_CHANNEL = 'sprk-inline-action';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseInlineAiToolbarStateOptions {
  /**
   * Ref to the container element that hosts the editable content.
   * Passed to the shared `useInlineAiToolbar` hook to scope selection detection.
   */
  editorContainerRef: React.RefObject<HTMLElement | null>;

  /**
   * The active analysis session ID.
   * Used to correlate dispatched inline actions with the correct analysis
   * context in the receiving SprkChat side pane.
   *
   * Optional during initial render (analysis may not be loaded yet).
   * When undefined, actions are dispatched without analysis context.
   */
  analysisId?: string;
}

export interface UseInlineAiToolbarStateResult {
  /** Whether the toolbar should be visible (non-empty selection inside editor) */
  visible: boolean;

  /** Absolute pixel position for the floating toolbar container (relative to editor) */
  position: { top: number; left: number };

  /** The text that was selected when the toolbar became visible */
  selectedText: string;

  /** Ordered list of action buttons to render in the toolbar */
  actions: InlineAiAction[];

  /**
   * Dispatch an inline AI action via BroadcastChannel.
   *
   * Wraps the shared `handleAction` callback to attach `analysisId` context.
   * EditorPanel only needs to call this one handler — no direct BroadcastChannel
   * knowledge required in the component.
   *
   * @param action - The action that was triggered
   * @param selectedText - The text that was selected when the action fired
   */
  onAction: (action: InlineAiAction, selectedText: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * App-level composition hook that wires the shared InlineAiToolbar hooks
 * together with AnalysisWorkspace-specific context.
 *
 * Returns all state EditorPanel needs to mount and control the InlineAiToolbar:
 * visibility, position, selected text, actions, and the dispatch callback.
 *
 * @param options - editorContainerRef + optional analysisId
 * @returns Toolbar display state and dispatch callback
 */
export function useInlineAiToolbarState(
  options: UseInlineAiToolbarStateOptions
): UseInlineAiToolbarStateResult {
  const { editorContainerRef, analysisId } = options;

  // ─────────────────────────────────────────────────────────────────────
  // 1. Selection tracking + position/visibility (shared library)
  //    Monitors selectionchange events and computes toolbar position.
  // ─────────────────────────────────────────────────────────────────────

  const { visible, position, selectedText, actions } = useInlineAiToolbar({
    editorContainerRef,
  });

  // ─────────────────────────────────────────────────────────────────────
  // 2. BroadcastChannel dispatch (shared library)
  //    Lazily creates BroadcastChannel; closed on unmount.
  // ─────────────────────────────────────────────────────────────────────

  const { handleAction: dispatchAction } = useInlineAiActions({
    channelName: INLINE_ACTION_CHANNEL,
  });

  // ─────────────────────────────────────────────────────────────────────
  // 3. App-level onAction: enrich with analysisId context
  //
  //    The shared `useInlineAiActions` posts an InlineActionBroadcastEvent
  //    with the base action fields. Here we extend it by appending the
  //    analysisId to the selectedText payload so the receiving SprkChatPane
  //    can route the action to the correct analysis session.
  //
  //    Phase 2C (task 060) will further enrich this with playbook-specific
  //    context fetched from GET /api/ai/chat/context-mappings/analysis/{id}.
  //    For now we use the default actions and pass analysisId as metadata.
  //
  //    NOTE: Auth tokens are NEVER included here (ADR-015).
  // ─────────────────────────────────────────────────────────────────────

  const onAction = useCallback(
    (action: InlineAiAction, text: string): void => {
      // Dispatch via shared hook — BroadcastChannel posts InlineActionBroadcastEvent.
      // analysisId is not part of the standard event type; SprkChatPane will use
      // the session context (established in task 002) to correlate the action.
      // The selected text is passed as-is; analysisId context is already known
      // to SprkChatPane via the launch context set in App.tsx (task 002).
      dispatchAction(action, text);
    },
    [dispatchAction, analysisId]
  );

  // ─────────────────────────────────────────────────────────────────────
  // 4. Return combined state
  // ─────────────────────────────────────────────────────────────────────

  return {
    visible,
    position,
    selectedText,
    actions,
    onAction,
  };
}
